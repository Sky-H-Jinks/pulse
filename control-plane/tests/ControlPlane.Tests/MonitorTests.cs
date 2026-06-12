using System.Net;
using System.Net.Http.Json;
using ControlPlane.Api.Domain;
using ControlPlane.Api.Endpoints;
using ControlPlane.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace ControlPlane.Tests;

[Collection(nameof(PostgresCollection))]
public sealed class MonitorTests(PulseApiFixture fixture) : IAsyncLifetime
{
    private readonly HttpClient _client = fixture.CreateClient();

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    private static CreateMonitorRequest ValidCreate() => new()
    {
        Name = "Example uptime",
        Type = MonitorType.Http,
        Url = "https://example.com/health",
        IntervalSeconds = 60,
        TimeoutSeconds = 10,
        ExpectedStatusCode = 200,
        FailureThreshold = 3,
        Enabled = true,
    };

    private static UpdateMonitorRequest ValidUpdate() => new()
    {
        Name = "Renamed monitor",
        Type = MonitorType.Http,
        Url = "https://example.com/live",
        IntervalSeconds = 120,
        TimeoutSeconds = 15,
        ExpectedStatusCode = 204,
        FailureThreshold = 5,
        Enabled = false,
    };

    private async Task<MonitorResponse> PostValidAsync(string name = "Example uptime")
    {
        var request = ValidCreate();
        request.Name = name;
        var response = await _client.PostAsJsonAsync("/api/monitors", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<MonitorResponse>();
        Assert.NotNull(created);
        return created!;
    }

    // --- Happy-path CRUD round trip ---

    [Fact]
    public async Task Post_CreatesMonitor_And_Returns201WithLocation()
    {
        var request = ValidCreate();
        var response = await _client.PostAsJsonAsync("/api/monitors", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<MonitorResponse>();
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created!.Id);
        Assert.NotEqual(default, created.CreatedAt);
        Assert.Equal($"/api/monitors/{created.Id}", response.Headers.Location?.ToString());

        Assert.Equal(request.Name, created.Name);
        Assert.Equal(request.Type, created.Type);
        Assert.Equal(request.Url, created.Url);
        Assert.Equal(request.IntervalSeconds, created.IntervalSeconds);
        Assert.Equal(request.TimeoutSeconds, created.TimeoutSeconds);
        Assert.Equal(request.ExpectedStatusCode, created.ExpectedStatusCode);
        Assert.Equal(request.FailureThreshold, created.FailureThreshold);
        Assert.Equal(request.Enabled, created.Enabled);
    }

    [Fact]
    public async Task GetById_AfterPost_Returns200WithCorrectFields()
    {
        var created = await PostValidAsync();

        var fetched = await _client.GetFromJsonAsync<MonitorResponse>($"/api/monitors/{created.Id}");

        Assert.Equal(created, fetched);
    }

    [Fact]
    public async Task GetList_AfterTwoPosts_ReturnsBothOrderedByCreatedAt()
    {
        var first = await PostValidAsync("first");
        var second = await PostValidAsync("second");

        var list = await _client.GetFromJsonAsync<List<MonitorResponse>>("/api/monitors");

        Assert.NotNull(list);
        Assert.Equal(2, list!.Count);
        Assert.Equal(first.Id, list[0].Id);
        Assert.Equal(second.Id, list[1].Id);
    }

    [Fact]
    public async Task Put_ReplacesMonitor_And_Returns200()
    {
        var created = await PostValidAsync();
        var update = ValidUpdate();

        var response = await _client.PutAsJsonAsync($"/api/monitors/{created.Id}", update);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var fetched = await _client.GetFromJsonAsync<MonitorResponse>($"/api/monitors/{created.Id}");
        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched!.Id);
        Assert.Equal(created.CreatedAt, fetched.CreatedAt);
        Assert.Equal(update.Name, fetched.Name);
        Assert.Equal(update.Url, fetched.Url);
        Assert.Equal(update.IntervalSeconds, fetched.IntervalSeconds);
        Assert.Equal(update.TimeoutSeconds, fetched.TimeoutSeconds);
        Assert.Equal(update.ExpectedStatusCode, fetched.ExpectedStatusCode);
        Assert.Equal(update.FailureThreshold, fetched.FailureThreshold);
        Assert.Equal(update.Enabled, fetched.Enabled);
    }

    [Fact]
    public async Task Delete_RemovesMonitor_And_Returns204()
    {
        var created = await PostValidAsync();

        var response = await _client.DeleteAsync($"/api/monitors/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var getAfter = await _client.GetAsync($"/api/monitors/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getAfter.StatusCode);

        var list = await _client.GetFromJsonAsync<List<MonitorResponse>>("/api/monitors");
        Assert.NotNull(list);
        Assert.Empty(list!);
    }

    // --- 404 cases ---

