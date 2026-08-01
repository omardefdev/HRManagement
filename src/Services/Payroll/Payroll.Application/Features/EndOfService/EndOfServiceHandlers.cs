using MediatR;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;

namespace Payroll.Application.Features.EndOfService;

public record CalculateEndOfServiceCommand(
    Guid EmployeeId,
    Guid? SeparationRecordId,
    DateTime CommencementDate,
    DateTime SeparationDate,
    decimal BaseSalary,
    int RemainingLeaveDays,
    decimal OutstandingAdvances,
    decimal OtherCompensation = 0) : IRequest<(bool Success, Guid? SettlementId, string? Error)>;

public class CalculateEndOfServiceHandler(IPayrollRepository repository)
    : IRequestHandler<CalculateEndOfServiceCommand, (bool Success, Guid? SettlementId, string? Error)>
{
    public async Task<(bool Success, Guid? SettlementId, string? Error)> Handle(
        CalculateEndOfServiceCommand request,
        CancellationToken cancellationToken)
    {
        var config = await repository.GetActiveConfigurationAsync(cancellationToken);
        var settlement = EndOfServiceCalculation.CreateSettlement(
            request.EmployeeId,
            request.SeparationRecordId,
            request.CommencementDate,
            request.SeparationDate,
            request.BaseSalary,
            request.RemainingLeaveDays,
            request.OutstandingAdvances,
            request.OtherCompensation,
            config);

        await repository.AddAsync(settlement, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return (true, settlement.Id, null);
    }
}

public record ApproveEosSettlementCommand(Guid SettlementId, Guid ApproverId) : IRequest<(bool Success, string? Error)>;

public class ApproveEosSettlementHandler(IPayrollRepository repository)
    : IRequestHandler<ApproveEosSettlementCommand, (bool Success, string? Error)>
{
    public async Task<(bool Success, string? Error)> Handle(
        ApproveEosSettlementCommand request,
        CancellationToken cancellationToken)
    {
        var settlement = await repository.EndOfServiceSettlements
            .FirstOrDefaultAsync(s => s.Id == request.SettlementId, cancellationToken);
        if (settlement is null) return (false, "التسوية غير موجودة.");
        if (settlement.Status != EosStatus.Draft) return (false, "لا يمكن اعتماد تسوية ليست مسودة.");
        if (settlement.ApprovedBy.HasValue && settlement.ApprovedBy.Value == request.ApproverId)
            return (false, "تم اعتماد التسوية مسبقاً من نفس المستخدم.");

        settlement.Status = EosStatus.Approved;
        settlement.ApprovedBy = request.ApproverId;
        await repository.SaveChangesAsync(cancellationToken);
        return (true, null);
    }
}

public record MarkEosSettlementPaidCommand(Guid SettlementId, Guid PaidBy) : IRequest<(bool Success, string? Error)>;

public class MarkEosSettlementPaidHandler(IPayrollRepository repository)
    : IRequestHandler<MarkEosSettlementPaidCommand, (bool Success, string? Error)>
{
    public async Task<(bool Success, string? Error)> Handle(
        MarkEosSettlementPaidCommand request,
        CancellationToken cancellationToken)
    {
        var settlement = await repository.EndOfServiceSettlements
            .FirstOrDefaultAsync(s => s.Id == request.SettlementId, cancellationToken);
        if (settlement is null) return (false, "التسوية غير موجودة.");
        if (settlement.Status != EosStatus.Approved) return (false, "لا يمكن تسجيل الصرف لتسوية غير معتمدة.");
        if (settlement.ApprovedBy.HasValue && settlement.ApprovedBy.Value == request.PaidBy)
            return (false, "لا يمكن لنفس المستخدم اعتماد التسوية وتسجيل صرفها.");

        settlement.Status = EosStatus.Paid;
        await repository.SaveChangesAsync(cancellationToken);
        return (true, null);
    }
}
