namespace ControlPlane.Api.Domain;

public enum MonitorType
{
    Http,
    Tcp,
}

public class Monitor
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public MonitorType Type { get; set; } = MonitorType.Http;
    public required string Url { get; set; }
    public int IntervalSeconds { get; set; } = 60;
    public int TimeoutSeconds { get; set; } = 10;
    public int ExpectedStatusCode { get; set; } = 200;

    /// Consecutive failures before an incident opens.
    public int FailureThreshold { get; set; } = 3;

    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }

    public List<CheckResult> CheckResults { get; set; } = [];
    public List<Incident> Incidents { get; set; } = [];
}
