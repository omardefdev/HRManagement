namespace HRManagement.ServiceDefaults;

/// <summary>Cross-service durable sink for sensitive audit events (beyond Serilog).</summary>
public interface IDurableAuditSink
{
    bool TryPersist(DurableAuditEvent auditEvent);
}

public sealed class DurableAuditEvent
{
    public required string Domain { get; init; }
    public required string Action { get; init; }
    public string? ResourceSummary { get; init; }
    public string? UserSub { get; init; }
    public string? UserName { get; init; }
    public string? CorrelationId { get; init; }
}

/// <summary>
/// File-backed durable sink used by module services when no gateway queue is registered.
/// </summary>
public sealed class FileDurableAuditSink : IDurableAuditSink
{
    private readonly string _directory;
    private long _sequence;

    public FileDurableAuditSink(string? directory = null)
    {
        _directory = directory
            ?? Environment.GetEnvironmentVariable("HR_AUDIT_OVERFLOW_PATH")
            ?? Path.Combine(Path.GetTempPath(), "hrm-sensitive-audit");
        Directory.CreateDirectory(_directory);
    }

    public string DirectoryPath => _directory;

    public bool TryPersist(DurableAuditEvent auditEvent)
    {
        var seq = Interlocked.Increment(ref _sequence);
        var path = Path.Combine(_directory, $"sensitive-{DateTime.UtcNow:yyyyMMdd}.jsonl");
        var line = System.Text.Json.JsonSerializer.Serialize(new
        {
            SequenceNumber = seq,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            auditEvent.Domain,
            auditEvent.Action,
            auditEvent.ResourceSummary,
            auditEvent.UserSub,
            auditEvent.UserName,
            auditEvent.CorrelationId,
            Decision = "sensitive_access"
        });
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        using var writer = new StreamWriter(stream);
        writer.WriteLine(line);
        return true;
    }
}
