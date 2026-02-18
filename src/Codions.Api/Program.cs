using Microsoft.EntityFrameworkCore;
using Codions.Contracts.Enums;
using Codions.Contracts.Interfaces;
using Codions.Contracts.Models;
using Codions.Core;
using Codions.Infrastructure.Data;
using Codions.Infrastructure.Storage;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var workspacesPath = builder.Configuration.GetValue<string>("Docker:WorkspacesPath") ?? "data/workspaces";
// Use a shared absolute path so API and Worker see the same artifact directory (job-spec.json, context-pack.json)
if (!Path.IsPathRooted(workspacesPath))
{
    workspacesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Codions",
        workspacesPath.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
}

workspacesPath = Path.GetFullPath(workspacesPath);
Directory.CreateDirectory(workspacesPath);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddScoped<IJobRepository, JobRepository>();
builder.Services.AddSingleton<IArtifactStore>(new FileArtifactStore(workspacesPath));
builder.Services.AddSingleton<ModelTierRouter>();
builder.Services.AddSingleton<ContextPackBuilder>();
builder.Services.AddSingleton(new RunProfileDefaults
{
    MaxAgentSteps = builder.Configuration.GetValue("Defaults:MaxAgentSteps", 16),
    MaxWallClockMinutes = builder.Configuration.GetValue("Defaults:MaxWallClockMinutes", 25),
    MaxFixAttempts = builder.Configuration.GetValue("Defaults:MaxFixAttempts", 2),
    MaxTestMinutes = builder.Configuration.GetValue("Defaults:MaxTestMinutes", 15)
});
builder.Services.AddScoped<OrchestratorService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.MapPost("/api/jobs", async (JobRequest request, OrchestratorService orchestrator, CancellationToken ct) =>
{
    var (spec, response) = await orchestrator.CreateJobAsync(request, ct);
    return Results.Created($"/api/jobs/{response.JobId}", response);
});

app.MapGet("/api/jobs/{id}", async (string id, OrchestratorService orchestrator, CancellationToken ct) =>
{
    var status = await orchestrator.GetJobStatusAsync(id, ct);
    return status is null ? Results.NotFound() : Results.Ok(status);
});

app.MapGet("/api/jobs", async (OrchestratorService orchestrator, int? limit, CancellationToken ct) =>
{
    var jobs = await orchestrator.ListJobsAsync(limit ?? 20, ct);
    return Results.Ok(jobs);
});

app.MapGet("/api/jobs/{id}/logs", async (string id, IArtifactStore store, CancellationToken ct) =>
{
    var logs = await store.LoadArtifactAsync(id, ArtifactType.Log, ct);
    return logs is null ? Results.NotFound() : Results.Text(logs, "text/plain");
});

app.Run();