using Codions.BotHarness.Runners;

namespace Codions.BotHarness.Helpers;

public static class GitHelper
{
    
    public static async Task<string> GetStagedDiffAsync(string repoPath)
    {
        var (_, stdout, stderr) = await ProcessRunner.RunAsync("git", "diff --cached --unified=0", repoPath,
            TimeSpan.FromMinutes(1));
        return $"{stdout}\n{stderr}".Trim();
    }

    public static async Task<List<string>> GetStagedChangedFilesAsync(string repoPath)
    {
        var (_, stdout, _) = await ProcessRunner.RunAsync("git", "diff --cached --name-only", repoPath,
            TimeSpan.FromMinutes(1));
        return ParsePathList(stdout);
    }

    public static async Task<List<string>> GetLastCommitChangedFilesAsync(string repoPath)
    {
        var (_, stdout, _) = await ProcessRunner.RunAsync("git", "show --pretty=format: --name-only HEAD", repoPath,
            TimeSpan.FromMinutes(1));
        return ParsePathList(stdout);
    }

    public static string GetGitHostForAuth(string cloneUrl)
    {
        if (Uri.TryCreate(cloneUrl, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
            return uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        return "github.com";
    }

    public static List<string> ParsePathList(string output)
    {
        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}