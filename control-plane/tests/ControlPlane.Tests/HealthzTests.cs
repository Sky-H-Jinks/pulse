using System.Net;
using ControlPlane.Api.Data;
using ControlPlane.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ControlPlane.Tests;

[Collection(nameof(PostgresCollection))]
public sealed class HealthzTests(PulseApiFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Healthz_returns_200_and_migrations_are_applied()
    {
        using var client = fixture.CreateClient();

        var response = await client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PulseDbContext>();

        var applied = await db.Database.GetAppliedMigrationsAsync();
        Assert.Contains(applied, m => m.EndsWith("InitialCreate"));

        // The schema is actually usable, not just recorded in the history table.
        Assert.Equal(0, await db.Monitors.CountAsync());
    }
}
