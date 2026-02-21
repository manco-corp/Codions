using System.Diagnostics;
using Codions.BotHarness.CommitPush;
using Codions.BotHarness.Helpers;
using Codions.BotHarness.Llm;
using Codions.BotHarness.Steps;
using Codions.Contracts.Models;

namespace Codions.BotHarness.Runners;
 
public class BotHarnessRunner(
    JobSpec spec,
    ContextPack context,
    string workspacePath,
    string githubToken,
    ILlmChatClient? llmChatClient)
 
{
    private string _repoPath = "";
    private StackProfile _detectedStack = new() { Name = "unknown" };
    private readonly SecretScanner _secretScanner = new(githubToken);

    #region Orchestration

    public async Task<RunSummary> RunAsync()
    {
        var sw = Stopwatch.StartNew();
        _repoPath = Path.Combine(workspacePath, "repo");

        var cloneStep = new CloneAndBranchStep(workspacePath, _repoPath, spec, githubToken);
        await cloneStep.ExecuteAsync();
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

        var gatesRunner = new LocalGatesRunner(spec, context, _repoPath, _detectedStack);
        var gateResults = await gatesRunner.RunAsync();
        var allGatesPassed = gateResults.All(g => g.Passed);
        if (!allGatesPassed)
            Console.WriteLine("[BotHarness] Gates failed. Creating PR anyway so developer can fix.");

        await CommitAndPushAsync();
        var prService = new PullRequestService();
        var prBody = PullRequestService.BuildBody(spec, gateResults);
        var prUrl = await prService.CreateAsync(spec, prBody, githubToken);

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

    #region Git — commit, push, secrets

    private async Task CommitAndPushAsync()
    {
        var pipeline = new CommitPushPipeline();
        var gitRunner = new GitCommandRunner(_repoPath, spec, githubToken);
        await pipeline.ExecuteAsync(new CommitPushContext(_repoPath, spec, githubToken, _secretScanner, gitRunner));
    }

    #endregion

    #region Stack detection and dependencies

    private async Task DetectStackAndInstallDepsAsync()
    {
        Console.WriteLine("[BotHarness] Step 1b: Detecting tech stack...");
        _detectedStack = await StackDetector.DetectAsync(_repoPath);
        Console.WriteLine($"[BotHarness] Detected stack: {_detectedStack.Name}");
        var installer = new DependencyInstaller(_repoPath, _detectedStack);
        await installer.InstallAsync();
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
                       *This file was created by the Codion bot as a stub edit.*
                       """;
        await File.WriteAllTextAsync(path, content);
        return ["AGENT_CHANGE.md"];
    }

    #endregion

}

#region Helpers

#endregion