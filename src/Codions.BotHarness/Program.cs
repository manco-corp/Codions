using System.Text.Json;
using Codions.BotHarness;
using Codions.Contracts.Models;

var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true
};

var workspacePath = Environment.GetEnvironmentVariable("WORKSPACE_PATH") ?? "/workspace";
var jobId = Environment.GetEnvironmentVariable("JOB_ID") ?? "unknown";

Console.WriteLine($"[BotHarness] Starting job {jobId}");
Console.WriteLine($"[BotHarness] Workspace: {workspacePath}");

try
{
    var specPath = Path.Combine(workspacePath, "job-spec.json");
    var contextPath = Path.Combine(workspacePath, "context-pack.json");

    if (!File.Exists(specPath) || !File.Exists(contextPath))
    {
        Console.Error.WriteLine("[BotHarness] Missing job-spec.json or context-pack.json");
        Environment.Exit(1);
    }

    var specJson = await File.ReadAllTextAsync(specPath);
    var contextJson = await File.ReadAllTextAsync(contextPath);

    var spec = JsonSerializer.Deserialize<JobSpec>(specJson, jsonOptions)
        ?? throw new InvalidOperationException("Failed to deserialize job-spec.json");

    var context = JsonSerializer.Deserialize<ContextPack>(contextJson, jsonOptions)
        ?? throw new InvalidOperationException("Failed to deserialize context-pack.json");

    var githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? "";
    var ollamaBaseUrl = Environment.GetEnvironmentVariable("OLLAMA_BASE_URL") ?? "http://host.docker.internal:11434";

    Console.WriteLine($"[BotHarness] GITHUB_TOKEN present in environment: {(string.IsNullOrEmpty(githubToken) ? "No" : "Yes")}");

    var harness = new BotHarnessRunner(spec, context, workspacePath, githubToken, ollamaBaseUrl, jsonOptions);
    var summary = await harness.RunAsync();

    var summaryJson = JsonSerializer.Serialize(summary, jsonOptions);
    await File.WriteAllTextAsync(Path.Combine(workspacePath, "run-summary.json"), summaryJson);

    Console.WriteLine($"[BotHarness] Job {jobId} completed. Success: {summary.Success}");
    if (summary.PrUrl is not null)
        Console.WriteLine($"[BotHarness] PR: {summary.PrUrl}");

    Environment.Exit(summary.Success ? 0 : 1);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[BotHarness] Fatal error: {ex.Message}");
    Console.Error.WriteLine(ex.StackTrace);

    var errorSummary = new RunSummary
    {
        JobId = jobId,
        Success = false,
        ErrorMessage = ex.Message
    };
    var errorJson = JsonSerializer.Serialize(errorSummary, jsonOptions);
    await File.WriteAllTextAsync(Path.Combine(workspacePath, "run-summary.json"), errorJson);

    Environment.Exit(1);
}
