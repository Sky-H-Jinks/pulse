using ControlPlane.Api.Data;
using ControlPlane.Api.Domain;
using ControlPlane.Tests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Monitor = ControlPlane.Api.Domain.Monitor;

namespace ControlPlane.Tests.Scheduling;

/// Runs the real scheduler (1s tick) in its own host over the shared Postgres
/// container, against an in-process Kestrel server playing the monitored
/// targets.
[Collection(nameof(PostgresCollection))]
public sealed class CheckSchedulerTests(PulseApiFixture fixture) : IAsyncLifetime
{
    private static readonly TimeSpan PollBudget = TimeSpan.FromSeconds(30);

    private WebApplication _fakeTarget = null!;
    private PulseApiFactory _schedulerFactory = null!;
    private string _targetBaseUrl = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetDatabaseAsync();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _fakeTarget = builder.Build();
        _fakeTarget.MapGet("/ok", () => Results.Ok("ok"));
        _fakeTarget.MapGet("/error", () => Results.StatusCode(500));
        _fakeTarget.MapGet("/slow", async (CancellationToken ct) =>
        {
            // Longer than any test monitor's timeout, so the check times out first.
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return Results.Ok("slow");
        });
        await _fakeTarget.StartAsync();
        _targetBaseUrl = _fakeTarget.Urls.First();

        _schedulerFactory = new PulseApiFactory(fixture.ConnectionString, new Dictionary<string, string>
        {
            ["Scheduler:Enabled"] = "true",
            ["Scheduler:PeriodSeconds"] = "1",
        });

        // Building the host starts the scheduler BackgroundService.
        _ = _schedulerFactory.Services;
    }

    public async Task DisposeAsync()
    {
        // Stop the scheduler before the next test's Respawn reset. Bounded wait
        // because WebApplicationFactory disposal can hang with a running
        // BackgroundService (dotnet/aspnetcore#50622).
        await Task.WhenAny(_schedulerFactory.DisposeAsync().AsTask(), Task.Delay(TimeSpan.FromSeconds(10)));
        await _fakeTarget.DisposeAsync();
    }

    private async Task<Monitor> InsertMonitorAsync(string path, bool enabled = true)
    {
        using var scope = _schedulerFactory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PulseDbContext>();

        var monitor = new Monitor
        {
            Id = Guid.NewGuid(),
            Name = $"target {path}",
            Type = MonitorType.Http,
            Url = _targetBaseUrl + path,
            IntervalSeconds = 5,
            TimeoutSeconds = 1,
            ExpectedStatusCode = 200,
            Enabled = enabled,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Monitors.Add(monitor);
        await db.SaveChangesAsync();
        return monitor;
    }

    private async Task<CheckResult> PollForCheckResultAsync(Guid monitorId)
    {
        var deadline = DateTimeOffset.UtcNow + PollBudget;
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var scope = _schedulerFactory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
            var result = await db.CheckResults.AsNoTracking()
                .FirstOrDefaultAsync(r => r.MonitorId == monitorId);
            if (result is not null)
            {
                return result;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        throw new TimeoutException($"No check result for monitor {monitorId} within {PollBudget}.");
    }

    [Fact]
    public async Task HttpMonitor_OkEndpoint_StoresUpResult()
    {
        var monitor = await InsertMonitorAsync("/ok");

        var result = await PollForCheckResultAsync(monitor.Id);

        Assert.Equal(CheckStatus.Up, result.Status);
        Assert.Equal(200, result.StatusCode);
        Assert.NotNull(result.LatencyMs);
        Assert.True(result.LatencyMs >= 0);
        Assert.Null(result.Error);
        Assert.NotEqual(default, result.CheckedAt);
    }

    [Fact]
    public async Task HttpMonitor_ErrorEndpoint_StoresDownResult_WithErrorMessage()
    {
        var monitor = await InsertMonitorAsync("/error");

        var result = await PollForCheckResultAsync(monitor.Id);

        Assert.Equal(CheckStatus.Down, result.Status);
        Assert.Equal(500, result.StatusCode);
        Assert.NotNull(result.Error);
        Assert.Contains("200", result.Error);
        Assert.Contains("500", result.Error);
    }

    [Fact]
    public async Task HttpMonitor_SlowEndpoint_StoresDownResult_WithTimeoutMessage()
    {
        var monitor = await InsertMonitorAsync("/slow");

        var result = await PollForCheckResultAsync(monitor.Id);

        Assert.Equal(CheckStatus.Down, result.Status);
        Assert.Null(result.StatusCode);
        Assert.NotNull(result.Error);
        Assert.Contains("timed out", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DisabledMonitor_NoRowStored_WhileEnabledMonitorGetsRow()
    {
        var disabled = await InsertMonitorAsync("/ok", enabled: false);
        var enabled = await InsertMonitorAsync("/ok");

        // The enabled monitor getting its row proves at least one full
        // scheduler tick ran; the disabled one must still have none.
        await PollForCheckResultAsync(enabled.Id);

        using var scope = _schedulerFactory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
        Assert.False(await db.CheckResults.AnyAsync(r => r.MonitorId == disabled.Id));
    }
}
