using Codions.Contracts.Enums;
using Codions.Contracts.Models;

namespace Codions.ChatAdapter;

/// <summary>
/// Parses structured text from Chat into JobRequest.
/// Format (line-based):
///   repo: owner/repo-name
///   title: Fix login bug
///   description: User cannot log in when ...
/// </summary>
public static class JobRequestParser
{
    private const string RepoPrefix = "repo:";
    private const string TitlePrefix = "title:";
    private const string DescriptionPrefix = "description:";

    public static (bool Ok, JobRequest? Request, string? Error) TryParse(string text, RequesterInfo requester)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (false, null, "Message is empty. Use: repo: owner/repo  title: ...  description: ...");

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string? repo = null;
        string? title = null;
        var descriptionParts = new List<string>();

        foreach (var line in lines)
        {
            if (line.StartsWith(RepoPrefix, StringComparison.OrdinalIgnoreCase))
                repo = line[RepoPrefix.Length..].Trim();
            else if (line.StartsWith(TitlePrefix, StringComparison.OrdinalIgnoreCase))
                title = line[TitlePrefix.Length..].Trim();
            else if (line.StartsWith(DescriptionPrefix, StringComparison.OrdinalIgnoreCase))
                descriptionParts.Add(line[DescriptionPrefix.Length..].Trim());
            else if (repo is not null && title is not null && descriptionParts.Count > 0)
                descriptionParts.Add(line);
            else if (title is not null && descriptionParts.Count > 0)
                descriptionParts.Add(line);
        }

        if (string.IsNullOrWhiteSpace(repo))
            return (false, null, "Missing `repo: owner/repo-name` (e.g. repo: acme/web-api).");
        if (string.IsNullOrWhiteSpace(title))
            return (false, null, "Missing `title: ...`.");

        var ownerSlashName = repo.Trim().Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (ownerSlashName.Length != 2)
            return (false, null, "Repo must be in the form owner/repo-name (e.g. acme/web-api).");

        var owner = ownerSlashName[0].Trim();
        var name = ownerSlashName[1].Trim();
        if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(name))
            return (false, null, "Repo owner and name cannot be empty.");

        var description = descriptionParts.Count > 0 ? string.Join("\n", descriptionParts) : "";

        var cloneUrl = $"https://github.com/{owner}/{name}.git";
        var request = new JobRequest
        {
            Source = "GoogleChat",
            Requester = requester,
            Repo = new RepoInfo
            {
                Provider = RepoProvider.GitHub,
                Owner = owner,
                Name = name,
                CloneUrl = cloneUrl,
                DefaultBranch = "main"
            },
            Task = new TaskInfo
            {
                Title = title,
                Description = description,
                AcceptanceCriteria = [],
                Links = [],
                ScopeHints = []
            },
            Preferences = null
        };

        return (true, request, null);
    }

    public static string GetUsageHelp()
    {
        return "Send a message in this format:\n\n" +
               "repo: owner/repo-name\n" +
               "title: Your task title\n" +
               "description: Optional longer description (can span multiple lines)\n\n" +
               "Example:\n" +
               "repo: acme/web-api\n" +
               "title: Fix login bug\n" +
               "description: User cannot log in when 2FA is enabled.";
    }
}
