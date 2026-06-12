namespace ControlPlane.Api.Domain;

/// One row per agent sample. Empty until Phase 2; written by the Rust ingest
/// service from Phase 3 on. Shape mirrors the payload contract in agent/README.md.
public class Metric
{
    public long Id { get; set; }
    public required string AgentId { get; set; }
    public required string Host { get; set; }
    public required string Name { get; set; }
    public double Value { get; set; }
    public Dictionary<string, string>? Labels { get; set; }
    public DateTimeOffset At { get; set; }
}
