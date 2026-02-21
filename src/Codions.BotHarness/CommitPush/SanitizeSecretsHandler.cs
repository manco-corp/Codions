using Codions.BotHarness;
using Codions.BotHarness.Helpers;

namespace Codions.BotHarness.CommitPush;

/// <summary>
/// Second handler: scans staged diff for secrets, redacts files if needed, re-adds and verifies.
/// </summary>
internal sealed class SanitizeSecretsHandler : ICommitPushHandler
{
    public async Task HandleAsync(CommitPushContext context, Func<CommitPushContext, CancellationToken, Task> next, CancellationToken cancellationToken = default)
    {
        var detections = context.SecretScanner.DetectSecretsInText(
            DiffHelper.ExtractAddedLines(
                await GitHelper.GetStagedDiffAsync(context.RepoPath))
        );
        if (detections.Count == 0)
        {
            await next(context, cancellationToken);
            return;
        }

        Console.WriteLine(
            $"[BotHarness] Detected {detections.Count} potential secret(s) in staged diff. Attempting auto-sanitization...");

        var findings = await context.SecretScanner.RedactFilesAsync(context.RepoPath, await GitHelper.GetStagedChangedFilesAsync(context.RepoPath));
        if (findings.Count == 0)
        {
            throw new InvalidOperationException(
                "Potential secrets detected in staged changes, but no automatic sanitization could be applied. Please remove credentials from generated files.");
        }

        await context.GitRunner.RunOrThrowAsync("add -A", "Git add failed after secret sanitization.", cancellationToken);
        var remaining = context.SecretScanner.DetectSecretsInText(
            DiffHelper.ExtractAddedLines(await GitHelper.GetStagedDiffAsync(context.RepoPath)));
        if (remaining.Count > 0)
        {
            var names = string.Join(", ", remaining.Distinct(StringComparer.OrdinalIgnoreCase).Take(5));
            throw new InvalidOperationException(
                $"Potential secrets remain after sanitization ({names}). Please remove sensitive values before push.");
        }

        var fileCount = findings.Select(f => f.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        Console.WriteLine($"[BotHarness] Auto-sanitized {fileCount} file(s) containing potential secrets.");

        await next(context, cancellationToken);
    }
}
