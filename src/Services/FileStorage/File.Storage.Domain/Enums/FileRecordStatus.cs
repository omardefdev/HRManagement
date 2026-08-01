namespace File.Storage.Domain.Enums;

public static class FileRecordStatus
{
    public const string Pending = "Pending";
    public const string Active = "Active";
    /// <summary>Soft-deleted; object purge queued (FILE-001 two-phase delete).</summary>
    public const string PendingPurge = "PendingPurge";
    public const string Deleted = "Deleted";
}
