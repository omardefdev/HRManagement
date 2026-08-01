namespace HRManagement.SharedKernel.Privacy;

/// <summary>PRV-002: statutory individual-rights request types.</summary>
public enum DsarRequestType
{
    Access = 0,
    Correction = 1,
    Restriction = 2,
    Deletion = 3,
    Objection = 4,
    Appeal = 5
}

/// <summary>PRV-002: DSAR lifecycle status (extends GOV-001 baseline).</summary>
public enum DsarRequestStatus
{
    Received = 0,
    IdentityVerification = 1,
    InProgress = 2,
    PendingReview = 3,
    Fulfilled = 4,
    PartiallyFulfilled = 5,
    Rejected = 6,
    AppealOpen = 7,
    Closed = 8
}

/// <summary>
/// National DSAR entity contract (PRV-002). Compliance service maps to/from this model.
/// No direct DB edits — all changes via <see cref="ServiceDefaults.Privacy.IDsarService"/>.
/// </summary>
public class DsarRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    /// <summary>Data subject (employee/person) identifier within tenant boundary.</summary>
    public Guid SubjectId { get; set; }

    public DsarRequestType RequestType { get; set; }

    public DsarRequestStatus Status { get; set; } = DsarRequestStatus.Received;

    /// <summary>Statutory or configured SLA deadline (UTC).</summary>
    public DateTime DeadlineUtc { get; set; }

    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>SHA-256 hex of national ID when SubjectId unknown (never store raw).</summary>
    public string? SubjectNationalIdHash { get; set; }

    /// <summary>Identity proof method (e.g. in-person, digital-id, delegated-guardian).</summary>
    public string? IdentityProofMethod { get; set; }

    /// <summary>JSON array of discovered source systems (HR.Core, Payroll, File.Storage, …).</summary>
    public string? DiscoveryScopeJson { get; set; }

    /// <summary>Redacted discovery package delivered to subject at fulfill time.</summary>
    public string? DiscoveryPackageJson { get; set; }

    /// <summary>Legal exception code when request partially denied (third-party rights, legal hold, …).</summary>
    public string? ExceptionCode { get; set; }

    public string? ExceptionReason { get; set; }

    /// <summary>Independent appeal case id when RequestType is Appeal or status is AppealOpen.</summary>
    public Guid? AppealOfRequestId { get; set; }

    public string? Notes { get; set; }

    public Guid? AssignedToUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Allowed status transitions for PRV-002 workflow validation.</summary>
public static class DsarWorkflowRules
{
    public static bool IsAllowedTransition(DsarRequestStatus from, DsarRequestStatus to) => (from, to) switch
    {
        (DsarRequestStatus.Received, DsarRequestStatus.IdentityVerification) => true,
        (DsarRequestStatus.Received, DsarRequestStatus.Rejected) => true,
        (DsarRequestStatus.IdentityVerification, DsarRequestStatus.InProgress) => true,
        (DsarRequestStatus.IdentityVerification, DsarRequestStatus.Rejected) => true,
        (DsarRequestStatus.InProgress, DsarRequestStatus.PendingReview) => true,
        (DsarRequestStatus.InProgress, DsarRequestStatus.Rejected) => true,
        (DsarRequestStatus.PendingReview, DsarRequestStatus.Fulfilled) => true,
        (DsarRequestStatus.PendingReview, DsarRequestStatus.PartiallyFulfilled) => true,
        (DsarRequestStatus.PendingReview, DsarRequestStatus.Rejected) => true,
        (DsarRequestStatus.PartiallyFulfilled, DsarRequestStatus.AppealOpen) => true,
        (DsarRequestStatus.Rejected, DsarRequestStatus.AppealOpen) => true,
        (DsarRequestStatus.AppealOpen, DsarRequestStatus.InProgress) => true,
        (DsarRequestStatus.Fulfilled, DsarRequestStatus.Closed) => true,
        (DsarRequestStatus.PartiallyFulfilled, DsarRequestStatus.Closed) => true,
        (DsarRequestStatus.Rejected, DsarRequestStatus.Closed) => true,
        _ => false
    };

    /// <summary>Default SLA days per request type (configurable at tenant level in production).</summary>
    public static int DefaultSlaDays(DsarRequestType type) => type switch
    {
        DsarRequestType.Access => 30,
        DsarRequestType.Correction => 30,
        DsarRequestType.Restriction => 30,
        DsarRequestType.Deletion => 45,
        DsarRequestType.Objection => 30,
        DsarRequestType.Appeal => 30,
        _ => 30
    };

    public static DateTime ComputeDeadlineUtc(DsarRequestType type, DateTime requestedAtUtc) =>
        requestedAtUtc.AddDays(DefaultSlaDays(type));
}
