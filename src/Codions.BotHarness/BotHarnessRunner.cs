using System.Diagnostics;
using System.Text.Json;
using Codions.Contracts.Models;

namespace Codions.BotHarness;

#pragma warning disable CS9113 // Primary constructor parameter kept for API compatibility
public class BotHarnessRunner(
    JobSpec spec,
    ContextPack context,
    string workspacePath,
    string githubToken,
    string ollamaBaseUrl,
    JsonSerializerOptions jsonOptions)
#pragma warning restore CS9113
{
    private string _repoPath = "";

    public async Task<RunSummary> RunAsync()
    {
        var sw = Stopwatch.StartNew();
        List<GateResult> gateResults = [];

        _repoPath = Path.Combine(workspacePath, "repo");
        Console.WriteLine("[BotHarness] Step 1: Cloning repo and creating branch...");
        await GitCloneAndBranch();

        Console.WriteLine("[BotHarness] Step 2: Agent loop (generating changes)...");
        var filesChanged = await RunAgentLoop();

        if (filesChanged.Count == 0)
        {
            return new RunSummary
            {
                JobId = spec.JobId,
                Success = false,
                ErrorMessage = "Agent produced no changes",
                ElapsedMinutes = sw.Elapsed.TotalMinutes
            };
        }

        Console.WriteLine("[BotHarness] Step 3: Running local gates...");
        if (spec.RunProfile.LocalGates.Format)
        {
            var formatResult = await RunGate("format",
                context.RepoInsights.SuggestedCommands.Format ?? "dotnet format");
            gateResults.Add(formatResult);
        }

        if (spec.RunProfile.LocalGates.Build)
        {
            var buildResult = await RunGate("build",
                context.RepoInsights.SuggestedCommands.Build ?? "dotnet build -c Release");
            gateResults.Add(buildResult);
        }

        if (spec.RunProfile.LocalGates.Tests)
        {
            var testCommands = spec.RunProfile.TestStrategy.TargetedCommands;
            if (testCommands.Count > 0)
            {
                foreach (var cmd in testCommands)
                {
                    var testResult = await RunGate("test-targeted", cmd);
                    gateResults.Add(testResult);
                }
            }
            else
            {
                var testResult = await RunGate("test",
                    context.RepoInsights.SuggestedCommands.Test ?? spec.RunProfile.TestStrategy.FallbackCommand);
                gateResults.Add(testResult);
            }
        }

        var allGatesPassed = gateResults.All(g => g.Passed);

        if (!allGatesPassed)
        {
            Console.WriteLine("[BotHarness] Gates failed. Not creating PR.");
            return new RunSummary
            {
                JobId = spec.JobId,
                Success = false,
                ErrorMessage = "Local gates failed",
                GateResults = gateResults,
                FilesChanged = filesChanged,
                ElapsedMinutes = sw.Elapsed.TotalMinutes
            };
        }

        Console.WriteLine("[BotHarness] Step 4: Committing and pushing...");
        await GitCommitAndPush();

        Console.WriteLine("[BotHarness] Step 5: Creating PR...");
        var prBody = BuildPrBody(gateResults);
        var prUrl = await CreatePullRequest(prBody);

        sw.Stop();
        return new RunSummary
        {
            JobId = spec.JobId,
            Success = true,
            PrUrl = prUrl,
            GateResults = gateResults,
            FilesChanged = filesChanged,
            ElapsedMinutes = sw.Elapsed.TotalMinutes,
            AttemptNumber = 1,
            ModelUsed = spec.RunProfile.ModelName
        };
    }

    private async Task GitCloneAndBranch()
    {
        var cloneUrl = spec.Repo.CloneUrl;

        if (!string.IsNullOrEmpty(githubToken) && cloneUrl.StartsWith("https://"))
        {
            cloneUrl = cloneUrl.Replace("https://", $"https://x-access-token:{githubToken}@");
        }
        else if (cloneUrl.StartsWith("https://") && cloneUrl.Contains("github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "GitHub HTTPS clone requires GITHUB_TOKEN. Set GitHub:Token in the Worker appsettings (or User Secrets) and ensure it is passed into the container.");
        }

        await RunProcessOrThrow("git", $"clone --depth 1 --branch {spec.Repo.DefaultBranch} {cloneUrl} repo", workspacePath, "Git clone failed. If the repo is private, set GitHub:Token in the Worker configuration.");
        await RunProcessOrThrow("git", $"checkout -b {spec.Branch.Name}", _repoPath, "Git checkout failed.");
        await RunProcessOrThrow("git", "config user.email \"bot@minions.dev\"", _repoPath, "Git config failed.");
        await RunProcessOrThrow("git", "config user.name \"Minions Bot\"", _repoPath, "Git config failed.");
    }

    private async Task<List<string>> RunAgentLoop()
    {
        if (string.IsNullOrEmpty(ollamaBaseUrl))
        {
            Console.WriteLine("[BotHarness] No Ollama base URL. Running in stub mode (trivial edit).");
            return await MakeStubEdit();
        }

        var agentLoop = new AgentLoop(spec, context, _repoPath, ollamaBaseUrl);
        return await agentLoop.ExecuteAsync();
    }

    private async Task<List<string>> MakeStubEdit()
    {
        var readmePath = Path.Combine(_repoPath, "AGENT_CHANGE.md");
        var content = $"""
            # Agent Change - {spec.Task.Title}

            **Job ID:** {spec.JobId}
            **Branch:** {spec.Branch.Name}
            **Created:** {spec.CreatedUtc:u}

            ## Task
            {spec.Task.Description}

            ---
            *This file was created by the Minions bot as a stub edit.*
            """;

        await File.WriteAllTextAsync(readmePath, content);
        return ["AGENT_CHANGE.md"];
    }

    private async Task<GateResult> RunGate(string gateName, string command)
    {
        Console.WriteLine($"[BotHarness] Running gate '{gateName}': {command}");
        var sw = Stopwatch.StartNew();

        var parts = command.Split(' ', 2);
        var fileName = parts[0];
        var arguments = parts.Length > 1 ? parts[1] : "";

        try
        {
            var (exitCode, output) = await RunProcessWithOutput(fileName, arguments, _repoPath,
                TimeSpan.FromMinutes(spec.RunProfile.TestStrategy.MaxTestMinutes));

            sw.Stop();
            var passed = exitCode == 0;
            Console.WriteLine($"[BotHarness] Gate '{gateName}': {(passed ? "PASSED" : "FAILED")} (exit code {exitCode})");

            return new GateResult
            {
                GateName = gateName,
                Command = command,
                Passed = passed,
                ExitCode = exitCode,
                Output = TruncateOutput(output, 4000),
                DurationSeconds = sw.Elapsed.TotalSeconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            Console.WriteLine($"[BotHarness] Gate '{gateName}': ERROR - {ex.Message}");
            return new GateResult
            {
                GateName = gateName,
                Command = command,
                Passed = false,
                ExitCode = -1,
                Output = ex.Message,
                DurationSeconds = sw.Elapsed.TotalSeconds
            };
        }
    }

    private async Task GitCommitAndPush()
    {
        await RunProcessOrThrow("git", "add -A", _repoPath, "Git add failed.");
        await RunProcessOrThrow("git", $"commit -m \"{spec.Branch.CommitMessage}\"", _repoPath, "Git commit failed.");
        await RunProcessOrThrow("git", $"push origin {spec.Branch.Name}", _repoPath, "Git push failed. Ensure GitHub:Token has push access.");
    }

    private async Task<string> CreatePullRequest(string body)
    {
        if (string.IsNullOrEmpty(githubToken))
        {
            Console.WriteLine("[BotHarness] No GitHub token. Skipping PR creation.");
            return "no-pr-token-missing";
        }

        var client = new Octokit.GitHubClient(new Octokit.ProductHeaderValue("MinionsBot"))
        {
            Credentials = new Octokit.Credentials(githubToken)
        };

        var pr = await client.PullRequest.Create(
            spec.Repo.Owner,
            spec.Repo.Name,
            new Octokit.NewPullRequest(spec.Branch.PrTitle, spec.Branch.Name, spec.Repo.DefaultBranch)
            {
                Body = body
            });

        Console.WriteLine($"[BotHarness] PR created: {pr.HtmlUrl}");
        return pr.HtmlUrl;
    }

    private string BuildPrBody(List<GateResult> gateResults)
    {
        List<string> lines =
        [
            "## Summary",
            "",
            $"**Task:** {spec.Task.Title}",
            "",
            spec.Task.Description,
            "",
            "## Verification",
            ""
        ];

        foreach (var gate in gateResults)
        {
            var icon = gate.Passed ? "✅" : "❌";
            lines.Add($"- `{gate.Command}`: {icon} (exit code {gate.ExitCode}, {gate.DurationSeconds:F1}s)");
        }

        lines.Add("");
        lines.Add("## Notes/Risk");
        lines.Add("");
        lines.Add("- Automated change by Minions bot.");
        lines.Add($"- Model used: {spec.RunProfile.ModelName}");
        lines.Add($"- Job ID: {spec.JobId}");

        return string.Join("\n", lines);
    }

    private static async Task RunProcessOrThrow(string fileName, string arguments, string workingDir, string failureContext)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {fileName} {arguments}");

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            Console.WriteLine($"[BotHarness] Process '{fileName} {arguments}' stdout: {stdout}");
            Console.WriteLine($"[BotHarness] Process '{fileName} {arguments}' stderr: {stderr}");
            throw new InvalidOperationException(
                $"{failureContext} Exit code: {process.ExitCode}. Stderr: {stderr.Trim()}");
        }
    }

    private static async Task RunProcess(string fileName, string arguments, string workingDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {fileName} {arguments}");

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            Console.WriteLine($"[BotHarness] Process '{fileName} {arguments}' stdout: {stdout}");
            Console.WriteLine($"[BotHarness] Process '{fileName} {arguments}' stderr: {stderr}");
        }
    }

    private static async Task<(int exitCode, string output)> RunProcessWithOutput(
        string fileName, string arguments, string workingDir, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {fileName} {arguments}");

        using var cts = new CancellationTokenSource(timeout);

        var stdout = await process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderr = await process.StandardError.ReadToEndAsync(cts.Token);

        await process.WaitForExitAsync(cts.Token);
        return (process.ExitCode, $"{stdout}\n{stderr}".Trim());
    }

    private static string TruncateOutput(string output, int maxChars)
    {
        if (output.Length <= maxChars) return output;
        return output[..maxChars] + "\n... (truncated)";
    }
}
