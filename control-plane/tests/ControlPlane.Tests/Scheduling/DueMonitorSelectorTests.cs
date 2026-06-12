using ControlPlane.Api.Scheduling;
using Microsoft.Extensions.Time.Testing;
using Monitor = ControlPlane.Api.Domain.Monitor;

namespace ControlPlane.Tests.Scheduling;

public sealed class DueMonitorSelectorTests
{
    private static readonly DateTimeOffset Start = new(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Start);
    private readonly DueMonitorSelector _selector;

    public DueMonitorSelectorTests() => _selector = new DueMonitorSelector(_time);

    private static Monitor MonitorWithInterval(int intervalSeconds) => new()
    {
        Name = "monitor",
        Url = "https://example.com/health",
        IntervalSeconds = intervalSeconds,
    };

    [Fact]
    public void NeverChecked_IsDue()
    {
        Assert.True(_selector.IsDue(MonitorWithInterval(60), lastCheckedAt: null));
    }

    [Fact]
    public void CheckedJustNow_IsNotDue()
    {
        Assert.False(_selector.IsDue(MonitorWithInterval(60), Start));
    }

    [Fact]
    public void CheckedExactlyIntervalAgo_IsDue()
    {
        Assert.True(_selector.IsDue(MonitorWithInterval(60), Start.AddSeconds(-60)));
    }

    [Fact]
    public void CheckedLongerAgo_IsDue()
    {
        Assert.True(_selector.IsDue(MonitorWithInterval(60), Start.AddSeconds(-90)));
    }

    [Fact]
    public void AdvancingTime_MovesMonitorToDue()
    {
        var monitor = MonitorWithInterval(60);

        Assert.False(_selector.IsDue(monitor, Start));

        _time.Advance(TimeSpan.FromSeconds(60));

        Assert.True(_selector.IsDue(monitor, Start));
    }
}
