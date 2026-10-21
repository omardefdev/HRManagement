using MediatR;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using Payroll.Domain.Services.Engine;
using HRManagement.SharedKernel.SeparationOfDuties;

namespace Payroll.Application.Features.PayrollRuns;

public record CreatePayrollRunCommand(
    DateTime PeriodStart,
    DateTime PeriodEnd,
    DateTime RunDate,
    PayrollRunType RunType = PayrollRunType.Regular,
    Guid? SourceRunId = null) : IRequest<(bool Success, Guid? Id, string? Error)>;

public class CreatePayrollRunHandler(IPayrollRepository repository)
    : IRequestHandler<CreatePayrollRunCommand, (bool Success, Guid? Id, string? Error)>
{
    public async Task<(bool Success, Guid? Id, string? Error)> Handle(
        CreatePayrollRunCommand request,
        CancellationToken cancellationToken)
    {
        var periodErr = PayrollCalculation.ValidatePayrollPeriod(request.PeriodStart, request.PeriodEnd);
        if (periodErr != null) return (false, null, periodErr);

        var coverageErr = PayrollPeriodRules.ValidatePayrollPeriodCoverage(
            DateOnly.FromDateTime(request.PeriodStart),
            DateOnly.FromDateTime(request.PeriodEnd));
        if (coverageErr != null) return (false, null, coverageErr);

        var run = new PayrollRun
        {
            PeriodStart = request.PeriodStart,
            PeriodEnd = request.PeriodEnd,
            RunDate = request.RunDate,
            Status = PayrollRunStatus.Draft,
            RunType = request.RunType,
            SourceRunId = request.SourceRunId,
            TotalNetPay = 0
        };

        await repository.AddAsync(run, cancellationToken);
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException?.Message.Contains("23505", StringComparison.Ordinal) == true
            || ex.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) == true
            || ex.Message.Contains("unique", StringComparison.OrdinalIgnoreCase))
        {
            return (false, null, "يوجد تشغيل رواتب حي لنفس الفترة والنوع مسبقاً.");
        }

        return (true, run.Id, null);
    }
}

public record CalculatePayrollRunCommand(Guid PayrollRunId) : IRequest<(bool Success, int LinesGenerated, string? Error)>;

public class CalculatePayrollRunHandler(IMediator mediator)
    : IRequestHandler<CalculatePayrollRunCommand, (bool Success, int LinesGenerated, string? Error)>
{
    public async Task<(bool Success, int LinesGenerated, string? Error)> Handle(
        CalculatePayrollRunCommand request,
        CancellationToken cancellationToken)
    {
        // PAY-003 / PERF-001: job-based path (snapshot → bounded workers → validate)
        var jobResult = await mediator.Send(new StartPayrollRunJobCommand(request.PayrollRunId), cancellationToken);
        return (jobResult.Success, jobResult.LinesGenerated, jobResult.Error);
    }
}


public record SubmitPayrollRunForReviewCommand(Guid PayrollRunId) : IRequest<(bool Success, string? Error)>;

public class SubmitPayrollRunForReviewHandler(IPayrollRepository repository)
    : IRequestHandler<SubmitPayrollRunForReviewCommand, (bool Success, string? Error)>
{
    public async Task<(bool Success, string? Error)> Handle(
        SubmitPayrollRunForReviewCommand request,
        CancellationToken cancellationToken)
    {
        repository.ClearChangeTracker();
        var run = await repository.GetPayrollRunAsync(request.PayrollRunId, includeLines: true, cancellationToken);
        if (run is null) return (false, "التشغيل غير موجود.");

        try
        {
            run.SubmitForReview();
            await repository.SaveChangesAsync(cancellationToken);
            return (true, null);
        }
        catch (InvalidOperationException ex)
        {
            return (false, ex.Message);
        }
    }
}

public record ApprovePayrollRunCommand(Guid PayrollRunId, Guid ApproverId, string? Role, string? Notes)
    : IRequest<(bool Success, string? Error)>;

