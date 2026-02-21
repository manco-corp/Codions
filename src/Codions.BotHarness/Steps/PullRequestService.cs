using Codions.Contracts.Models;

namespace Codions.BotHarness.Steps;

/// <summary>
/// Creates GitHub pull requests and builds PR body from gate results.
/// </summary>
public sealed class PullRequestService
{
    /// <summary>
    /// Creates a pull request. Returns "no-pr-token-missing" if no token is configured.
    /// </summary>
    public async Task<string> CreateAsync(JobSpec spec, string body, string githubToken)
    {
        Console.WriteLine("[BotHarness] Step 5: Creating PR...");

        if (string.IsNullOrEmpty(githubToken))
        {
            Console.WriteLine("[BotHarness] No GitHub token. Skipping PR creation.");
            return "no-pr-token-missing";
        }

        var client = new Octokit.GitHubClient(new Octokit.ProductHeaderValue("CodionBot"))
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

    /// <summary>
    /// Builds the PR description body from the task and gate results.
    /// </summary>
    public static string BuildBody(JobSpec spec, List<GateResult> gateResults)
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
        lines.Add("- Automated change by Codion bot.");
        lines.Add($"- Model used: {spec.RunProfile.ModelName}");
        lines.Add($"- Job ID: {spec.JobId}");
        return string.Join("\n", lines);
    }
}
