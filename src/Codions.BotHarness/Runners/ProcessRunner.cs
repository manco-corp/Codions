using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Codions.BotHarness.Runners;

/// <summary>
/// Runs external processes with optional timeout and PATH prepend.
/// All failure output is redacted before logging to avoid leaking secrets.
/// </summary>
internal static class ProcessRunner
{
    public static async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(
        string fileName,
        string arguments,
        string workingDir,
        TimeSpan? timeout = null,
        string? prependToPath = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!string.IsNullOrEmpty(prependToPath) && Directory.Exists(prependToPath))
        {
            var pathKey = Environment.OSVersion.Platform == PlatformID.Win32NT ? "Path" : "PATH";
            var currentPath = Environment.GetEnvironmentVariable(pathKey) ?? "";
            foreach (var kvp in Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>())
            {
                if (kvp is { Key: string k, Value: string v })
                    psi.Environment[k] = v;
            }
            psi.Environment[pathKey] = prependToPath + Path.PathSeparator + currentPath;
        }

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {fileName} {arguments}");

        if (timeout is { } t)
        {
            using var cts = new CancellationTokenSource(t);
            var stdout = await process.StandardOutput.ReadToEndAsync(cts.Token);
            var stderr = await process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);
            return (process.ExitCode, stdout, stderr);
        }

        var outStr = await process.StandardOutput.ReadToEndAsync();
        var errStr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, outStr, errStr);
    }

    public static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        text = Regex.Replace(text, @"gh[pousr]_[A-Za-z0-9_]{30,}", "ghp_***REDACTED***", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"github_pat_[A-Za-z0-9_]{20,}", "github_pat_***REDACTED***", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"Bearer\s+[A-Za-z0-9\-._~+/]+=*", "Bearer ***REDACTED***", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"(?i)(authorization:\s*basic\s+)[A-Za-z0-9+/=]+", "$1***REDACTED***");
        text = Regex.Replace(text, @"(?i)(x-access-token:)[^@\s]+(@)", "$1***REDACTED***$2");
        return text;
    }

    public static string Truncate(string output, int maxChars)
    {
        if (output.Length <= maxChars)
            return output;
        return output[..maxChars] + "\n... (truncated)";
    }
}
