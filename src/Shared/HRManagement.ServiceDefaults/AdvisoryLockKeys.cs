namespace HRManagement.ServiceDefaults;

/// <summary>Stable PostgreSQL advisory lock keys for singleton background jobs.</summary>
public static class AdvisoryLockKeys
{
    public const long FileScanProcessor = 4_001_001;
    public const long FileGarbageCollector = 4_001_002;
    public const long GatewayAuditRetention = 4_001_003;
    public const long AuditWormExporter = 4_001_004;
    public const long InboxRetention = 4_001_005;
    public const long FileReferenceOutbox = 4_001_006;
    public const long SiemDetector = 4_001_007;
    public const long GatewayAuditChain = 4_001_008;
    /// <summary>FUN-029 (J): HR.Contracts hire commencement watch.</summary>
    public const long HireCommencementWatch = 4_001_009;
    /// <summary>PLAN-45: Admin.Logistics, the approval start of waiting custody and loss requests.</summary>
    public const long AssetApprovalStartRetry = 4_001_010;
    /// <summary>PLAN-46: IDCard, the owner-side sweep of card requests.</summary>
    public const long CardRequestApprovalSweep = 4_001_011;
    /// <summary>PLAN-46: Compliance, the owner-side sweep of disciplinary actions.</summary>
    public const long DisciplinaryApprovalSweep = 4_001_012;
}
