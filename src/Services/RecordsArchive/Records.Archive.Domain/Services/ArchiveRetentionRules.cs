using Records.Archive.Domain.Entities;
using Records.Archive.Domain.Enums;

namespace Records.Archive.Domain.Services;

public static class ArchiveChecklistDefaults
{
    public const string RetentionScheduleCode = "ret-archive-docs";

    public static (string OwnerRole, int SlaHours, bool Required) GovernanceFor(ArchiveChecklistStep step) => step switch
    {
        ArchiveChecklistStep.Classified => ("archive", 24, true),
        ArchiveChecklistStep.Ingested => ("archive", 48, true),
        ArchiveChecklistStep.Held => ("archive", 4, false),
        ArchiveChecklistStep.Released => ("archive", 24, false),
        ArchiveChecklistStep.DisposeRequested => ("archive", 72, true),
        ArchiveChecklistStep.DisposeApproved => ("archive", 48, true),
        ArchiveChecklistStep.Disposed => ("archive", 24, true),
        _ => ("archive", 48, false)
    };

    public static IEnumerable<ArchiveChecklist> CreateFor(Guid correlationId) =>
        Enum.GetValues<ArchiveChecklistStep>().Select(step =>
        {
            var (owner, sla, required) = GovernanceFor(step);
            return new ArchiveChecklist
            {
                CorrelationId = correlationId,
                Step = step,
                IsRequired = required,
                OwnerRole = owner,
                SlaHours = sla
            };
        });

    public static void Mark(ArchiveChecklist item, Guid? by, string evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence))
            throw new InvalidOperationException("EvidenceRef مطلوب.");
        item.IsCompleted = true;
        item.CompletedAt = DateTime.UtcNow;
        item.CompletedBy = by;
        item.EvidenceRef = evidence.Trim();
    }
}

public static class ArchiveRetentionRules
{
    public static bool IsRetentionExpired(ArchiveItem item)
    {
        if (item.RetentionPeriodYears is null or 0) return false;
        return item.CreatedAt.AddYears(item.RetentionPeriodYears.Value) < DateTime.UtcNow;
    }

    public static bool RequiresClassification(ArchiveItem item) =>
        item.Format == ArchiveFormat.Digital;

    public static bool HasValidFilePlan(ArchiveItem item, DocumentClassification? classification) =>
        item.ClassificationId.HasValue
        && classification is not null
        && (classification.RetentionYears is > 0
            || string.Equals(classification.RetentionCode, ArchiveChecklistDefaults.RetentionScheduleCode, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(classification.RetentionCode));

    public static bool CanPlaceHold(ArchiveItem item) =>
        !item.IsOnLegalHold && item.DisposeApprovedAt is null;

    public static bool CanReleaseHold(ArchiveItem item) => item.IsOnLegalHold;

    public static bool CanRequestDispose(ArchiveItem item, bool hasActiveLoans) =>
        !item.IsOnLegalHold
        && !hasActiveLoans
        && IsRetentionExpired(item)
        && !item.IsDisposeCancelled
        && item.DisposeRequestedAt is null
        && item.DisposeApprovedAt is null;

    public static bool CanApproveDispose(ArchiveItem item, Guid? requesterId, Guid approverId) =>
        item.DisposeRequestedAt.HasValue
        && !item.IsDisposeCancelled
        && !item.IsOnLegalHold
        && item.DisposeApprovedAt is null
        && approverId != Guid.Empty
        && (!requesterId.HasValue || requesterId.Value != approverId);

    public static bool CanDispose(ArchiveItem item, bool hasActiveLoans) =>
        !item.IsOnLegalHold
        && !hasActiveLoans
        && IsRetentionExpired(item)
        && item.DisposeApprovedAt.HasValue
        && !item.IsDisposeCancelled;

    /// <summary>Legacy gate kept for tests — prefer CanRequestDispose / CanDispose with approval.</summary>
    public static bool CanDisposeLegacy(ArchiveItem item, bool hasActiveLoans)
    {
        if (item.IsOnLegalHold || hasActiveLoans) return false;
        return IsRetentionExpired(item);
    }

    public static bool IsOverdue(LoanRecord loan) =>
        loan.ReturnedDate is null && loan.DueDate < DateTime.UtcNow;

    public static void PlaceHold(ArchiveItem item, string? reason)
    {
        item.IsOnLegalHold = true;
        item.LegalHoldAt = DateTime.UtcNow;
        item.LegalHoldReason = string.IsNullOrWhiteSpace(reason) ? "Legal hold" : reason.Trim();
    }

    public static void ReleaseHold(ArchiveItem item)
    {
        item.IsOnLegalHold = false;
        item.LegalHoldAt = null;
        item.LegalHoldReason = null;
    }

    public static void SoftCancelDispose(ArchiveItem item, string? reason)
    {
        item.IsDisposeCancelled = true;
        item.DisposeCancelReason = string.IsNullOrWhiteSpace(reason) ? "إلغاء طلب إتلاف" : reason.Trim();
        item.DisposeRequestedAt = null;
        item.DisposeRequestedBy = null;
    }

    public static bool HasChecksum(string? checksum) =>
        !string.IsNullOrWhiteSpace(checksum) && checksum.Trim().Length >= 8;

    public static string CompletenessFingerprint(IEnumerable<DigitalFileRecord> files)
    {
        var parts = files
            .OrderBy(f => f.Id)
            .Select(f => $"{f.Id:N}:{f.Checksum ?? ""}:{f.FileId?.ToString("N") ?? ""}");
        return string.Join("|", parts);
    }
}

public static class ArchiveEvidenceRules
{
    public static CustodyEvent CreateCustody(Guid archiveItemId, string action, string? evidence, Guid? actor) =>
        new()
        {
            Id = Guid.NewGuid(),
            ArchiveItemId = archiveItemId,
            Action = action,
            EvidenceRef = evidence,
            ActorUserId = actor,
            OccurredAt = DateTime.UtcNow
        };
}
