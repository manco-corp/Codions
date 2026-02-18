using System.Text.RegularExpressions;

namespace Codions.Infrastructure.Security;

public static partial class TokenRedactor
{
    public static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        text = GithubTokenRegex().Replace(text, "ghp_***REDACTED***");
        text = GenericBearerRegex().Replace(text, "Bearer ***REDACTED***");
        text = GenericApiKeyRegex().Replace(text, "$1***REDACTED***");

        return text;
    }

    [GeneratedRegex(@"ghp_[A-Za-z0-9_]{36,}", RegexOptions.Compiled)]
    private static partial Regex GithubTokenRegex();

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.Compiled)]
    private static partial Regex GenericBearerRegex();

    [GeneratedRegex(@"(api[_-]?key[""']?\s*[:=]\s*[""']?)[A-Za-z0-9\-._]{10,}", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex GenericApiKeyRegex();
}
