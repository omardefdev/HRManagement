namespace HRManagement.FileStorage;

public static class FileStorageBuckets
{
    public const string HrCore = "hr-core";
    public const string HrContracts = "hr-contracts";
    public const string IdCard = "idcard";
    public const string RecordsArchive = "records-archive";
    public const string FileStorage = "file-storage";
    /// <summary>Pending/infected objects before Clean promotion (FILE-002).</summary>
    public const string Quarantine = "file-storage-quarantine";

    public static readonly IReadOnlyList<string> All =
    [
        HrCore,
        HrContracts,
        IdCard,
        RecordsArchive,
        FileStorage,
        Quarantine
    ];
}
