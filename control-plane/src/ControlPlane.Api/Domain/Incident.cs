namespace ControlPlane.Api.Domain;

public class Incident
{
    public Guid Id { get; set; }
    public Guid MonitorId { get; set; }
    public Monitor? Monitor { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }

    /// AI-generated post-mortem, populated in Phase 4.
    public string? Summary { get; set; }
}
