using ControlPlane.Api.Data;
using ControlPlane.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Monitor = ControlPlane.Api.Domain.Monitor;

namespace ControlPlane.Api.Scheduling;

/// Thin PeriodicTimer loop: each tick selects due HTTP monitors and runs
/// their checks with bounded concurrency. Tick and per-check failures are
/// logged and never kill the service.
public class CheckScheduler(
    IServiceScopeFactory scopeFactory,
    DueMonitorSelector dueSelector,
    CheckRunner checkRunner,
    IOptions<SchedulerOptions> options,
    TimeProvider timeProvider,
    ILogger<CheckScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (!opts.Enabled)
        {
            logger.LogInformation("Check scheduler is disabled via Scheduler:Enabled.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(opts.PeriodSeconds), timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await RunTickAsync(opts, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Scheduler tick failed; retrying on the next tick.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown.
        }
    }

    private async Task RunTickAsync(SchedulerOptions opts, CancellationToken stoppingToken)
    {
        List<(Monitor Monitor, DateTimeOffset? LastCheckedAt)> candidates;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
            candidates = (await db.Monitors
                    .AsNoTracking()
                    .Where(m => m.Enabled && m.Type == MonitorType.Http)
                    .Select(m => new
                    {
                        Monitor = m,
                        LastCheckedAt = db.CheckResults
                            .Where(r => r.MonitorId == m.Id)
                            .OrderByDescending(r => r.CheckedAt)
                            .Select(r => (DateTimeOffset?)r.CheckedAt)
                            .FirstOrDefault(),
                    })
                    .ToListAsync(stoppingToken))
                .Select(x => (x.Monitor, x.LastCheckedAt))
                .ToList();
        }

        var due = candidates.Where(c => dueSelector.IsDue(c.Monitor, c.LastCheckedAt)).ToList();
        if (due.Count == 0)
        {
            return;
        }

        await Parallel.ForEachAsync(
            due,
            new ParallelOptions { MaxDegreeOfParallelism = opts.MaxConcurrency, CancellationToken = stoppingToken },
            async (candidate, ct) =>
            {
                try
                {
                    var result = await checkRunner.ExecuteAsync(candidate.Monitor, ct);

                    // Own scope per check: DbContext is not thread-safe.
                    using var scope = scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<PulseDbContext>();
                    db.CheckResults.Add(result);
                    await db.SaveChangesAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Check failed for monitor {MonitorId} ({Url}).",
                        candidate.Monitor.Id, candidate.Monitor.Url);
                }
            });
    }
}
