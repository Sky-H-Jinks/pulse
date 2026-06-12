namespace ControlPlane.Api.Domain;

public enum CheckStatus
{
    Up,
    Down,
}

public class CheckResult
{
    public long Id { get; set; }
    public Guid MonitorId { get; set; }
    public Monitor? Monitor { get; set; }
    public CheckStatus Status { get; set; }
    public int? StatusCode { get; set; }
    public int? LatencyMs { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CheckedAt { get; set; }
}
