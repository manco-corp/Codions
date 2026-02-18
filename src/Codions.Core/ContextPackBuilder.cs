using Codions.Contracts.Models;

namespace Codions.Core;

public class ContextPackBuilder
{
    /// <summary>
    /// Builds a deterministic ContextPack from the task info and scope hints.
    /// In a full implementation this would clone the repo and run rg searches.
    /// For MVP, it produces a skeleton context pack from the available metadata.
    /// </summary>
    public static ContextPack Build(string jobId, TaskInfo task)
    {
        var searchQueries = ExtractSearchQueries(task);

        return new ContextPack
        {
            JobId = jobId,
            RepoInsights = new RepoInsights
            {
                Solutions = [],
                Projects = [],
                SuggestedCommands = new SuggestedCommands
                {
                    Format = "dotnet format",
                    Build = "dotnet build -c Release",
                    Test = "dotnet test"
                }
            },
            SearchResults = searchQueries.Select(q => new SearchResultEntry
            {
                Query = q,
                Matches = []
            }).ToList(),
            RelevantFilesShortlist = task.ScopeHints.ToList(),
            LinkedTexts = task.Links
                .Select(link => new LinkedText { Kind = "link", Content = link })
                .ToList(),
            Rules =
            [
                "Keep diff minimal and scoped to the task.",
                "Follow existing patterns and naming.",
                "Do not modify disallowed paths.",
                "Run all local gates before finalizing."
            ]
        };
    }

    private static List<string> ExtractSearchQueries(TaskInfo task)
    {
        List<string> queries = [];

        if (!string.IsNullOrWhiteSpace(task.Title))
            queries.Add(task.Title);

        var description = task.Description;
        var exceptionNames = new[] { "Exception", "Error", "NullReference", "ArgumentNull", "InvalidOperation" };
        foreach (var ex in exceptionNames)
        {
            if (description.Contains(ex, StringComparison.OrdinalIgnoreCase))
                queries.Add(ex);
        }

        return queries.Distinct().ToList();
    }
}
