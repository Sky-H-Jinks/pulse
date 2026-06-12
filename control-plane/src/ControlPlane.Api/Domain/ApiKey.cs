namespace ControlPlane.Api.Domain;

public class ApiKey
{
    public Guid Id { get; set; }
    public required string Name { get; set; }

    /// SHA-256 of the key; the plaintext is shown once at issuance and never stored.
    public required string KeyHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
