using ControlPlane.Api.Domain;
using Monitor = ControlPlane.Api.Domain.Monitor;

namespace ControlPlane.Api.Scheduling;

/// Executes a single HTTP check. The monitor's TimeoutSeconds is the only
/// timeout authority (the client's own timeout is disabled), so a timeout is
/// always distinguishable from a connection failure.
public class CheckRunner(IHttpClientFactory httpClientFactory, TimeProvider timeProvider)
{
    public async Task<CheckResult> ExecuteAsync(Monitor monitor, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(monitor.TimeoutSeconds));

        var client = httpClientFactory.CreateClient();
        client.Timeout = Timeout.InfiniteTimeSpan;

        var start = timeProvider.GetTimestamp();
        try
        {
            // ResponseHeadersRead and an immediate dispose: the body is never
            // downloaded, so latency means time-to-first-byte and a check
            // doesn't pull a monitored page's full content every interval.
            using var response = await client.GetAsync(
                monitor.Url, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);

            var latencyMs = (int)timeProvider.GetElapsedTime(start).TotalMilliseconds;
            var actualStatus = (int)response.StatusCode;

            return actualStatus == monitor.ExpectedStatusCode
                ? NewResult(monitor, CheckStatus.Up, actualStatus, latencyMs, error: null)
                : NewResult(monitor, CheckStatus.Down, actualStatus, latencyMs,
                    $"Expected status {monitor.ExpectedStatusCode}, got {actualStatus}.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return NewResult(monitor, CheckStatus.Down, statusCode: null, latencyMs: null,
                $"Timed out after {monitor.TimeoutSeconds}s.");
        }
        catch (HttpRequestException ex)
        {
            return NewResult(monitor, CheckStatus.Down, statusCode: null, latencyMs: null, ex.Message);
        }
    }

    private CheckResult NewResult(Monitor monitor, CheckStatus status, int? statusCode, int? latencyMs, string? error)
    {
        // Truncated to microseconds, same precedent as Monitor.CreatedAt.
        var now = timeProvider.GetUtcNow();
        now = now.AddTicks(-(now.Ticks % (TimeSpan.TicksPerMillisecond / 1000)));

        return new CheckResult
        {
            MonitorId = monitor.Id,
            Status = status,
            StatusCode = statusCode,
            LatencyMs = latencyMs,
            Error = error,
            CheckedAt = now,
        };
    }
}