    [Fact]
    public async Task GetById_UnknownId_Returns404()
    {
        var response = await _client.GetAsync($"/api/monitors/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_UnknownId_Returns404()
    {
        var response = await _client.PutAsJsonAsync($"/api/monitors/{Guid.NewGuid()}", ValidUpdate());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_UnknownId_Returns404()
    {
        var response = await _client.DeleteAsync($"/api/monitors/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- Validation failures ---

    private async Task AssertPostRejected(CreateMonitorRequest request, string expectedErrorKey)
    {
        var response = await _client.PostAsJsonAsync("/api/monitors", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains(expectedErrorKey, problem!.Errors.Keys);

        // Nothing was persisted.
        var list = await _client.GetFromJsonAsync<List<MonitorResponse>>("/api/monitors");
        Assert.NotNull(list);
        Assert.Empty(list!);
    }

    [Fact]
    public async Task Post_MissingName_Returns400()
    {
        var request = ValidCreate();
        request.Name = null;
        await AssertPostRejected(request, "Name");
    }

    [Fact]
    public async Task Post_EmptyName_Returns400()
    {
        var request = ValidCreate();
        request.Name = "";
        await AssertPostRejected(request, "Name");
    }

    [Fact]
    public async Task Post_NameTooLong_Returns400()
    {
        var request = ValidCreate();
        request.Name = new string('a', 201);
        await AssertPostRejected(request, "Name");
    }

    [Fact]
    public async Task Post_MissingUrl_Returns400()
    {
        var request = ValidCreate();
        request.Url = null;
        await AssertPostRejected(request, "Url");
    }

    [Fact]
    public async Task Post_RelativeUrl_Returns400()
    {
        var request = ValidCreate();
        request.Url = "/path/only";
        await AssertPostRejected(request, "Url");
    }

    [Fact]
    public async Task Post_NonHttpUrl_Returns400()
    {
        var request = ValidCreate();
        request.Url = "ftp://example.com";
        await AssertPostRejected(request, "Url");
    }

    [Fact]
    public async Task Post_UrlTooLong_Returns400()
    {
        var request = ValidCreate();
        request.Url = "https://example.com/" + new string('a', 2048);
        await AssertPostRejected(request, "Url");
    }

    [Fact]
    public async Task Post_IntervalTooLow_Returns400()
    {
        var request = ValidCreate();
        request.IntervalSeconds = 4;
        request.TimeoutSeconds = 1;
        await AssertPostRejected(request, "IntervalSeconds");
    }

    [Fact]
    public async Task Post_IntervalTooHigh_Returns400()
    {
        var request = ValidCreate();
        request.IntervalSeconds = 86401;
        await AssertPostRejected(request, "IntervalSeconds");
    }

    [Fact]
    public async Task Post_TimeoutTooLow_Returns400()
    {
        var request = ValidCreate();
        request.TimeoutSeconds = 0;
        await AssertPostRejected(request, "TimeoutSeconds");
    }

    [Fact]
    public async Task Post_TimeoutTooHigh_Returns400()
    {
        var request = ValidCreate();
        request.IntervalSeconds = 400;
        request.TimeoutSeconds = 301;
        await AssertPostRejected(request, "TimeoutSeconds");
    }

    [Fact]
    public async Task Post_TimeoutGreaterThanOrEqualToInterval_Returns400()
    {
        var request = ValidCreate();
        request.IntervalSeconds = 10;
        request.TimeoutSeconds = 10;
        await AssertPostRejected(request, "TimeoutSeconds");
    }

    [Fact]
    public async Task Post_BadStatusCode_TooLow_Returns400()
    {
        var request = ValidCreate();
        request.ExpectedStatusCode = 99;
        await AssertPostRejected(request, "ExpectedStatusCode");
    }

    [Fact]
    public async Task Post_BadStatusCode_TooHigh_Returns400()
    {
        var request = ValidCreate();
        request.ExpectedStatusCode = 600;
        await AssertPostRejected(request, "ExpectedStatusCode");
    }

    [Fact]
    public async Task Post_FailureThresholdTooLow_Returns400()
    {
        var request = ValidCreate();
        request.FailureThreshold = 0;
        await AssertPostRejected(request, "FailureThreshold");
    }

    [Fact]
    public async Task Post_FailureThresholdTooHigh_Returns400()
    {
        var request = ValidCreate();
        request.FailureThreshold = 11;
        await AssertPostRejected(request, "FailureThreshold");
    }

    [Fact]
    public async Task Put_InvalidBody_Returns400()
    {
        var created = await PostValidAsync();

        var update = ValidUpdate();
        update.Name = null;
        var response = await _client.PutAsJsonAsync($"/api/monitors/{created.Id}", update);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // The monitor is untouched.
        var fetched = await _client.GetFromJsonAsync<MonitorResponse>($"/api/monitors/{created.Id}");
        Assert.Equal(created, fetched);
    }

    // --- Respawn isolation proof: both tests pass in either order only if the
    // --- database is reset between tests.

    [Fact]
    public async Task RespawnIsolation_FirstTestStartsClean()
    {
        var list = await _client.GetFromJsonAsync<List<MonitorResponse>>("/api/monitors");
        Assert.Empty(list!);

        await PostValidAsync("isolation-a");

        list = await _client.GetFromJsonAsync<List<MonitorResponse>>("/api/monitors");
        Assert.Single(list!);
    }

    [Fact]
    public async Task RespawnIsolation_SecondTestStartsClean()
    {
        var list = await _client.GetFromJsonAsync<List<MonitorResponse>>("/api/monitors");
        Assert.Empty(list!);

        await PostValidAsync("isolation-b");

        list = await _client.GetFromJsonAsync<List<MonitorResponse>>("/api/monitors");
        Assert.Single(list!);
    }
}
