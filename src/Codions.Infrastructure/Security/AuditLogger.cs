using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Codions.Infrastructure.Security;

public class AuditLogger
{
    private readonly ILogger<AuditLogger> _logger;
    private readonly string _auditDir;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AuditLogger(ILogger<AuditLogger> logger, string dataPath = "data")
    {
        _logger = logger;
        _auditDir = Path.Combine(dataPath, "audit");
        Directory.CreateDirectory(_auditDir);
    }

    public async Task LogJobEventAsync(AuditEntry entry)
    {
        _logger.LogInformation(
            "[AUDIT] Job={JobId} Event={Event} Requester={Requester} Repo={Repo} Branch={Branch}",
            entry.JobId, entry.Event, entry.Requester, entry.Repo, entry.Branch);

        var fileName = $"{DateTime.UtcNow:yyyyMMdd}.audit.jsonl";
        var filePath = Path.Combine(_auditDir, fileName);
        var json = JsonSerializer.Serialize(entry, JsonOptions);

        await File.AppendAllTextAsync(filePath, json + Environment.NewLine);
    }
}

public sealed record AuditEntry
{
    public required string JobId { get; init; }
    public required string Event { get; init; }
    public required DateTime TimestampUtc { get; init; }
    public string? Requester { get; init; }
    public string? Repo { get; init; }
    public string? Branch { get; init; }
    public string? PrUrl { get; init; }
    public string? Details { get; init; }
}
