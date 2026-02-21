using System.Text;
using Codions.BotHarness;
using Codions.BotHarness.Helpers;
using Codions.BotHarness.Runners;
using Codions.Contracts.Models;

namespace Codions.BotHarness.CommitPush;

/// <summary>
/// Runs git commands in a given repo directory. Uses ProcessRunner and GitHelper for auth.
/// </summary>
internal sealed class GitCommandRunner(string repoPath, JobSpec spec, string githubToken) : IGitCommandRunner
{
    public async Task RunOrThrowAsync(string arguments, string failureContext, CancellationToken cancellationToken = default)
    {
        var (exitCode, stdout, stderr) = await ProcessRunner.RunAsync("git", arguments, repoPath);
        if (exitCode != 0)
        {
            var safeCmd = ProcessRunner.Redact($"git {arguments}");
            Console.WriteLine($"[BotHarness] Process '{safeCmd}' stdout: {ProcessRunner.Redact(stdout)}");
            Console.WriteLine($"[BotHarness] Process '{safeCmd}' stderr: {ProcessRunner.Redact(stderr)}");
            throw new InvalidOperationException(
                $"{failureContext} Exit code: {exitCode}. Stderr: {ProcessRunner.Redact(stderr.Trim())}");
        }
    }

    public async Task<(int ExitCode, string Stdout, string Stderr)> RunAuthenticatedPushAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(githubToken))
        {
            throw new InvalidOperationException(
                "Git push failed: GITHUB_TOKEN is missing. Set GitHub:Token in Worker configuration.");
        }

        var host = GitHelper.GetGitHostForAuth(spec.Repo.CloneUrl);
        var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"x-access-token:{githubToken}"));
        var args =
            $"-c http.https://{host}/.extraheader=\"AUTHORIZATION: basic {basicAuth}\" push origin {spec.Branch.Name}";
        return await ProcessRunner.RunAsync("git", args, repoPath, TimeSpan.FromMinutes(5));
    }
}
