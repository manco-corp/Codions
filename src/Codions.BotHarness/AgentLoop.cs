using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Codions.Contracts.Models;
using OllamaClient;
using OllamaClient.Models;

namespace Codions.BotHarness;

/// <summary>
/// The agent loop calls the Ollama API to generate code changes iteratively.
/// It reads relevant files, builds a prompt, parses the model's file-edit instructions,
/// and applies them to the working tree.
/// </summary>
public partial class AgentLoop(JobSpec spec, ContextPack context, string repoPath, IOllamaHttpClient llm)
{
    private readonly string _model = ResolveModelName(spec.RunProfile);
    private readonly List<ConversationMessage> _conversation = [];

    public async Task<List<string>> ExecuteAsync()
    {
        HashSet<string> changedFiles = [];
        var maxSteps = spec.RunProfile.MaxAgentSteps;
        var sw = Stopwatch.StartNew();
        var maxMinutes = spec.RunProfile.MaxWallClockMinutes;

        var systemPrompt = BuildSystemPrompt();
        var userMessage = BuildInitialUserMessage();

        _conversation.Add(new ConversationMessage("user", userMessage));

        for (var step = 0; step < maxSteps; step++)
        {
            if (sw.Elapsed.TotalMinutes > maxMinutes)
            {
                Console.WriteLine($"[AgentLoop] Wall clock limit reached ({maxMinutes}m). Stopping.");
                break;
            }

            Console.WriteLine($"[AgentLoop] Step {step + 1}/{maxSteps}...");

            var response = await SendAsync(systemPrompt, _conversation);
            Console.WriteLine($"[AgentLoop] Response: {response.Length} chars");

            _conversation.Add(new ConversationMessage("assistant", response));

            var edits = ParseFileEdits(response);

            if (edits.Count == 0 && response.Contains("[DONE]", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("[AgentLoop] Model signaled DONE with no edits.");
                break;
            }

            if (edits.Count == 0)
            {
                Console.WriteLine("[AgentLoop] No file edits parsed from response. Asking for clarification...");
                _conversation.Add(new ConversationMessage("user",
                    "I didn't detect any file edits in your response. Please provide concrete file changes using the FILE_EDIT format, or reply with [DONE] if no changes are needed."));
                continue;
            }

            foreach (var edit in edits)
            {
                await ApplyEdit(edit);
                changedFiles.Add(edit.FilePath);
            }

            if (response.Contains("[DONE]", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("[AgentLoop] Model signaled DONE.");
                break;
            }

            _conversation.Add(new ConversationMessage("user",
                $"Applied {edits.Count} file edit(s). If you need to make more changes, continue. Otherwise reply with [DONE]."));
        }

        return changedFiles.ToList();
    }

    private async Task<string> SendAsync(string systemPrompt, List<ConversationMessage> messages)
    {
        var chatMessages = new List<Message>
        {
            new() { Role = "system", Content = systemPrompt }
        };
        chatMessages.AddRange(messages.Select(m => new Message { Role = m.Role, Content = m.Content }));

        var request = new ChatRequest
        {
            Model = _model,
            Messages = chatMessages
        };

        var chatResponse = await llm.SendChat(request, CancellationToken.None);

        var promptTokens = chatResponse.PromptEvalCount ?? 0;
        var completionTokens = chatResponse.EvalCount ?? 0;
        Console.WriteLine($"[AgentLoop] Tokens: {promptTokens} in / {completionTokens} out");

        return chatResponse.Message?.Content ?? "";
    }

    private string BuildSystemPrompt()
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a coding agent running inside an isolated container.");
        sb.AppendLine("Your job is to make minimal, focused code changes to complete the given task.");
        sb.AppendLine();
        sb.AppendLine("RULES:");
        foreach (var rule in context.Rules)
            sb.AppendLine($"- {rule}");
        sb.AppendLine();
        sb.AppendLine("OUTPUT FORMAT:");
        sb.AppendLine("When you need to create or modify a file, use this exact format:");
        sb.AppendLine();
        sb.AppendLine("---FILE_EDIT: path/to/file.cs---");
        sb.AppendLine("<entire new file content>");
        sb.AppendLine("---END_FILE_EDIT---");
        sb.AppendLine();
        sb.AppendLine("You can include multiple FILE_EDIT blocks in a single response.");
        sb.AppendLine("When all changes are complete, include [DONE] at the end of your response.");
        sb.AppendLine("Keep changes minimal and scoped. Do not modify files unrelated to the task.");

        return sb.ToString();
    }

    private string BuildInitialUserMessage()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## Task: {spec.Task.Title}");
        sb.AppendLine();
        sb.AppendLine(spec.Task.Description);
        sb.AppendLine();

        if (spec.Task.AcceptanceCriteria.Count > 0)
        {
            sb.AppendLine("## Acceptance Criteria:");
            foreach (var criterion in spec.Task.AcceptanceCriteria)
                sb.AppendLine($"- {criterion}");
            sb.AppendLine();
        }

        if (context.RelevantFilesShortlist.Count > 0)
        {
            sb.AppendLine("## Relevant Files:");
            foreach (var file in context.RelevantFilesShortlist)
            {
                sb.AppendLine($"### {file}");
                var fullPath = Path.Combine(repoPath, file);
                if (File.Exists(fullPath))
                {
                    var content = File.ReadAllText(fullPath);
                    sb.AppendLine("```");
                    sb.AppendLine(content.Length > 10000 ? content[..10000] + "\n... (truncated)" : content);
                    sb.AppendLine("```");
                }
                else
                {
                    sb.AppendLine("*(file not found)*");
                }
                sb.AppendLine();
            }
        }

        if (context.SearchResults.Count > 0)
        {
            sb.AppendLine("## Search Results:");
            foreach (var sr in context.SearchResults)
            {
                sb.AppendLine($"Query: `{sr.Query}`");
                foreach (var match in sr.Matches)
                {
                    sb.AppendLine($"- {match.Path}:{match.Line} - {match.Snippet}");
                }
            }
            sb.AppendLine();
        }

        if (context.LinkedTexts.Count > 0)
        {
            sb.AppendLine("## Linked Context:");
            foreach (var lt in context.LinkedTexts)
            {
                sb.AppendLine($"[{lt.Kind}]: {lt.Content}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("Please analyze the task and make the necessary code changes.");

        return sb.ToString();
    }

    private static List<FileEdit> ParseFileEdits(string response)
    {
        List<FileEdit> edits = [];
        var pattern = FileEditRegex();

        foreach (Match match in pattern.Matches(response))
        {
            var filePath = match.Groups[1].Value.Trim();
            var content = match.Groups[2].Value;

            if (content.StartsWith('\n'))
                content = content[1..];
            if (content.EndsWith('\n'))
                content = content[..^1];

            edits.Add(new FileEdit(filePath, content));
        }

        return edits;
    }

    [GeneratedRegex(@"---FILE_EDIT:\s*(.+?)---\n([\s\S]*?)---END_FILE_EDIT---", RegexOptions.Multiline)]
    private static partial Regex FileEditRegex();

    private async Task ApplyEdit(FileEdit edit)
    {
        ValidatePathPolicy(edit.FilePath);

        var fullPath = Path.Combine(repoPath, edit.FilePath.Replace('/', Path.DirectorySeparatorChar));
        var canonicalPath = Path.GetFullPath(fullPath);
        var canonicalRepo = Path.GetFullPath(repoPath);

        if (!canonicalPath.StartsWith(canonicalRepo, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"[AgentLoop] BLOCKED: Path traversal attempt: {edit.FilePath}");
            return;
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (directory is not null)
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(fullPath, edit.Content);
        Console.WriteLine($"[AgentLoop] Wrote: {edit.FilePath}");
    }

    private void ValidatePathPolicy(string filePath)
    {
        var normalized = filePath.Replace('\\', '/');
        foreach (var disallowed in spec.RunProfile.Policies.DisallowedPaths)
        {
            if (normalized.StartsWith(disallowed.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Policy violation: file '{filePath}' is in disallowed path '{disallowed}'");
            }
        }

        if (!spec.RunProfile.Policies.AllowFileWritesOutsideScope && spec.Task.ScopeHints.Count > 0)
        {
            var inScope = spec.Task.ScopeHints.Any(scope =>
                normalized.StartsWith(scope.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));

            if (!inScope)
            {
                Console.WriteLine($"[AgentLoop] WARNING: File '{filePath}' is outside scope hints. Allowing for MVP.");
            }
        }
    }

    private static string ResolveModelName(RunProfile profile)
    {
        var modelEnv = profile.ModelTier switch
        {
            Contracts.Enums.ModelTier.Cheap => Environment.GetEnvironmentVariable("MODEL_CHEAP"),
            Contracts.Enums.ModelTier.Balanced => Environment.GetEnvironmentVariable("MODEL_BALANCED"),
            Contracts.Enums.ModelTier.Strong => Environment.GetEnvironmentVariable("MODEL_STRONG"),
            _ => null
        };

        return modelEnv ?? profile.ModelName;
    }
}

internal sealed record FileEdit(string FilePath, string Content);

public sealed record ConversationMessage(string Role, string Content);
