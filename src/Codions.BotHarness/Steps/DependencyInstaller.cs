using Codions.BotHarness.Runners;
using Codions.Contracts.Models;

namespace Codions.BotHarness.Steps;

/// <summary>
/// Installs dependencies for the detected stack (npm/pip/dotnet).
/// </summary>
public sealed class DependencyInstaller(string repoPath, StackProfile stack)
{
    public async Task InstallAsync()
    {
        switch (stack.Name)
        {
            case "node" or "angular" when File.Exists(Path.Combine(repoPath, "package-lock.json")):
                Console.WriteLine("[BotHarness] Installing Node.js dependencies (npm ci)...");
                await RunAsync("npm", "ci", repoPath);
                break;
            case "node" or "angular":
                Console.WriteLine("[BotHarness] Installing Node.js dependencies (npm install)...");
                await RunAsync("npm", "install", repoPath);
                break;
            case "python":
                if (File.Exists(Path.Combine(repoPath, "requirements.txt")))
                {
                    Console.WriteLine("[BotHarness] Installing Python dependencies...");
                    await RunAsync("pip", "install -r requirements.txt", repoPath);
                }
                break;
            case "dotnet":
                Console.WriteLine("[BotHarness] Restoring .NET dependencies...");
                await RunAsync("dotnet", "restore", repoPath);
                break;
        }
    }

    private static async Task RunAsync(string fileName, string arguments, string workingDir)
    {
        var (exitCode, stdout, stderr) = await ProcessRunner.RunAsync(fileName, arguments, workingDir);
        if (exitCode != 0)
        {
            var safeCmd = ProcessRunner.Redact($"{fileName} {arguments}");
            Console.WriteLine($"[BotHarness] Process '{safeCmd}' stdout: {ProcessRunner.Redact(stdout)}");
            Console.WriteLine($"[BotHarness] Process '{safeCmd}' stderr: {ProcessRunner.Redact(stderr)}");
        }
    }
}
