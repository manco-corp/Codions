using System.Diagnostics;
using System.Text.Json;
using Codions.BotHarness.Llm;
using Codions.Contracts.Models;

namespace Codions.BotHarness;

#pragma warning disable CS9113 // Primary constructor parameter kept for API compatibility
public class BotHarnessRunner(
    JobSpec spec,
    ContextPack context,
    string workspacePath,
    string githubToken,
    ILlmChatClient? llmChatClient,
    JsonSerializerOptions jsonOptions)
#pragma warning restore CS9113
{
    private string _repoPath = "";
    private StackProfile _detectedStack = new() { Name = "unknown" };

    public async Task<RunSummary> RunAsync()
    {
        var sw = Stopwatch.StartNew();
        List<GateResult> gateResults = [];

        _repoPath = Path.Combine(workspacePath, "repo");
        Console.WriteLine("[BotHarness] Step 1: Cloning repo and creating branch...");
        await GitCloneAndBranch();

        Console.WriteLine("[BotHarness] Step 1b: Detecting tech stack...");
        _detectedStack = await StackDetector.DetectAsync(_repoPath);
        Console.WriteLine($"[BotHarness] Detected stack: {_detectedStack.Name}");

        await InstallDependenciesIfNeeded();

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
        var formatCmd = context.RepoInsights.SuggestedCommands.Format ?? _detectedStack.FormatCommand;
        if (spec.RunProfile.LocalGates.Format && !string.IsNullOrEmpty(formatCmd))
        {
            var formatResult = await RunGate("format", formatCmd);
            gateResults.Add(formatResult);
        }

        var buildCmd = context.RepoInsights.SuggestedCommands.Build ?? _detectedStack.BuildCommand;
        if (spec.RunProfile.LocalGates.Build && !string.IsNullOrEmpty(buildCmd))
        {
            var buildResult = await RunGate("build", buildCmd);
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
                var testCmd = context.RepoInsights.SuggestedCommands.Test
                    ?? (string.IsNullOrEmpty(spec.RunProfile.TestStrategy.FallbackCommand)
                        ? _detectedStack.TestCommand
                        : spec.RunProfile.TestStrategy.FallbackCommand);

                if (!string.IsNullOrEmpty(testCmd))
                {
                    if (_detectedStack.Name is "angular" or "node" && !HasSpecOrTestFiles(_repoPath))
                    {
                        Console.WriteLine("[BotHarness] Skipping gate 'test': no *.spec.ts or *.test.ts files found.");
                        gateResults.Add(new GateResult
                        {
                            GateName = "test",
                            Command = testCmd,
                            Passed = true,
                            ExitCode = 0,
                            Output = "Skipped (no *.spec.ts or *.test.ts files found)",
                            DurationSeconds = 0
                        });
                    }
                    else
                    {
                        var testResult = await RunGate("test", testCmd);
                        gateResults.Add(testResult);
                    }
                }
            }
        }

        var allGatesPassed = gateResults.All(g => g.Passed);
        if (!allGatesPassed)
            Console.WriteLine("[BotHarness] Gates failed. Creating PR anyway so developer can fix.");

        Console.WriteLine("[BotHarness] Step 4: Committing and pushing...");
        await GitCommitAndPush();

        Console.WriteLine("[BotHarness] Step 5: Creating PR...");
        var prBody = BuildPrBody(gateResults);
        var prUrl = await CreatePullRequest(prBody);

        sw.Stop();
        return new RunSummary
        {
            JobId = spec.JobId,
            Success = allGatesPassed,
            PrUrl = prUrl,
            ErrorMessage = allGatesPassed ? null : "Local gates failed; PR created for developer to fix.",
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
        if (llmChatClient is null)
        {
            Console.WriteLine("[BotHarness] No Ollama client. Running in stub mode (trivial edit).");
            return await MakeStubEdit();
        }

        var agentLoop = new AgentLoop(spec, context, _repoPath, llmChatClient, _detectedStack);
        return await agentLoop.ExecuteAsync();
    }

    private async Task InstallDependenciesIfNeeded()
    {
        if (_detectedStack.Name is "node" or "angular")
        {
            var lockFilePath = Path.Combine(_repoPath, "package-lock.json");
            if (File.Exists(lockFilePath))
            {
                Console.WriteLine("[BotHarness] Installing Node.js dependencies (npm ci)...");
                await RunProcess("npm", "ci", _repoPath);
            }
            else
            {
                Console.WriteLine("[BotHarness] Installing Node.js dependencies (npm install)...");
                await RunProcess("npm", "install", _repoPath);
            }
        }
        else if (_detectedStack.Name == "python")
        {
            var reqPath = Path.Combine(_repoPath, "requirements.txt");
            if (File.Exists(reqPath))
            {
                Console.WriteLine("[BotHarness] Installing Python dependencies...");
                await RunProcess("pip", "install -r requirements.txt", _repoPath);
            }
        }
        else if (_detectedStack.Name == "dotnet")
        {
            Console.WriteLine("[BotHarness] Restoring .NET dependencies...");
            await RunProcess("dotnet", "restore", _repoPath);
        }
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

    private static bool HasSpecOrTestFiles(string repoPath)
    {
        try
        {
            var specFiles = Directory.GetFiles(repoPath, "*.spec.ts", SearchOption.AllDirectories);
            if (specFiles.Length > 0) return true;
            var testFiles = Directory.GetFiles(repoPath, "*.test.ts", SearchOption.AllDirectories);
            return testFiles.Length > 0;
        }
        catch
        {
            return true; // if we can't enumerate, run the test gate as usual
        }
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
            var pathPrepend = (_detectedStack.Name is "node" or "angular")
                ? Path.Combine(_repoPath, "node_modules", ".bin")
                : null;
            var (exitCode, output) = await RunProcessWithOutput(fileName, arguments, _repoPath,
                TimeSpan.FromMinutes(spec.RunProfile.TestStrategy.MaxTestMinutes), pathPrepend);

            sw.Stop();
            var passed = exitCode == 0;
            Console.WriteLine($"[BotHarness] Gate '{gateName}': {(passed ? "PASSED" : "FAILED")} (exit code {exitCode})");
            if (!passed && !string.IsNullOrWhiteSpace(output))
            {
                Console.WriteLine($"[BotHarness] Gate '{gateName}' output:");
                Console.WriteLine("---");
                Console.WriteLine(TruncateOutput(output, 8000));
                Console.WriteLine("---");
                if (gateName == "build" && output.Contains("budget", StringComparison.OrdinalIgnoreCase))
                    Console.WriteLine("[BotHarness] Hint: Build failed due to bundle/style budgets. Relax or remove 'budgets' in angular.json, or instruct the agent to make smaller changes.");
            }

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
        var anyFailed = gateResults.Any(g => !g.Passed);
        var lines = new List<string>
        {
            "## Summary",
            "",
            $"**Task:** {spec.Task.Title}",
            "",
            spec.Task.Description,
            ""
        };
        if (anyFailed)
        {
            lines.Add("⚠️ **Some gates failed.** Please fix and re-run checks before merging.");
            lines.Add("");
        }
        lines.Add("## Verification");
        lines.Add("");

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
        string fileName, string arguments, string workingDir, TimeSpan timeout,
        string? prependToPath = null)
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

        if (!string.IsNullOrEmpty(prependToPath) && Directory.Exists(prependToPath))
        {
            var pathKey = Environment.OSVersion.Platform == PlatformID.Win32NT ? "Path" : "PATH";
            var currentPath = Environment.GetEnvironmentVariable(pathKey) ?? "";
            foreach (var kvp in Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>())
            {
                if (kvp.Key is string k && kvp.Value is string v)
                    psi.Environment[k] = v;
            }
            psi.Environment[pathKey] = prependToPath + Path.PathSeparator + currentPath;
        }

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
