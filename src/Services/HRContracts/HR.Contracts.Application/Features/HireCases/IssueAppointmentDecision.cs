using HR.Contracts.Application.Abstractions;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HRManagement.ServiceDefaults.CodeGeneration;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HR.Contracts.Application.Features.HireCases;

/// <summary>
/// FUN-029 (I3, D-6): issue the appointment decision of a hire — an <see cref="AdministrativeDecision"/> numbered by
/// COD-001 (<c>DEC-APPT-YYYY-#####</c>), linked to the transaction, completing <see cref="HireStep.AppointmentDecisionIssued"/>
/// with the decision number as evidence. Idempotent: a second call returns the existing decision.
/// PLAN-29 follow-up: register in Diwan / sign
/// </summary>
public record IssueAppointmentDecisionCommand(
    Guid ServiceTransactionId,
    string? EmployeeName,
    Guid? ActorUserId,
    string? ActorName) : IRequest<IssueAppointmentDecisionResult>;

public sealed record IssueAppointmentDecisionResult(
    bool Success,
    string? Error,
    AdministrativeDecision? Decision,
    bool AlreadyIssued);

public class IssueAppointmentDecisionHandler(IContractsRepository repository, ICodeGenerationClient codeClient)
    : IRequestHandler<IssueAppointmentDecisionCommand, IssueAppointmentDecisionResult>
{
    public const string DecisionTypeCode = "APPT";

    public async Task<IssueAppointmentDecisionResult> Handle(
        IssueAppointmentDecisionCommand request,
        CancellationToken cancellationToken)
    {
        var transaction = await repository.ServiceTransactions
            .FirstOrDefaultAsync(t => t.Id == request.ServiceTransactionId, cancellationToken);
        if (transaction is null || transaction.TransactionType != ServiceTransactionType.Appointment)
            return new(false, "معاملة التعيين غير موجودة.", null, false);
        if (HireWorkflowRules.IsRejectedOrCancelled(transaction))
            return new(false, "المعاملة مرفوضة أو ملغاة.", null, false);

        if (transaction.AppointmentDecisionId is Guid existingId
            && await repository.AdministrativeDecisions.FirstOrDefaultAsync(d => d.Id == existingId, cancellationToken) is { } existing)
            return new(true, null, existing, AlreadyIssued: true);

        var contract = await repository.Contracts.AsNoTracking()
            .Include(c => c.ContractType)
            .Where(c => c.EmployeeId == transaction.EmployeeId)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var (ok, number, error) = await CodeGenerationGateway.GenerateOrErrorAsync(
            codeClient, CodeGenerationRequests.AdministrativeDecision(DecisionTypeCode), cancellationToken);
        if (!ok || string.IsNullOrWhiteSpace(number))
            return new(false, error ?? "تعذّر توليد رقم القرار.", null, false);

        var decision = new AdministrativeDecision
        {
            Subject = $"قرار تعيين: {Name(request.EmployeeName)} — {PositionLabel(contract)}",
            DecisionNumber = number,
            IssuedBy = string.IsNullOrWhiteSpace(request.ActorName) ? null : request.ActorName.Trim(),
            IssuedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            RelatedEmployeeId = transaction.EmployeeId
        };
        await repository.AddAsync(decision, cancellationToken);
        transaction.AppointmentDecisionId = decision.Id;

        var item = await repository.HireChecklists.FirstOrDefaultAsync(
            c => c.ServiceTransactionId == transaction.Id && c.Step == HireStep.AppointmentDecisionIssued,
            cancellationToken);
        if (item is not null && !item.IsCompleted)
            HireWorkflowRules.MarkStepCompleted(item, "قرار التعيين", decision.DecisionNumber, request.ActorUserId, request.ActorName);

        await repository.SaveChangesAsync(cancellationToken);
        return new(true, null, decision, AlreadyIssued: false);
    }

    private static string Name(string? name) => string.IsNullOrWhiteSpace(name) ? "—" : name.Trim();

    /// <summary>HR.Contracts has no position title; the contract type and grade identify the appointment.</summary>
    private static string PositionLabel(Contract? contract) => contract is null
        ? "—"
        : string.Join(" ", new[] { contract.ContractType?.NameAr, contract.GradeCode is null ? null : $"الدرجة {contract.GradeCode}" }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
}
