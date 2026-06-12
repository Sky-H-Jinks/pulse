using System.ComponentModel.DataAnnotations;
using ControlPlane.Api.Domain;

namespace ControlPlane.Api.Endpoints;

/// Shared shape for create and update. Strings are nullable so a missing
/// field reaches [Required] validation and yields a 400 ValidationProblem
/// instead of failing JSON deserialization.
public abstract class MonitorRequest
{
    [Required(AllowEmptyStrings = false)]
    [StringLength(200, MinimumLength = 1)]
    public string? Name { get; set; }

    public MonitorType Type { get; set; } = MonitorType.Http;

    [Required(AllowEmptyStrings = false)]
    [StringLength(2048)]
    public string? Url { get; set; }

    [Range(5, 86400)]
    public int IntervalSeconds { get; set; } = 60;

    [Range(1, 300)]
    public int TimeoutSeconds { get; set; } = 10;

    [Range(100, 599)]
    public int ExpectedStatusCode { get; set; } = 200;

    [Range(1, 10)]
    public int FailureThreshold { get; set; } = 3;

    public bool Enabled { get; set; } = true;
}

public sealed class CreateMonitorRequest : MonitorRequest;

public sealed class UpdateMonitorRequest : MonitorRequest;

public sealed record MonitorResponse(
    Guid Id,
    string Name,
    MonitorType Type,
    string Url,
    int IntervalSeconds,
    int TimeoutSeconds,
    int ExpectedStatusCode,
    int FailureThreshold,
    bool Enabled,
    DateTimeOffset CreatedAt);
