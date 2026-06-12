using ControlPlane.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<PulseDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// The control-plane owns the schema; migrations apply on startup so a fresh
// `docker compose up` needs no manual step. compose gates startup on the
// Postgres healthcheck.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<PulseDbContext>().Database.Migrate();
}

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.Run();

// Exposes the implicit Program class to WebApplicationFactory in tests.
public partial class Program;
