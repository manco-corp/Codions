using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Codions.BotHarness.Llm;
using Codions.Contracts.Models;

namespace Codions.BotHarness.Runners;

/// <summary>
/// The agent loop calls the Ollama API to generate code changes iteratively.
/// It reads relevant files, builds a prompt, parses the model's file-edit instructions,
/// and applies them to the working tree.
/// </summary>
public partial class AgentLoop(JobSpec spec, ContextPack context, string repoPath, ILlmChatClient llm, StackProfile detectedStack)
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

            if (edits.Count == 0)
            {
                const int maxPreview = 1500;
                var preview = response.Length <= maxPreview ? response : response[..maxPreview] + "\n... (truncated)";
                Console.WriteLine("[AgentLoop] Raw response (no FILE_EDIT blocks parsed):");
                Console.WriteLine("---");
                Console.WriteLine(preview);
                Console.WriteLine("---");
            }

            if (edits.Count == 0 && response.Contains("[DONE]", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("[AgentLoop] Model signaled DONE with no edits.");
                Console.WriteLine("[AgentLoop] What to do next:");
                Console.WriteLine("  1. Check the raw response above: the model must use ---FILE_EDIT: path --- ... ---END_FILE_EDIT--- (not just prose + [DONE]).");
                Console.WriteLine("  2. Check Ollama model (MODEL_CHEAP/MODEL_BALANCED/MODEL_STRONG) and increase num_predict if the reply is truncated (~50–70 tokens may be too low for real edits).");
                Console.WriteLine("  3. Try a stronger or more instruction-following model if the model is outputting prose instead of FILE_EDIT blocks.");
                break;
            }

            if (edits.Count == 0)
            {
                Console.WriteLine("[AgentLoop] No file edits parsed from response. Asking for clarification...");
                _conversation.Add(new ConversationMessage("user",
                    "I didn't detect any file edits in your response. You MUST output changes using the exact FILE_EDIT format shown in the system instructions (---FILE_EDIT: path --- ... ---END_FILE_EDIT---). Do NOT reply with [DONE] until you have actually included at least one FILE_EDIT block with the requested code changes. If the task truly requires no code changes, explain briefly and then use [DONE]."));
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
        var chatMessages = new List<LlmChatMessage>
        {
            new("system", systemPrompt)
        };
        chatMessages.AddRange(messages.Select(m => new LlmChatMessage(m.Role, m.Content)));

        var result = await llm.SendChatAsync(_model, chatMessages, CancellationToken.None);

        var promptTokens = result.PromptEvalCount ?? 0;
        var completionTokens = result.EvalCount ?? 0;
        Console.WriteLine($"[AgentLoop] Tokens: {promptTokens} in / {completionTokens} out");

        return result.Content;
    }

    private string BuildSystemPrompt()
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a coding agent running inside an isolated container.");
        sb.AppendLine("Your job is to make minimal, focused code changes to complete the given task.");
        sb.AppendLine($"The repository uses the **{detectedStack.Name}** tech stack.");
        sb.AppendLine();
        sb.AppendLine("RULES:");
        foreach (var rule in context.Rules)
            sb.AppendLine($"- {rule}");
        sb.AppendLine();
        sb.AppendLine("OUTPUT FORMAT (mandatory):");
        sb.AppendLine("You MUST output file changes using this EXACT format. No other format is accepted.");
        sb.AppendLine();
        sb.AppendLine("---FILE_EDIT: <path from repo root>---");
        sb.AppendLine("<entire new file content>");
        sb.AppendLine("---END_FILE_EDIT---");
        sb.AppendLine();

        var example = detectedStack.PromptFileExample.Trim();
        if (!string.IsNullOrEmpty(example))
        {
            sb.AppendLine("CONCRETE EXAMPLE:");
            sb.AppendLine("Your response must look like this (copy the structure exactly):");
            sb.AppendLine();
            sb.AppendLine(example);
            sb.AppendLine();
            sb.AppendLine("[DONE]");
            sb.AppendLine();
        }

        sb.AppendLine("RULES for OUTPUT:");
        sb.AppendLine("- Do NOT reply with only [DONE]. You MUST output at least one ---FILE_EDIT--- ... ---END_FILE_EDIT--- block with the requested code changes before writing [DONE].");
        sb.AppendLine("- Use the path relative to the repository root (e.g. src/services/user.ts, not user.ts).");
        sb.AppendLine("- Include the COMPLETE file content between the markers; the block replaces the whole file.");
        sb.AppendLine("- You can include multiple ---FILE_EDIT: path --- ... ---END_FILE_EDIT--- blocks in one response.");
        sb.AppendLine("- Only write [DONE] at the very end when you have finished all file edits.");
        sb.AppendLine("- Do not describe changes in prose instead of FILE_EDIT blocks; the harness only applies edits from those blocks.");
        sb.AppendLine("- Keep changes minimal and scoped. Do not modify files unrelated to the task.");

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

    private async Task ApplyEdit(FileEdit edit)
    {
        ValidatePathPolicy(edit.FilePath);

        var fullPath = Path.Combine(repoPath, edit.FilePath.Replace('/', Path.DirectorySeparatorChar));
        var canonicalPath = Path.GetFullPath(fullPath);
        var canonicalRepo = Path.GetFullPath(repoPath);

        if (!canonicalPath.StartsWith(canonicalRepo, StringComparison.OrdinalIgnoreCase))
        {
            await Console.Error.WriteLineAsync($"[AgentLoop] BLOCKED: Path traversal attempt: {edit.FilePath}");
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

        if (spec.RunProfile.Policies.AllowFileWritesOutsideScope || spec.Task.ScopeHints.Count <= 0) return;
        var inScope = spec.Task.ScopeHints.Any(scope =>
            normalized.StartsWith(scope.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));

        if (!inScope)
        {
            Console.WriteLine($"[AgentLoop] WARNING: File '{filePath}' is outside scope hints. Allowing for MVP.");
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

    [GeneratedRegex(@"---FILE_EDIT:\s*(.+?)---\r?\n([\s\S]*?)\r?\n---END_FILE_EDIT---", RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex FileEditRegex();
}

internal sealed record FileEdit(string FilePath, string Content);

public sealed record ConversationMessage(string Role, string Content);
