using System.Text.RegularExpressions;
using ControlPlane.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ControlPlane.Api.Data;

public class PulseDbContext(DbContextOptions<PulseDbContext> options) : DbContext(options)
{
    public DbSet<Domain.Monitor> Monitors => Set<Domain.Monitor>();
    public DbSet<CheckResult> CheckResults => Set<CheckResult>();
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<Metric> Metrics => Set<Metric>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Domain.Monitor>(e =>
        {
            e.ToTable("monitors");
            e.Property(m => m.Name).HasMaxLength(200);
            e.Property(m => m.Url).HasMaxLength(2048);
            e.Property(m => m.Type).HasConversion<string>().HasMaxLength(16);
        });

        modelBuilder.Entity<CheckResult>(e =>
        {
            e.ToTable("check_results");
            e.Property(c => c.Status).HasConversion<string>().HasMaxLength(8);
            e.HasIndex(c => new { c.MonitorId, c.CheckedAt });
            e.HasOne(c => c.Monitor)
                .WithMany(m => m.CheckResults)
                .HasForeignKey(c => c.MonitorId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Incident>(e =>
        {
            e.ToTable("incidents");
            e.HasIndex(i => new { i.MonitorId, i.OpenedAt });
            e.HasOne(i => i.Monitor)
                .WithMany(m => m.Incidents)
                .HasForeignKey(i => i.MonitorId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApiKey>(e =>
        {
            e.ToTable("api_keys");
            e.Property(k => k.Name).HasMaxLength(200);
            e.Property(k => k.KeyHash).HasMaxLength(128);
            e.HasIndex(k => k.KeyHash).IsUnique();
        });

        modelBuilder.Entity<Metric>(e =>
        {
            e.ToTable("metrics");
            e.Property(m => m.AgentId).HasMaxLength(64);
            e.Property(m => m.Host).HasMaxLength(255);
            e.Property(m => m.Name).HasMaxLength(255);
            e.Property(m => m.Labels).HasColumnType("jsonb");
            e.HasIndex(m => new { m.AgentId, m.At });
            e.HasIndex(m => new { m.Name, m.At });
        });

        // Postgres is the contract other services read, so names are snake_case
        // rather than EF's default PascalCase.
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
                property.SetColumnName(ToSnakeCase(property.Name));
            foreach (var key in entity.GetKeys())
                key.SetName(ToSnakeCase(key.GetName()!));
            foreach (var foreignKey in entity.GetForeignKeys())
                foreignKey.SetConstraintName(ToSnakeCase(foreignKey.GetConstraintName()!));
            foreach (var index in entity.GetIndexes())
                index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName()!));
        }
    }

    private static string ToSnakeCase(string name) =>
        Regex.Replace(name, "(?<=[a-z0-9])([A-Z])", "_$1").ToLowerInvariant();
}
