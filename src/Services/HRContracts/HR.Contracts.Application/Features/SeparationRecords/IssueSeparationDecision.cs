using HR.Contracts.Application.Abstractions;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HRManagement.ServiceDefaults.CodeGeneration;
using HRManagement.ServiceDefaults.Integration;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HR.Contracts.Application.Features.SeparationRecords;

/// <summary>
/// PLAN-60 (§3, G-SEP-04): the administrative decision that ends a person's service, numbered by COD-001 (<c>DEC-SEP-YYYY-#####</c>),
/// issued for an approved record by a holder of <c>HrContractsApprove</c> who is not its creator, linked to the record, completing its
/// <see cref="SeparationStep.DecisionIssued"/> item (System). Once: a second call gives back the same decision and spends no number.
/// </summary>
public record IssueSeparationDecisionCommand(Guid SeparationRecordId, Guid? IssuerId, string? IssuerName) : IRequest<IssueSeparationDecisionResult>;

public sealed record IssueSeparationDecisionResult(bool Success, string? Error, AdministrativeDecision? Decision, bool AlreadyIssued);

public class IssueSeparationDecisionHandler(
    IContractsRepository repository,
    ICodeGenerationClient codeClient,
    IHrCoreLookupClient hrCoreLookup,
    SeparationClock clock,
    IOptions<SeparationOptions> options,
    ILogger<IssueSeparationDecisionHandler>? logger = null) : IRequestHandler<IssueSeparationDecisionCommand, IssueSeparationDecisionResult>
{
    public const string DecisionTypeCode = "SEP";
    public const string NotApprovedMessage = "يُصدر قرار إنهاء الخدمة لسجل معتمد فقط.";
    public const string IssuerIsCreatorMessage = "لا يُصدر منشئُ سجل الفصل ولا مقدّمُه للاعتماد قرارَه: يصدره صاحب صلاحية آخر.";
    public const string LostRaceMessage = "صدر قرار هذا السجل للتو من مستخدم آخر.";
    public const string NoIssuerMessage = "تعذّر تحديد المستخدم الحالي — لم يُصدر القرار.";

    public async Task<IssueSeparationDecisionResult> Handle(IssueSeparationDecisionCommand request, CancellationToken cancellationToken)
    {
        var record = await repository.GetSeparationRecordAsync(request.SeparationRecordId, cancellationToken);
        if (record is null)
            return new(false, "سجل الفصل غير موجود.", null, false);

        // Once: the decision already linked is given back, whatever the status now, and no number is spent.
        if (record.DecisionId is Guid existingId
            && await repository.AdministrativeDecisions.FirstOrDefaultAsync(d => d.Id == existingId, cancellationToken) is { } existing)
            return new(true, null, existing, AlreadyIssued: true);

        if (record.Status != SeparationStatus.Approved)
            return new(false, NotApprovedMessage, null, false);
        if (request.IssuerId is not Guid issuer || issuer == Guid.Empty)
            return new(false, NoIssuerMessage, null, false);
        // The issuer rule excludes the creator and the submitter alike (an inbound record has no creator: its submitter stands in).
        if (options.Value.Decision.IssuerMustDifferFromCreator && (record.CreatedBy == issuer || record.SubmittedBy == issuer))
            return new(false, IssuerIsCreatorMessage, null, false);

        var (ok, number, error) = await CodeGenerationGateway.GenerateOrErrorAsync(
            codeClient, CodeGenerationRequests.AdministrativeDecision(DecisionTypeCode), cancellationToken);
        if (!ok || string.IsNullOrWhiteSpace(number))
            return new(false, error ?? "تعذّر توليد رقم القرار.", null, false);

        var decision = new AdministrativeDecision
        {
            Subject = $"قرار إنهاء خدمة: {await EmployeeNameAsync(record.EmployeeId, cancellationToken)} — "
                      + $"{SeparationApprovalService.TypeLabel(record.SeparationType)} — نافذ {record.EffectiveDate:yyyy-MM-dd}",
            DecisionNumber = number,
            IssuedBy = string.IsNullOrWhiteSpace(request.IssuerName) ? null : request.IssuerName.Trim(),
            // Damascus's date, not UTC's.
            IssuedDate = clock.Today,
            RelatedEmployeeId = record.EmployeeId
        };
        await repository.AddAsync(decision, cancellationToken);
        record.DecisionId = decision.Id;

        var item = await repository.SeparationChecklists.FirstOrDefaultAsync(
            c => c.SeparationRecordId == record.Id && c.Step == SeparationStep.DecisionIssued, cancellationToken);
        if (item is not null && !item.IsCompleted)
        {
            item.IsCompleted = true;
            item.CompletedAt = clock.UtcNow;
            item.CompletedBy = issuer;
            item.CompletedByName = decision.IssuedBy;
            item.EvidenceRef = decision.DecisionNumber;
            item.SourceSystem = "HRContracts";
        }

        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Two issuances at once: the record's xmin lets one through. The other answers with the winner's decision instead of a
            // 500; the number it took stays unused (logged, so the register can note the gap).
            logger?.LogWarning("Separation {RecordId}: a concurrent issuance won; decision number {Number} was generated and not used.",
                record.Id, number);
            repository.Remove(decision);
            await repository.ReloadAsync(record, cancellationToken);
            if (item is not null)
                await repository.ReloadAsync(item, cancellationToken);
            if (record.DecisionId is Guid winnerId
                && await repository.AdministrativeDecisions.FirstOrDefaultAsync(d => d.Id == winnerId, cancellationToken) is { } winner)
                return new(true, null, winner, AlreadyIssued: true);
            return new(false, LostRaceMessage, null, false);
        }

        return new(true, null, decision, AlreadyIssued: false);
    }

    private async Task<string> EmployeeNameAsync(Guid employeeId, CancellationToken ct)
    {
        try
        {
            var found = await hrCoreLookup.GetEmployeesByIdsAsync([employeeId], ct);
            return found.FirstOrDefault()?.Label is { Length: > 0 } label ? label : "—";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return "—";
        }
    }
}