public class ApprovePayrollRunHandler(IPayrollRepository repository, SeparationOfDutiesService sod)
    : IRequestHandler<ApprovePayrollRunCommand, (bool Success, string? Error)>
{
    public async Task<(bool Success, string? Error)> Handle(
        ApprovePayrollRunCommand request,
        CancellationToken cancellationToken)
    {
        repository.ClearChangeTracker();
        var run = await repository.GetPayrollRunAsync(request.PayrollRunId, includeLines: false, cancellationToken);
        if (run is null) return (false, "التشغيل غير موجود.");

        if (run.CreatedBy is Guid creatorId && creatorId == request.ApproverId)
            return (false, "لا يمكن لمنشئ التشغيل اعتماد نفس التشغيل.");

        var (allowed, sodError) = PayrollSodGuard.ValidatePayrollRunAction(
            sod,
            SodAction.Check,
            request.ApproverId,
            request.Role,
            run.Id,
            run.CreatedBy,
            null,
            run.TotalNetPay);
        if (!allowed)
            return (false, sodError);

        try
        {
            run.Approve(request.ApproverId, request.Role, request.Notes);
            var approval = run.Approvals.OrderByDescending(a => a.DecidedAt).First();
            repository.MarkAsAdded(approval);
            await repository.SaveChangesAsync(cancellationToken);
            return (true, null);
        }
        catch (InvalidOperationException ex)
        {
            return (false, ex.Message);
        }
    }
}

public record RejectPayrollRunCommand(Guid PayrollRunId, Guid ApproverId, string? Notes)
    : IRequest<(bool Success, string? Error)>;

public class RejectPayrollRunHandler(IPayrollRepository repository)
    : IRequestHandler<RejectPayrollRunCommand, (bool Success, string? Error)>
{
    public async Task<(bool Success, string? Error)> Handle(
        RejectPayrollRunCommand request,
        CancellationToken cancellationToken)
    {
        repository.ClearChangeTracker();
        var run = await repository.GetPayrollRunAsync(request.PayrollRunId, includeLines: false, cancellationToken);
        if (run is null) return (false, "التشغيل غير موجود.");

        try
        {
            run.Reject(request.ApproverId, request.Notes);
            var approval = run.Approvals.OrderByDescending(a => a.DecidedAt).First();
            repository.MarkAsAdded(approval);
            await repository.SaveChangesAsync(cancellationToken);
            return (true, null);
        }
        catch (InvalidOperationException ex)
        {
            return (false, ex.Message);
        }
    }
}

public record PostPayrollRunCommand(Guid PayrollRunId, Guid PosterId) : IRequest<(bool Success, string? Error)>;

