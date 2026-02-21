using Codions.Contracts.Enums;
using Codions.Contracts.Models;

namespace Codions.Core;

public class ModelTierRouter
{
    private static readonly string[] CheapSignals =
        ["rename", "docs", "typo", "format", "small fix", "formatting", "comment", "documentation"];

    private static readonly string[] StrongSignals =
        ["refactor", "security", "auth", "payment", "architecture", "redesign", "migration", "complex"];

    public static RunProfile Route(TaskInfo task, Preferences? preferences, RunProfileDefaults defaults)
    {
        var tier = DetermineModelTier(task, preferences);

        return new RunProfile
        {
            ModelTier = tier,
            ModelName = MapTierToModelName(tier),
            MaxAgentSteps = defaults.MaxAgentSteps,
            MaxWallClockMinutes = preferences?.MaxMinutes ?? defaults.MaxWallClockMinutes,
            MaxFixAttempts = defaults.MaxFixAttempts,
            LocalGates = new LocalGates { Format = true, Build = true, Tests = true },
            TestStrategy = BuildTestStrategy(task, defaults),
            Policies = new Policies
            {
                NoNetworkEgress = true,
                DisallowedPaths = [],
                AllowFileWritesOutsideScope = false
            }
        };
    }

    public ModelTier Escalate(ModelTier current)
    {
        return current switch
        {
            ModelTier.Cheap => ModelTier.Balanced,
            ModelTier.Balanced => ModelTier.Strong,
            ModelTier.Strong => ModelTier.Strong,
            _ => ModelTier.Strong
        };
    }

    private static ModelTier DetermineModelTier(TaskInfo task, Preferences? preferences)
    {
        if (preferences?.ModelHint is not null)
        {
            return preferences.ModelHint.ToLowerInvariant() switch
            {
                "cheap" => ModelTier.Cheap,
                "balanced" => ModelTier.Balanced,
                "strong" => ModelTier.Strong,
                _ => ModelTier.Balanced
            };
        }

        var text = $"{task.Title} {task.Description}".ToLowerInvariant();
        var scopeSize = task.ScopeHints.Count;

        var cheapScore = CheapSignals.Count(signal => text.Contains(signal));
        var strongScore = StrongSignals.Count(signal => text.Contains(signal));

        if (scopeSize <= 2 && task.AcceptanceCriteria.Count > 0)
            cheapScore++;

        if (scopeSize > 5 || string.IsNullOrWhiteSpace(task.Description) || task.Description.Length < 20)
            strongScore++;

        if (strongScore > cheapScore)
            return ModelTier.Strong;
        if (cheapScore > strongScore)
            return ModelTier.Cheap;

        return ModelTier.Balanced;
    }

    private static string MapTierToModelName(ModelTier tier) => tier switch
    {
        ModelTier.Cheap => "qwen2.5-coder:7b",
        ModelTier.Balanced => "qwen2.5-coder:14b",
        ModelTier.Strong => "qwen2.5-coder:32b",
        _ => "qwen2.5-coder:14b"
    };

    private static readonly string[] KnownTestPrefixes =
        ["dotnet test", "npm test", "npx ", "pytest", "go test", "cargo test", "mvn test", "gradle test"];

    private static TestStrategy BuildTestStrategy(TaskInfo task, RunProfileDefaults defaults)
    {
        var targeted = task.AcceptanceCriteria
            .Where(c => KnownTestPrefixes.Any(prefix =>
                c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return new TestStrategy
        {
            Mode = targeted.Count > 0 ? "targeted-first" : "full",
            TargetedCommands = targeted,
            FallbackCommand = "",
            MaxTestMinutes = defaults.MaxTestMinutes
        };
    }
}

public sealed record RunProfileDefaults
{
    public int MaxAgentSteps { get; init; } = 16;
    public int MaxWallClockMinutes { get; init; } = 25;
    public int MaxFixAttempts { get; init; } = 2;
    public int MaxTestMinutes { get; init; } = 15;
}
