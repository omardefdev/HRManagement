using MediatR;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Application.Features.PartialDisbursement;

public record CreateFundingDecisionCommand(
    Guid PayrollRunId,
    decimal RequestedAmount,
    decimal ApprovedAmount,
    decimal FundingRatio,
    Guid? PriorityRuleSetId,
    Guid ApprovedBy) : IRequest<(bool Success, Guid? Id, string? Error)>;

public class CreateFundingDecisionHandler(IPayrollRepository repository)
    : IRequestHandler<CreateFundingDecisionCommand, (bool Success, Guid? Id, string? Error)>
{
    public async Task<(bool Success, Guid? Id, string? Error)> Handle(
        CreateFundingDecisionCommand request,
        CancellationToken cancellationToken)
    {
        var run = await repository.GetPayrollRunAsync(request.PayrollRunId, includeLines: true, cancellationToken);
        if (run is null)
            return (false, null, "التشغيل غير موجود.");

        if (run.Status is not (PayrollRunStatus.Approved or PayrollRunStatus.Posted))
            return (false, null, "يجب اعتماد أو ترحيل التشغيل قبل قرار التمويل.");

        if (request.FundingRatio is < 0 or > 1)
            return (false, null, "نسبة التمويل يجب أن تكون بين 0 و 1.");

        var decision = new FundingDecision
        {
            PayrollRunId = run.Id,
            RequestedAmount = request.RequestedAmount,
            ApprovedAmount = request.ApprovedAmount,
            FundingRatio = request.FundingRatio,
            PriorityRuleSetId = request.PriorityRuleSetId,
            ApprovedBy = request.ApprovedBy,
            DecisionDate = DateTime.UtcNow
        };

        await repository.AddAsync(decision, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return (true, decision.Id, null);
    }
}