public class PostPayrollRunHandler(
    IPayrollRepository repository,
    IPayrollEventPublisher eventPublisher,
    SeparationOfDutiesService sod)
    : IRequestHandler<PostPayrollRunCommand, (bool Success, string? Error)>
{
    public async Task<(bool Success, string? Error)> Handle(
        PostPayrollRunCommand request,
        CancellationToken cancellationToken)
    {
        repository.ClearChangeTracker();
        var run = await repository.GetPayrollRunAsync(request.PayrollRunId, includeLines: true, cancellationToken);
        if (run is null) return (false, "التشغيل غير موجود.");

        var latestApproval = run.Approvals
            .Where(a => a.Decision == ApprovalDecision.Approved)
            .OrderByDescending(a => a.DecidedAt)
            .FirstOrDefault();

        var (allowed, sodError) = PayrollSodGuard.ValidatePayrollRunAction(
            sod,
            SodAction.Post,
            request.PosterId,
            "payroll-poster",
            run.Id,
            run.CreatedBy,
            latestApproval?.ApproverId,
            run.TotalNetPay);
        if (!allowed)
            return (false, sodError);

        var hasClosedAudit = await repository.PayrollAuditRecords
            .AnyAsync(a => a.PayrollRunId == run.Id && a.Status == AuditStatus.Closed, cancellationToken);
        if (!hasClosedAudit)
            return (false, "يجب إغلاق سجل مراجعة الرواتب قبل الترحيل.");

        try
        {
            var postErr = run.ValidateCanPost();
            if (postErr != null) return (false, postErr);

            var job = await repository.PayrollRunJobs
                .Include(j => j.Partitions)
                .Where(j => j.PayrollRunId == run.Id)
                .OrderByDescending(j => j.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (job is not null)
            {
                if (job.Partitions.Any(p => p.Status != PayrollRunPartitionStatus.Completed))
                    return (false, "لا يمكن ترحيل تشغيل بأقسام حساب غير مكتملة.");
                if (!job.ResultsValidated && !run.ValidatedAtCalculate)
                    return (false, "يجب نجاح التحقق قبل الترحيل.");
            }
            else if (!run.ValidatedAtCalculate)
            {
                return (false, "يجب نجاح التحقق قبل الترحيل.");
            }

            foreach (var sl in run.SalaryLines)
                PayrollCalculation.ApplyNetFromGrossAndDeductions(sl);

            var total = PayrollCalculation.SumNetPay(run.SalaryLines);
            run.Post(total);

            // PAY-003: Post creates Pending disbursement; Pay sets ProcessedAt later.
            await repository.AddAsync(new PaymentBatch
            {
                PayrollRunId = run.Id,
                TotalAmount = total,
                Status = PaymentBatchStatus.Pending,
                BatchKind = PaymentBatchKind.Disbursement,
                BankTransferReference = $"PAY-{run.PeriodStart:yyyyMM}-{run.Id:N}"[..32]
            }, cancellationToken);

            if (job is not null)
            {
                job.Phase = PayrollRunJobPhase.Post;
                job.LastCheckpointAt = DateTime.UtcNow;
            }

            await repository.SaveChangesAsync(cancellationToken);

            await eventPublisher.PublishPayrollProcessedAsync(
                run.Id, run.PeriodStart, run.PeriodEnd, run.TotalNetPay, cancellationToken);
            await repository.SaveChangesAsync(cancellationToken);

            return (true, null);
        }
        catch (InvalidOperationException ex)
        {
            return (false, ex.Message);
        }
    }
}

public record ReversePayrollRunCommand(Guid PayrollRunId) : IRequest<(bool Success, string? Error)>;

public class ReversePayrollRunHandler(IPayrollRepository repository)
    : IRequestHandler<ReversePayrollRunCommand, (bool Success, string? Error)>
{
    public async Task<(bool Success, string? Error)> Handle(
        ReversePayrollRunCommand request,
        CancellationToken cancellationToken)
    {
        repository.ClearChangeTracker();
        var run = await repository.GetPayrollRunAsync(request.PayrollRunId, includeLines: true, cancellationToken);
        if (run is null) return (false, "التشغيل غير موجود.");

        try
        {
            var original = await repository.PaymentBatches
                .Where(b => b.PayrollRunId == run.Id
                            && b.BatchKind == PaymentBatchKind.Disbursement
                            && b.Status != PaymentBatchStatus.Reversed)
                .OrderByDescending(b => b.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            run.Reverse();

            // A reversal moves real money back and therefore goes through the same banking cycle
            // as a disbursement. Creating it already "Processed" recorded a refund that no bank
            // had been asked to make.
            var reversal = new PaymentBatch
            {
                PayrollRunId = run.Id,
                TotalAmount = original?.TotalAmount ?? run.TotalNetPay,
                Status = PaymentBatchStatus.Pending,
                BatchKind = PaymentBatchKind.Reversal,
                ReversesBatchId = original?.Id,
                BankTransferReference = $"REV-{run.PeriodStart:yyyyMM}-{run.Id:N}"[..32]
            };
            await repository.AddAsync(reversal, cancellationToken);

            if (original is not null)
                original.Status = PaymentBatchStatus.Reversed;

            // Release applied deductions/absences for compensation path
            var deductions = await repository.DeductionRecords
                .Where(d => d.AppliedInRunId == run.Id)
                .ToListAsync(cancellationToken);
            foreach (var d in deductions) d.AppliedInRunId = null;

            var absences = await repository.AbsenceDeductions
                .Where(a => a.AppliedInRunId == run.Id)
                .ToListAsync(cancellationToken);
            foreach (var a in absences) a.AppliedInRunId = null;

            await repository.AddAsync(new PayrollAuditRecord
            {
                PayrollRunId = run.Id,
                Status = AuditStatus.Closed,
                Findings = "عكس تعويضي PAY-003",
                ClosedAt = DateTime.UtcNow
            }, cancellationToken);

            await repository.SaveChangesAsync(cancellationToken);
            return (true, null);
        }
        catch (InvalidOperationException ex)
        {
            return (false, ex.Message);
        }
    }
}
