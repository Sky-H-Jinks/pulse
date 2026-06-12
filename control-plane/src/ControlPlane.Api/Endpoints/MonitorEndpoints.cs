using ControlPlane.Api.Data;
using Microsoft.EntityFrameworkCore;
using Monitor = ControlPlane.Api.Domain.Monitor;

namespace ControlPlane.Api.Endpoints;

public static class MonitorEndpoints
{
    public static IEndpointRouteBuilder MapMonitorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/monitors").WithTags("Monitors");

        group.MapGet("/", async (PulseDbContext db) =>
            {
                var monitors = await db.Monitors.AsNoTracking()
                    .OrderBy(m => m.CreatedAt)
                    .ToListAsync();

                return Results.Ok(monitors.Select(ToResponse));
            })
            .WithName("GetMonitors")
            .Produces<IEnumerable<MonitorResponse>>();

        group.MapPost("/", async (CreateMonitorRequest request, PulseDbContext db) =>
            {
                if (ValidationHelper.Validate(request) is { } invalid)
                {
                    return invalid;
                }

                // Truncated to microseconds so the created response matches the
                // value Postgres stores (timestamptz has microsecond precision).
                var now = DateTimeOffset.UtcNow;
                now = now.AddTicks(-(now.Ticks % (TimeSpan.TicksPerMillisecond / 1000)));

                var monitor = new Monitor
                {
                    Id = Guid.NewGuid(),
                    Name = request.Name!,
                    Type = request.Type,
                    Url = request.Url!,
                    IntervalSeconds = request.IntervalSeconds,
                    TimeoutSeconds = request.TimeoutSeconds,
                    ExpectedStatusCode = request.ExpectedStatusCode,
                    FailureThreshold = request.FailureThreshold,
                    Enabled = request.Enabled,
                    CreatedAt = now,
                };

                db.Monitors.Add(monitor);
                await db.SaveChangesAsync();

                return Results.Created($"/api/monitors/{monitor.Id}", ToResponse(monitor));
            })
            .WithName("CreateMonitor")
            .Produces<MonitorResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        group.MapGet("/{id:guid}", async (Guid id, PulseDbContext db) =>
                await db.Monitors.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id) is { } monitor
                    ? Results.Ok(ToResponse(monitor))
                    : Results.NotFound())
            .WithName("GetMonitorById")
            .Produces<MonitorResponse>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}", async (Guid id, UpdateMonitorRequest request, PulseDbContext db) =>
            {
                if (ValidationHelper.Validate(request) is { } invalid)
                {
                    return invalid;
                }

                var monitor = await db.Monitors.SingleOrDefaultAsync(m => m.Id == id);
                if (monitor is null)
                {
                    return Results.NotFound();
                }

                monitor.Name = request.Name!;
                monitor.Type = request.Type;
                monitor.Url = request.Url!;
                monitor.IntervalSeconds = request.IntervalSeconds;
                monitor.TimeoutSeconds = request.TimeoutSeconds;
                monitor.ExpectedStatusCode = request.ExpectedStatusCode;
                monitor.FailureThreshold = request.FailureThreshold;
                monitor.Enabled = request.Enabled;
                await db.SaveChangesAsync();

                return Results.Ok(ToResponse(monitor));
            })
            .WithName("UpdateMonitor")
            .Produces<MonitorResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        group.MapDelete("/{id:guid}", async (Guid id, PulseDbContext db) =>
            {
                var monitor = await db.Monitors.SingleOrDefaultAsync(m => m.Id == id);
                if (monitor is null)
                {
                    return Results.NotFound();
                }

                db.Monitors.Remove(monitor);
                await db.SaveChangesAsync();

                return Results.NoContent();
            })
            .WithName("DeleteMonitor")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static MonitorResponse ToResponse(Monitor monitor) => new(
        monitor.Id,
        monitor.Name,
        monitor.Type,
        monitor.Url,
        monitor.IntervalSeconds,
        monitor.TimeoutSeconds,
        monitor.ExpectedStatusCode,
        monitor.FailureThreshold,
        monitor.Enabled,
        monitor.CreatedAt);
}
