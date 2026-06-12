using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Respawn;
using Respawn.Graph;
using Testcontainers.PostgreSql;

namespace ControlPlane.Tests.Infrastructure;

/// Boots the real app against the shared Postgres container, exactly as
/// docker compose does: migrations apply on startup. The scheduler is off by
/// default so tests stay deterministic; scheduler tests opt back in through
/// extraSettings.
public sealed class PulseApiFactory(string connectionString, IDictionary<string, string>? extraSettings = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("Scheduler:Enabled", "false");
        foreach (var (key, value) in extraSettings ?? new Dictionary<string, string>())
        {
            builder.UseSetting(key, value);
        }
    }
}

/// Collection fixture: one Postgres container and one app host for the whole
/// test run. Test classes call <see cref="ResetDatabaseAsync"/> from their own
/// IAsyncLifetime.InitializeAsync to get a clean database per test.
public sealed class PulseApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    private PulseApiFactory _factory = null!;
    private NpgsqlConnection _connection = null!;
    private Respawner _respawner = null!;

    public IServiceProvider Services => _factory.Services;

    /// For tests that need their own host over the same database (e.g. with
    /// the scheduler enabled).
    public string ConnectionString => _postgres.GetConnectionString();

    public HttpClient CreateClient() => _factory.CreateClient();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _factory = new PulseApiFactory(_postgres.GetConnectionString());

        // Building the host applies migrations (Program.cs does so on startup),
        // which must happen before Respawn snapshots the schema.
        _ = _factory.Services;

        _connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await _connection.OpenAsync();
        _respawner = await Respawner.CreateAsync(_connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            TablesToIgnore = [new Table("__EFMigrationsHistory")],
        });
    }

    public Task ResetDatabaseAsync() => _respawner.ResetAsync(_connection);

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

[CollectionDefinition(nameof(PostgresCollection))]
public sealed class PostgresCollection : ICollectionFixture<PulseApiFixture>;
