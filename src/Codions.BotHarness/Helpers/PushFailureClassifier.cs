using Codions.BotHarness.Runners;

namespace Codions.BotHarness.Helpers;

internal static class PushFailureClassifier
{
    public static bool IsPushProtection(string stderr)
    {
        return stderr.Contains("GH013", StringComparison.OrdinalIgnoreCase)
               || stderr.Contains("push cannot contain secrets", StringComparison.OrdinalIgnoreCase)
               || stderr.Contains("secret scanning", StringComparison.OrdinalIgnoreCase)
               || stderr.Contains("push protection", StringComparison.OrdinalIgnoreCase);
    }

    public static string GetMessage(string stderr)
    {
        if (stderr.Contains("invalid username or token", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("authentication failed", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("password authentication is not supported", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("could not read Username", StringComparison.OrdinalIgnoreCase))
        {
            return "Git push failed: authentication failed. Ensure GitHub:Token is valid and has repo push permission.";
        }

        if (IsPushProtection(stderr))
            return
                "Git push failed: GitHub push protection blocked detected secrets. Remove/sanitize credentials before pushing.";

        if (stderr.Contains("permission denied", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("protected branch hook declined", StringComparison.OrdinalIgnoreCase))
        {
            return "Git push failed: permission or branch protection blocked the push.";
        }

        return $"Git push failed. Stderr: {ProcessRunner.Redact(stderr.Trim())}";
    }
}