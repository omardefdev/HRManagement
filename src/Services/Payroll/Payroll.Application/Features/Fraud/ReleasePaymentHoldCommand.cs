using MediatR;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Application.Services;
using Payroll.Domain.Constants;
using Payroll.Domain.Entities;

namespace Payroll.Application.Features.Fraud;

public record ReleasePaymentHoldCommand(
    Guid HoldId,
    Guid ReleasedBy,
    Guid ApproverUserId,
    string ReleaseReason,
    Guid? EvidenceRecordId = null) : IRequest<(bool Success, string? Error)>;

public class ReleasePaymentHoldHandler(
    IPayrollRepository repository,
    PaymentHoldService holdService)
    : IRequestHandler<ReleasePaymentHoldCommand, (bool Success, string? Error)>
{
    public async Task<(bool Success, string? Error)> Handle(
        ReleasePaymentHoldCommand request,
        CancellationToken cancellationToken)
    {
        var hold = await repository.PaymentHolds.FirstOrDefaultAsync(h => h.Id == request.HoldId, cancellationToken);
        if (hold is null)
            return (false, "حجز الصرف غير موجود.");

        try
        {
            holdService.ReleaseHold(
                hold,
                request.ReleasedBy,
                request.ApproverUserId,
                request.ReleaseReason,
                request.EvidenceRecordId);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return (false, ex.Message);
        }

        if (hold.FraudAlertId.HasValue)
        {
            var alert = await repository.FraudAlerts.FirstOrDefaultAsync(
                a => a.Id == hold.FraudAlertId.Value,
                cancellationToken);
            if (alert is not null && alert.Status is FraudAlertStatus.Open or FraudAlertStatus.UnderReview)
            {
                alert.Status = FraudAlertStatus.Resolved;
                alert.ResolvedBy = request.ReleasedBy;
                alert.ApprovedBy = request.ApproverUserId;
                alert.ResolutionReason = request.ReleaseReason;
                alert.UpdatedAt = DateTime.UtcNow;
            }
        }

        await repository.SaveChangesAsync(cancellationToken);
        return (true, null);
    }
}
