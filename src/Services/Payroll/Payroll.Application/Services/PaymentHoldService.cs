using Payroll.Domain.Constants;
using Payroll.Domain.Entities;

namespace Payroll.Application.Services;

/// <summary>FRD-001 payment hold release with four-eyes control.</summary>
public class PaymentHoldService
{
    /// <summary>
    /// Releases an active payment hold. Requires distinct releaser and approver (four-eyes).
    /// Throws when <paramref name="releasedBy"/> equals <paramref name="approvedBy"/>.
    /// </summary>
    public PaymentHold ReleaseHold(
        PaymentHold hold,
        Guid releasedBy,
        Guid approvedBy,
        string reason,
        Guid? evidenceRecordId = null)
    {
        ArgumentNullException.ThrowIfNull(hold);

        if (releasedBy == Guid.Empty)
            throw new ArgumentException("ReleasedBy is required.", nameof(releasedBy));
        if (approvedBy == Guid.Empty)
            throw new ArgumentException("ApprovedBy is required.", nameof(approvedBy));
        if (releasedBy == approvedBy)
            throw new InvalidOperationException(
                "Single-operator release is blocked; ReleasedBy and ApprovedBy must differ (four-eyes).");
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Release reason is required.", nameof(reason));
        if (!string.Equals(hold.Status, PaymentHoldStatus.Active, StringComparison.Ordinal))
            throw new InvalidOperationException($"Hold {hold.Id} is not Active (status={hold.Status}).");

        hold.Status = PaymentHoldStatus.Released;
        hold.ReleasedBy = releasedBy;
        hold.ReleaseApprovedBy = approvedBy;
        hold.ReleaseReason = reason.Trim();
        hold.EvidenceRecordId = evidenceRecordId;
        hold.ReleasedAtUtc = DateTime.UtcNow;
        hold.UpdatedAt = DateTime.UtcNow;
        return hold;
    }

    /// <summary>
    /// Resolves or dismisses a fraud alert. Dismissals that release an associated hold require four-eyes.
    /// </summary>
    public void ResolveAlert(
        FraudAlert alert,
        PaymentHold? linkedHold,
        Guid resolvedBy,
        Guid approvedBy,
        string resolutionReason,
        string targetStatus,
        bool releaseHold)
    {
        ArgumentNullException.ThrowIfNull(alert);

        if (resolvedBy == Guid.Empty)
            throw new ArgumentException("ResolvedBy is required.", nameof(resolvedBy));
        if (string.IsNullOrWhiteSpace(resolutionReason))
            throw new ArgumentException("Resolution reason is required.", nameof(resolutionReason));

        var dismissingOrResolving = targetStatus is FraudAlertStatus.Resolved or FraudAlertStatus.Dismissed;
        if (!dismissingOrResolving)
            throw new ArgumentException("Target status must be Resolved or Dismissed.", nameof(targetStatus));

        if (releaseHold)
        {
            if (linkedHold is null)
                throw new InvalidOperationException("Cannot release hold: no linked PaymentHold provided.");
            // Four-eyes enforced inside ReleaseHold (ResolvedBy != ApprovedBy).
            ReleaseHold(linkedHold, resolvedBy, approvedBy, resolutionReason);
        }

        alert.Status = targetStatus;
        alert.ResolvedBy = resolvedBy;
        alert.ApprovedBy = approvedBy;
        alert.ResolutionReason = resolutionReason.Trim();
        alert.UpdatedAt = DateTime.UtcNow;
    }
}
