using Monitor = ControlPlane.Api.Domain.Monitor;

namespace ControlPlane.Api.Scheduling;

/// Decides whether a monitor is overdue for a check. Pure logic over
/// TimeProvider so it is unit-testable with FakeTimeProvider.
public class DueMonitorSelector(TimeProvider timeProvider)
{
    public bool IsDue(Monitor monitor, DateTimeOffset? lastCheckedAt) =>
        lastCheckedAt is null ||
        timeProvider.GetUtcNow() - lastCheckedAt.Value >= TimeSpan.FromSeconds(monitor.IntervalSeconds);
}
