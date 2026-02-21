using System.Diagnostics;
using Codions.BotHarness;
using Codions.BotHarness.Runners;
using Codions.Contracts.Models;

namespace Codions.BotHarness.Steps;

/// <summary>
/// Runs format, build, and test gates for the repo and returns gate results.
/// </summary>
public sealed class LocalGatesRunner(JobSpec spec, ContextPack context, string repoPath, StackProfile detectedStack)
{
    public async Task<List<GateResult>> RunAsync()
    {
        Console.WriteLine("[BotHarness] Step 3: Running local gates...");
        var results = new List<GateResult>();

        var formatCmd = context.RepoInsights.SuggestedCommands.Format ?? detectedStack.FormatCommand;
        if (spec.RunProfile.LocalGates.Format && !string.IsNullOrEmpty(formatCmd))
            results.Add(await RunGateAsync("format", formatCmd));

        var buildCmd = context.RepoInsights.SuggestedCommands.Build ?? detectedStack.BuildCommand;
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
                        ? detectedStack.TestCommand
                        : spec.RunProfile.TestStrategy.FallbackCommand);

                if (!string.IsNullOrEmpty(testCmd))
                {
                    if (detectedStack.Name is "angular" or "node" && !HasSpecOrTestFiles(repoPath))
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
        var pathPrepend = (detectedStack.Name is "node" or "angular")
            ? Path.Combine(repoPath, "node_modules", ".bin")
            : null;

        try
        {
            var (exitCode, output) = await RunWithOutputAsync(fileName, arguments, repoPath,
                TimeSpan.FromMinutes(spec.RunProfile.TestStrategy.MaxTestMinutes), pathPrepend);

            sw.Stop();
            var passed = exitCode == 0;
            Console.WriteLine(
                $"[BotHarness] Gate '{gateName}': {(passed ? "PASSED" : "FAILED")} (exit code {exitCode})");

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

    private static async Task<(int ExitCode, string Output)> RunWithOutputAsync(
        string fileName,
        string arguments,
        string workingDir,
        TimeSpan timeout,
        string? prependToPath = null)
    {
        var (exitCode, stdout, stderr) =
            await ProcessRunner.RunAsync(fileName, arguments, workingDir, timeout, prependToPath);
        return (exitCode, $"{stdout}\n{stderr}".Trim());
    }
}
