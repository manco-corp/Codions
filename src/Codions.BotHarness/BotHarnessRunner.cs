using System.Diagnostics;
using System.Text;
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
    private readonly SecretScanner _secretScanner = new(githubToken);

    #region Orchestration

    public async Task<RunSummary> RunAsync()
    {
        var sw = Stopwatch.StartNew();
        _repoPath = Path.Combine(workspacePath, "repo");

        await CloneAndBranchAsync();
        await DetectStackAndInstallDepsAsync();
        var filesChanged = await RunAgentLoopAsync();

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

        var gateResults = await RunLocalGatesAsync();
        var allGatesPassed = gateResults.All(g => g.Passed);
        if (!allGatesPassed)
            Console.WriteLine("[BotHarness] Gates failed. Creating PR anyway so developer can fix.");

        await CommitAndPushAsync();
        var prUrl = await CreatePullRequestAsync(BuildPrBody(gateResults));

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

    #endregion

    #region Git — clone, branch, config

    private async Task CloneAndBranchAsync()
    {
        Console.WriteLine("[BotHarness] Step 1: Cloning repo and creating branch...");

        var cloneUrl = BuildAuthenticatedCloneUrl(spec.Repo.CloneUrl);
        await RunOrThrowAsync("git",
            $"clone --depth 1 --branch {spec.Repo.DefaultBranch} {cloneUrl} repo",
            workspacePath,
            "Git clone failed. If the repo is private, set GitHub:Token in the Worker configuration.");

        await RunOrThrowAsync("git", $"checkout -b {spec.Branch.Name}", _repoPath, "Git checkout failed.");
        await RunOrThrowAsync("git", "config user.email \"bot@minions.dev\"", _repoPath, "Git config failed.");
        await RunOrThrowAsync("git", "config user.name \"Minions Bot\"", _repoPath, "Git config failed.");
    }

    private string BuildAuthenticatedCloneUrl(string cloneUrl)
    {
        if (!string.IsNullOrEmpty(githubToken) && cloneUrl.StartsWith("https://", StringComparison.Ordinal))
            return cloneUrl.Replace("https://", $"https://x-access-token:{githubToken}@");

        if (cloneUrl.StartsWith("https://", StringComparison.Ordinal) &&
            cloneUrl.Contains("github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "GitHub HTTPS clone requires GITHUB_TOKEN. Set GitHub:Token in the Worker appsettings (or User Secrets) and ensure it is passed into the container.");
        }

        return cloneUrl;
    }

    #endregion

    #region Git — commit, push, secrets

    private async Task CommitAndPushAsync()
    {
        Console.WriteLine("[BotHarness] Step 4: Committing and pushing...");

        await RunOrThrowAsync("git", "add -A", _repoPath, "Git add failed.");
        await SanitizeStagedSecretsAsync();
        await RunOrThrowAsync("git", $"commit -m \"{spec.Branch.CommitMessage}\"", _repoPath, "Git commit failed.");
        await PushWithAuthAsync();
    }

    private async Task SanitizeStagedSecretsAsync()
    {
        var stagedDiff = await GetStagedDiffAsync();
        var addedLines = DiffHelper.ExtractAddedLines(stagedDiff);
        var detections = _secretScanner.DetectSecretsInText(addedLines);
        if (detections.Count == 0)
            return;

        Console.WriteLine(
            $"[BotHarness] Detected {detections.Count} potential secret(s) in staged diff. Attempting auto-sanitization...");

        var stagedFiles = await GetStagedChangedFilesAsync();
        var findings = await _secretScanner.RedactFilesAsync(_repoPath, stagedFiles);
        if (findings.Count == 0)
        {
            throw new InvalidOperationException(
                "Potential secrets detected in staged changes, but no automatic sanitization could be applied. Please remove credentials from generated files.");
        }

        await RunOrThrowAsync("git", "add -A", _repoPath, "Git add failed after secret sanitization.");
        var remaining = _secretScanner.DetectSecretsInText(DiffHelper.ExtractAddedLines(await GetStagedDiffAsync()));
        if (remaining.Count > 0)
        {
            var names = string.Join(", ", remaining.Distinct(StringComparer.OrdinalIgnoreCase).Take(5));
            throw new InvalidOperationException(
                $"Potential secrets remain after sanitization ({names}). Please remove sensitive values before push.");
        }

        var fileCount = findings.Select(f => f.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        Console.WriteLine($"[BotHarness] Auto-sanitized {fileCount} file(s) containing potential secrets.");
    }

    private async Task PushWithAuthAsync()
    {
        var (exitCode, _, stderr) = await RunAuthenticatedPushAsync();
        if (exitCode == 0)
            return;

        var message = PushFailureClassifier.GetMessage(stderr);
        if (!PushFailureClassifier.IsPushProtection(stderr))
            throw new InvalidOperationException(message);

        Console.WriteLine("[BotHarness] GitHub push protection detected. Attempting one sanitization retry...");
        var lastCommitFiles = await GetLastCommitChangedFilesAsync();
        var findings = await _secretScanner.RedactFilesAsync(_repoPath, lastCommitFiles);
        if (findings.Count == 0)
            throw new InvalidOperationException(message);

        await RunOrThrowAsync("git", "add -A", _repoPath, "Git add failed after push-protection sanitization.");
        await RunOrThrowAsync("git", "commit -m \"chore: sanitize potential secrets before push\"", _repoPath,
            "Git commit failed after push-protection sanitization.");

        var (retryExitCode, _, retryStderr) = await RunAuthenticatedPushAsync();
        if (retryExitCode == 0)
            return;

        throw new InvalidOperationException(PushFailureClassifier.GetMessage(retryStderr));
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunAuthenticatedPushAsync()
    {
        if (string.IsNullOrWhiteSpace(githubToken))
        {
            throw new InvalidOperationException(
                "Git push failed: GITHUB_TOKEN is missing. Set GitHub:Token in Worker configuration.");
        }

        var host = GetGitHostForAuth(spec.Repo.CloneUrl);
        var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"x-access-token:{githubToken}"));
        var args = $"-c http.https://{host}/.extraheader=\"AUTHORIZATION: basic {basicAuth}\" push origin {spec.Branch.Name}";
        return await ProcessRunner.RunAsync("git", args, _repoPath, TimeSpan.FromMinutes(5));
    }

    private async Task<string> GetStagedDiffAsync()
    {
        var (_, stdout, stderr) = await ProcessRunner.RunAsync("git", "diff --cached --unified=0", _repoPath,
            TimeSpan.FromMinutes(1));
        return $"{stdout}\n{stderr}".Trim();
    }

    private async Task<List<string>> GetStagedChangedFilesAsync()
    {
        var (_, stdout, _) = await ProcessRunner.RunAsync("git", "diff --cached --name-only", _repoPath,
            TimeSpan.FromMinutes(1));
        return ParsePathList(stdout);
    }

    private async Task<List<string>> GetLastCommitChangedFilesAsync()
    {
        var (_, stdout, _) = await ProcessRunner.RunAsync("git", "show --pretty=format: --name-only HEAD", _repoPath,
            TimeSpan.FromMinutes(1));
        return ParsePathList(stdout);
    }

    private static string GetGitHostForAuth(string cloneUrl)
    {
        if (Uri.TryCreate(cloneUrl, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
            return uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        return "github.com";
    }

    private static List<string> ParsePathList(string output)
    {
        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    #endregion

    #region Stack detection and dependencies

    private async Task DetectStackAndInstallDepsAsync()
    {
        Console.WriteLine("[BotHarness] Step 1b: Detecting tech stack...");
        _detectedStack = await StackDetector.DetectAsync(_repoPath);
        Console.WriteLine($"[BotHarness] Detected stack: {_detectedStack.Name}");
        await InstallDependenciesAsync();
    }

    private async Task InstallDependenciesAsync()
    {
        if (_detectedStack.Name is "node" or "angular")
        {
            if (File.Exists(Path.Combine(_repoPath, "package-lock.json")))
            {
                Console.WriteLine("[BotHarness] Installing Node.js dependencies (npm ci)...");
                await RunAsync("npm", "ci", _repoPath);
            }
            else
            {
                Console.WriteLine("[BotHarness] Installing Node.js dependencies (npm install)...");
                await RunAsync("npm", "install", _repoPath);
            }
        }
        else if (_detectedStack.Name == "python")
        {
            if (File.Exists(Path.Combine(_repoPath, "requirements.txt")))
            {
                Console.WriteLine("[BotHarness] Installing Python dependencies...");
                await RunAsync("pip", "install -r requirements.txt", _repoPath);
            }
        }
        else if (_detectedStack.Name == "dotnet")
        {
            Console.WriteLine("[BotHarness] Restoring .NET dependencies...");
            await RunAsync("dotnet", "restore", _repoPath);
        }
    }

    #endregion

    #region Agent loop

    private async Task<List<string>> RunAgentLoopAsync()
    {
        Console.WriteLine("[BotHarness] Step 2: Agent loop (generating changes)...");

        if (llmChatClient is null)
        {
            Console.WriteLine("[BotHarness] No LLM client. Running in stub mode (trivial edit).");
            return await MakeStubEditAsync();
        }

        var agentLoop = new AgentLoop(spec, context, _repoPath, llmChatClient, _detectedStack);
        return await agentLoop.ExecuteAsync();
    }

    private async Task<List<string>> MakeStubEditAsync()
    {
        var path = Path.Combine(_repoPath, "AGENT_CHANGE.md");
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
        await File.WriteAllTextAsync(path, content);
        return ["AGENT_CHANGE.md"];
    }

    #endregion

    #region Local gates

    private async Task<List<GateResult>> RunLocalGatesAsync()
    {
        Console.WriteLine("[BotHarness] Step 3: Running local gates...");
        var results = new List<GateResult>();

        var formatCmd = context.RepoInsights.SuggestedCommands.Format ?? _detectedStack.FormatCommand;
        if (spec.RunProfile.LocalGates.Format && !string.IsNullOrEmpty(formatCmd))
            results.Add(await RunGateAsync("format", formatCmd));

        var buildCmd = context.RepoInsights.SuggestedCommands.Build ?? _detectedStack.BuildCommand;
        if (spec.RunProfile.LocalGates.Build && !string.IsNullOrEmpty(buildCmd))
            results.Add(await RunGateAsync("build", buildCmd));

        if (spec.RunProfile.LocalGates.Tests)
        {
            var targeted = spec.RunProfile.TestStrategy.TargetedCommands;
            if (targeted.Count > 0)
            {
                foreach (var cmd in targeted)
                    results.Add(await RunGateAsync("test-targeted", cmd));
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
                        results.Add(new GateResult
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
                        results.Add(await RunGateAsync("test", testCmd));
                    }
                }
            }
        }

        return results;
    }

    private async Task<GateResult> RunGateAsync(string gateName, string command)
    {
        Console.WriteLine($"[BotHarness] Running gate '{gateName}': {command}");
        var sw = Stopwatch.StartNew();

        var (fileName, arguments) = SplitCommand(command);
        var pathPrepend = (_detectedStack.Name is "node" or "angular")
            ? Path.Combine(_repoPath, "node_modules", ".bin")
            : null;

        try
        {
            var (exitCode, output) = await RunWithOutputAsync(fileName, arguments, _repoPath,
                TimeSpan.FromMinutes(spec.RunProfile.TestStrategy.MaxTestMinutes), pathPrepend);

            sw.Stop();
            var passed = exitCode == 0;
            Console.WriteLine($"[BotHarness] Gate '{gateName}': {(passed ? "PASSED" : "FAILED")} (exit code {exitCode})");

            if (!passed && !string.IsNullOrWhiteSpace(output))
            {
                Console.WriteLine($"[BotHarness] Gate '{gateName}' output:");
                Console.WriteLine("---");
                Console.WriteLine(ProcessRunner.Truncate(output, 8000));
                Console.WriteLine("---");
                if (gateName == "build" && output.Contains("budget", StringComparison.OrdinalIgnoreCase))
                    Console.WriteLine(
                        "[BotHarness] Hint: Build failed due to bundle/style budgets. Relax or remove 'budgets' in angular.json, or instruct the agent to make smaller changes.");
            }

            return new GateResult
            {
                GateName = gateName,
                Command = command,
                Passed = passed,
                ExitCode = exitCode,
                Output = ProcessRunner.Truncate(output, 4000),
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

    private static (string fileName, string arguments) SplitCommand(string command)
    {
        var parts = command.Split(' ', 2);
        return (parts[0], parts.Length > 1 ? parts[1] : "");
    }

    private static bool HasSpecOrTestFiles(string repoPath)
    {
        try
        {
            if (Directory.GetFiles(repoPath, "*.spec.ts", SearchOption.AllDirectories).Length > 0)
                return true;
            return Directory.GetFiles(repoPath, "*.test.ts", SearchOption.AllDirectories).Length > 0;
        }
        catch
        {
            return true;
        }
    }

    #endregion

    #region Pull request

    private async Task<string> CreatePullRequestAsync(string body)
    {
        Console.WriteLine("[BotHarness] Step 5: Creating PR...");

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
            new Octokit.NewPullRequest(spec.Branch.PrTitle, spec.Branch.Name, spec.Repo.DefaultBranch) { Body = body });

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

    #endregion

    #region Process execution

    private async Task RunOrThrowAsync(string fileName, string arguments, string workingDir, string failureContext)
    {
        var (exitCode, stdout, stderr) = await ProcessRunner.RunAsync(fileName, arguments, workingDir);
        if (exitCode != 0)
        {
            var safeCmd = ProcessRunner.Redact($"{fileName} {arguments}");
            Console.WriteLine($"[BotHarness] Process '{safeCmd}' stdout: {ProcessRunner.Redact(stdout)}");
            Console.WriteLine($"[BotHarness] Process '{safeCmd}' stderr: {ProcessRunner.Redact(stderr)}");
            throw new InvalidOperationException(
                $"{failureContext} Exit code: {exitCode}. Stderr: {ProcessRunner.Redact(stderr.Trim())}");
        }
    }

    private async Task RunAsync(string fileName, string arguments, string workingDir)
    {
        var (exitCode, stdout, stderr) = await ProcessRunner.RunAsync(fileName, arguments, workingDir);
        if (exitCode != 0)
        {
            var safeCmd = ProcessRunner.Redact($"{fileName} {arguments}");
            Console.WriteLine($"[BotHarness] Process '{safeCmd}' stdout: {ProcessRunner.Redact(stdout)}");
            Console.WriteLine($"[BotHarness] Process '{safeCmd}' stderr: {ProcessRunner.Redact(stderr)}");
        }
    }

    private async Task<(int ExitCode, string Output)> RunWithOutputAsync(
        string fileName,
        string arguments,
        string workingDir,
        TimeSpan timeout,
        string? prependToPath = null)
    {
        var (exitCode, stdout, stderr) = await ProcessRunner.RunAsync(fileName, arguments, workingDir, timeout, prependToPath);
        return (exitCode, $"{stdout}\n{stderr}".Trim());
    }

    #endregion
}

#region Helpers

internal static class DiffHelper
{
    public static string ExtractAddedLines(string diffText)
    {
        if (string.IsNullOrEmpty(diffText))
            return "";

        var sb = new StringBuilder();
        foreach (var line in diffText.Split('\n'))
        {
            if (line.StartsWith("+++"))
                continue;
            if (line.StartsWith('+'))
                sb.AppendLine(line[1..]);
        }
        return sb.ToString();
    }
}

internal static class PushFailureClassifier
{
    public static bool IsPushProtection(string stderr)
    {
        return stderr.Contains("GH013", StringComparison.OrdinalIgnoreCase)
               || stderr.Contains("push cannot contain secrets", StringComparison.OrdinalIgnoreCase)
               || stderr.Contains("secret scanning", StringComparison.OrdinalIgnoreCase)
               || stderr.Contains("push protection", StringComparison.OrdinalIgnoreCase);
    }

    public static string GetMessage(string stderr)
    {
        if (stderr.Contains("invalid username or token", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("authentication failed", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("password authentication is not supported", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("could not read Username", StringComparison.OrdinalIgnoreCase))
        {
            return "Git push failed: authentication failed. Ensure GitHub:Token is valid and has repo push permission.";
        }

        if (IsPushProtection(stderr))
            return "Git push failed: GitHub push protection blocked detected secrets. Remove/sanitize credentials before pushing.";

        if (stderr.Contains("permission denied", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("protected branch hook declined", StringComparison.OrdinalIgnoreCase))
        {
            return "Git push failed: permission or branch protection blocked the push.";
        }

        return $"Git push failed. Stderr: {ProcessRunner.Redact(stderr.Trim())}";
    }
}

#endregion
