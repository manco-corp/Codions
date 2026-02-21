using Codions.BotHarness.Runners;
using Codions.Contracts.Models;

namespace Codions.BotHarness.Steps;

/// <summary>
/// Clones the repo, creates the branch, and configures git user for commits.
/// </summary>
public sealed class CloneAndBranchStep(string workspacePath, string repoPath, JobSpec spec, string githubToken)
{
    public async Task ExecuteAsync()
    {
        Console.WriteLine("[BotHarness] Step 1: Cloning repo and creating branch...");

        var cloneUrl = BuildAuthenticatedCloneUrl(spec.Repo.CloneUrl);
        await RunOrThrowAsync("git",
            $"clone --depth 1 --branch {spec.Repo.DefaultBranch} {cloneUrl} repo",
            workspacePath,
            "Git clone failed. If the repo is private, set GitHub:Token in the Worker configuration.");

        await RunOrThrowAsync("git", $"checkout -b {spec.Branch.Name}", repoPath, "Git checkout failed.");
        await RunOrThrowAsync("git", "config user.email \"bot@codions.dev\"", repoPath, "Git config failed.");
        await RunOrThrowAsync("git", "config user.name \"Codion Bot\"", repoPath, "Git config failed.");
    }

    private static string BuildAuthenticatedCloneUrl(string cloneUrl, string githubToken)
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

    private string BuildAuthenticatedCloneUrl(string cloneUrl) => BuildAuthenticatedCloneUrl(cloneUrl, githubToken);

    private static async Task RunOrThrowAsync(string fileName, string arguments, string workingDir, string failureContext)
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
}
