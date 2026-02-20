using Microsoft.EntityFrameworkCore;
using Codions.Contracts.Interfaces;
using Codions.Infrastructure.Data;
using Codions.Infrastructure.Docker;
using Codions.Infrastructure.Security;
using Codions.Infrastructure.Storage;
using Codions.Worker;

var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var workspacesPath = builder.Configuration.GetValue<string>("Docker:WorkspacesPath") ?? "data/workspaces";
// Use the same shared absolute path as the API so Worker finds job-spec.json and context-pack.json
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

var dockerSettings = new DockerSettings
{
    BotImage = builder.Configuration.GetValue<string>("Docker:BotImage") ?? "codions-bot:latest",
    BuildContextPath = builder.Configuration.GetValue<string>("Docker:BuildContextPath"),
    WorkspacesPath = workspacesPath,
    HostWorkspacesPath = builder.Configuration.GetValue<string>("Docker:HostWorkspacesPath"),
    NetworkMode = builder.Configuration.GetValue<string>("Docker:NetworkMode") ?? "bridge",
    MemoryLimitMb = builder.Configuration.GetValue<long>("Docker:MemoryLimitMb", 2048),
    CpuLimit = builder.Configuration.GetValue<double>("Docker:CpuLimit", 2.0)
};
builder.Services.AddSingleton(dockerSettings);
builder.Services.AddSingleton<IContainerRunner, DockerContainerRunner>();
builder.Services.AddSingleton<AuditLogger>();

builder.Services.AddHostedService<JobProcessorService>();

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
}

// Ensure bot Docker image exists (build from docker/bot/Dockerfile if missing)
using (var scope = host.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await DockerImageBuilder.EnsureBotImageExistsAsync(scope.ServiceProvider.GetRequiredService<DockerSettings>(),
        logger);
}

host.Run();