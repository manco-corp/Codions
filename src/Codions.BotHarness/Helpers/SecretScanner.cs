using System.Text.RegularExpressions;

namespace Codions.BotHarness.Helpers;

/// <summary>
/// Scans the working tree for secrets and redacts them before commit,
/// preventing GitHub push protection from blocking the push.
/// </summary>
public sealed partial class SecretScanner(string githubToken)
{
    private static readonly HashSet<string> SkipDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules", ".next", "bin", "obj", "dist", "__pycache__", ".nuget"
    };

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".ico", ".bmp", ".webp",
        ".woff", ".woff2", ".ttf", ".eot",
        ".zip", ".gz", ".tar", ".7z", ".rar",
        ".dll", ".exe", ".so", ".dylib",
        ".pdf", ".doc", ".docx",
        ".lock"
    };

    private static readonly (string Name, Regex Pattern, string Replacement)[] SecretRules =
    [
        ("GitHubPAT", GithubPatRegex(), "ghp_***REDACTED***"),
        ("GitHubFineGrainedPAT", GithubFineGrainedPatRegex(), "github_pat_***REDACTED***"),
        ("GitHubOAuth", GithubOAuthRegex(), "gho_***REDACTED***"),
        ("BearerToken", GenericBearerRegex(), "Bearer ***REDACTED***"),
        ("ApiKey", GenericApiKeyRegex(), "$1***REDACTED***"),
        ("AWSKey", AwsKeyRegex(), "***REDACTED***"),
        ("PrivateKey", PrivateKeyBlockRegex(), "-----BEGIN PRIVATE KEY-----\n***REDACTED***\n-----END PRIVATE KEY-----")
    ];

    public async Task<List<SecretFinding>> ScanAndRedactAsync(string repoPath)
    {
        var files = EnumerateSourceFiles(repoPath)
            .Select(path => Path.GetRelativePath(repoPath, path).Replace('\\', '/'))
            .ToList();
        return await RedactFilesAsync(repoPath, files);
    }

    public List<string> DetectSecretsInText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        List<string> findings = [];
        if (!string.IsNullOrEmpty(githubToken) && githubToken.Length >= 8 &&
            text.Contains(githubToken, StringComparison.Ordinal))
            findings.Add("ExactTokenMatch");

        foreach (var (name, pattern, _) in SecretRules)
        {
            if (pattern.IsMatch(text))
                findings.Add(name);
        }

        return findings
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<List<SecretFinding>> RedactFilesAsync(string repoPath, IEnumerable<string> relativePaths)
    {
        List<SecretFinding> findings = [];
        foreach (var relativePath in relativePaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var fullPath = Path.Combine(repoPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath) || IsBinaryFile(fullPath))
                continue;

            string content;
            try
            {
                content = await File.ReadAllTextAsync(fullPath);
            }
            catch
            {
                continue;
            }

            var redacted = RedactText(content, relativePath, findings);
            if (!string.Equals(redacted, content, StringComparison.Ordinal))
                await File.WriteAllTextAsync(fullPath, redacted);
        }

        return findings;
    }

    private string RedactText(string content, string filePath, List<SecretFinding> findings)
    {
        var updated = content;
        if (!string.IsNullOrEmpty(githubToken) && githubToken.Length >= 8 &&
            updated.Contains(githubToken, StringComparison.Ordinal))
        {
            updated = updated.Replace(githubToken, "***REDACTED***", StringComparison.Ordinal);
            findings.Add(new SecretFinding(filePath, "ExactTokenMatch"));
        }

        foreach (var (name, pattern, replacement) in SecretRules)
        {
            if (!pattern.IsMatch(updated))
                continue;

            updated = pattern.Replace(updated, replacement);
            findings.Add(new SecretFinding(filePath, name));
        }

        return updated;
    }

    private static IEnumerable<string> EnumerateSourceFiles(string rootPath)
    {
        var dirs = new Stack<string>();
        dirs.Push(rootPath);

        while (dirs.Count > 0)
        {
            var dir = dirs.Pop();

            string[] files;
            try
            {
                files = Directory.GetFiles(dir);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
                yield return file;

            try
            {
                foreach (var subDir in Directory.GetDirectories(dir))
                {
                    var name = Path.GetFileName(subDir);
                    if (!SkipDirectories.Contains(name))
                        dirs.Push(subDir);
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static bool IsBinaryFile(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (BinaryExtensions.Contains(ext))
            return true;

        try
        {
            using var stream = File.OpenRead(filePath);
            Span<byte> buffer = stackalloc byte[512];
            var bytesRead = stream.Read(buffer);
            for (var i = 0; i < bytesRead; i++)
            {
                if (buffer[i] == 0)
                    return true;
            }
        }
        catch
        {
            return true;
        }

        return false;
    }

    // GitHub classic PATs: ghp_, gho_, ghu_, ghs_
    [GeneratedRegex(@"gh[pousr]_[A-Za-z0-9_]{36,}", RegexOptions.Compiled)]
    private static partial Regex GithubPatRegex();

    // GitHub fine-grained PATs
    [GeneratedRegex(@"github_pat_[A-Za-z0-9_]{20,}", RegexOptions.Compiled)]
    private static partial Regex GithubFineGrainedPatRegex();

    // GitHub OAuth tokens
    [GeneratedRegex(@"gho_[A-Za-z0-9]{36,}", RegexOptions.Compiled)]
    private static partial Regex GithubOAuthRegex();

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-._~+/]{20,}=*", RegexOptions.Compiled)]
    private static partial Regex GenericBearerRegex();

    [GeneratedRegex(@"(?i)(api[_\-]?key[""']?\s*[:=]\s*[""']?)[A-Za-z0-9\-._]{10,}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex GenericApiKeyRegex();

    // AWS access key IDs
    [GeneratedRegex(@"AKIA[0-9A-Z]{16}", RegexOptions.Compiled)]
    private static partial Regex AwsKeyRegex();

    // PEM private key blocks
    [GeneratedRegex(@"-----BEGIN\s+(RSA\s+)?PRIVATE\s+KEY-----[\s\S]*?-----END\s+(RSA\s+)?PRIVATE\s+KEY-----",
        RegexOptions.Compiled)]
    private static partial Regex PrivateKeyBlockRegex();
}

public sealed record SecretFinding(string FilePath, string Pattern);