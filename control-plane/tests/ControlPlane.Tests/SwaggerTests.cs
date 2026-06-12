using ControlPlane.Tests.Infrastructure;

namespace ControlPlane.Tests;

[Collection(nameof(PostgresCollection))]
public sealed class SwaggerTests(PulseApiFixture fixture)
{
    [Fact]
    public async Task Swagger_doc_includes_monitor_endpoints()
    {
        using var client = fixture.CreateClient();

        var doc = await client.GetStringAsync("/swagger/v1/swagger.json");

        Assert.Contains("\"/api/monitors\"", doc);
        Assert.Contains("\"/api/monitors/{id}\"", doc);
        foreach (var operation in (string[])["GetMonitors", "CreateMonitor", "GetMonitorById", "UpdateMonitor", "DeleteMonitor"])
        {
            Assert.Contains(operation, doc);
        }
    }
}
