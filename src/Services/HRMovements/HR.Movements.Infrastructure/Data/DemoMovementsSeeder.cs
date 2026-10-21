using HR.Movements.Domain.Entities;
using HR.Movements.Domain.Enums;
using HR.Movements.Domain.Services;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Seeding;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HR.Movements.Infrastructure.Data;

/// <summary>Populates demo transfers, secondments, loans, and related movement records.</summary>
public static class DemoMovementsSeeder
{
    private static Guid TenantId => AcceptanceSeedExecution.TenantId;

    private static readonly string[] TransferReasons =
    [
        "نقل بناء على طلب الموظف",
        "نقل لحاجة العمل",
        "طلب نقل لأسباب عائلية",
        "إعادة توزيع الكوادر",
        "نقل لدعم مشروع مؤسسي",
        "مواءمة الهيكل التنظيمي",
        "سد فجوة في الوحدة المقصد",
        "نقل بعد انتهاء مهمة مؤقتة",
    ];

    private static readonly string[] HostOrgs =
    [
        "وزارة المالية",
        "وزارة التخطيط",
        "الهيئة المركزية للرقابة",
        "مديرية التنمية الإدارية",
        "مركز المعلومات الوطني",
        "المحافظة / مديرية الشؤون الإدارية",
    ];

    public static async Task SeedAsync(
        MovementsDbContext db,
        CancellationToken ct = default)
    {
        var previousTenantId = TenantIdFlow.Get();
        TenantIdFlow.Set(TenantId);
        try
        {
            var rng = AcceptanceSeedExecution.CreateRandom("movements");
            var now = AcceptanceSeedExecution.UtcNow;
            var today = DateOnly.FromDateTime(now);

            await SeedIntegrationAsync(db, now, ct);

            if (await db.TransferRequests.AnyAsync(ct))
                return;

            var transfers = await SeedTransferRequestsAsync(db, rng, now, today, ct);
            await SeedApprovalsAndImpactsAsync(db, transfers, rng, now, ct);
            await SeedExecutionRecordsAsync(db, transfers, rng, now, ct);
            await SeedSecondmentsAsync(db, rng, now, today, ct);
            await SeedLoansAsync(db, rng, now, today, ct);
            await SeedDistributionPlansAsync(db, transfers, rng, now, today, ct);
            await SeedNoticesAsync(db, rng, now, today, ct);
            await SeedChecklistsAsync(db, transfers, rng, now, ct);
        }
        finally
        {
            TenantIdFlow.Set(previousTenantId);
        }
    }

    private static async Task SeedIntegrationAsync(MovementsDbContext db, DateTime now, CancellationToken ct)
    {
        if (!await db.OrgUnitSnapshots.AnyAsync(ct))
            db.OrgUnitSnapshots.AddRange(DemoIntegrationData.BuildOrgUnitSnapshots());

        if (!await db.AuditLogs.AnyAsync(ct))
            db.AuditLogs.AddRange(DemoIntegrationData.BuildAuditLogs(60, "TransferRequest", now));

        if (!await db.IntegrationMessageReceipts.AnyAsync(ct))
        {
            db.IntegrationMessageReceipts.AddRange(
                DemoIntegrationData.BuildMessageReceipts(30, "Movements.OrgUnitChangedConsumer", now));
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task<List<TransferRequest>> SeedTransferRequestsAsync(
        MovementsDbContext db,
        Random rng,
        DateTime now,
        DateOnly today,
        CancellationToken ct)
    {
        var statuses = Enumerable.Range(0, AcceptanceSeedExecution.TransferCount)
            .Select(i => (i % 4) switch
            {
                0 or 1 => ApprovalStatus.Approved,
                2 => ApprovalStatus.Pending,
                _ => ApprovalStatus.Rejected
            })
            .ToArray();

        var employeeIndices = Enumerable.Range(0, DemoSeedIds.EmployeeCount)
            .OrderBy(_ => rng.Next())
            .Take(statuses.Length)
            .ToList();

        var transfers = new List<TransferRequest>(statuses.Length);

        for (var i = 0; i < statuses.Length; i++)
        {
            var status = statuses[i];
            var empIndex = employeeIndices[i];
            var fromIdx = rng.Next(DemoSeedIds.OrgUnitCount);
            var toIdx = (fromIdx + 1 + rng.Next(DemoSeedIds.OrgUnitCount - 1)) % DemoSeedIds.OrgUnitCount;
            var daysAgo = rng.Next(10, 180);
            var requestDate = today.AddDays(-daysAgo);
            var approved = status == ApprovalStatus.Approved;

            var transfer = new TransferRequest
            {
                Id = DemoSeedIds.Named($"transfer-request-{i}"),
                TenantId = TenantId,
                EmployeeId = DemoSeedIds.EmployeeId(empIndex),
                FromOrgUnitId = DemoSeedIds.OrgUnitId(fromIdx),
                ToOrgUnitId = DemoSeedIds.OrgUnitId(toIdx),
                RequestedBy = DemoSeedIds.EmployeeId(empIndex),
                RequestDate = requestDate,
                EffectiveDate = requestDate.AddDays(rng.Next(14, 45)),
                Reason = TransferReasons[i % TransferReasons.Length],
                Status = status,
                ApprovedBy = approved || status == ApprovalStatus.Rejected
                    ? DemoSeedIds.EmployeeId(rng.Next(0, 40))
                    : null,
                ApprovedDate = approved || status == ApprovalStatus.Rejected
                    ? requestDate.AddDays(rng.Next(2, 10))
                    : null,
                CreatedAt = now.AddDays(-daysAgo),
                UpdatedAt = now,
            };

            db.TransferRequests.Add(transfer);
            transfers.Add(transfer);
        }

        await db.SaveChangesAsync(ct);
        return transfers;
    }

    private static async Task SeedApprovalsAndImpactsAsync(
        MovementsDbContext db,
        List<TransferRequest> transfers,
        Random rng,
        DateTime now,
        CancellationToken ct)
    {
        var approved = transfers.Where(t => t.Status == ApprovalStatus.Approved).ToList();

        foreach (var transfer in approved)
        {
            var approver = transfer.ApprovedBy ?? DemoSeedIds.EmployeeId(rng.Next(0, 40));
            var approvedAt = transfer.ApprovedDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
                ?? now.AddDays(-rng.Next(5, 60));

            db.TransferApprovals.Add(new TransferApproval
            {
                Id = DemoSeedIds.Named($"transfer-approval-{transfer.Id:N}"),
                TenantId = TenantId,
                TransferRequestId = transfer.Id,
                ApprovedBy = approver,
                ApprovedAt = approvedAt,
                Notes = "تمت الموافقة وفق حاجة العمل",
                CreatedAt = approvedAt,
                UpdatedAt = now,
            });

            var salaryChange = rng.Next(-50, 80) * 1000m;
            var allowanceChange = rng.Next(-20, 40) * 500m;

            db.TransferFinancialImpacts.Add(new TransferFinancialImpact
            {
                Id = DemoSeedIds.Named($"transfer-impact-{transfer.Id:N}"),
                TenantId = TenantId,
                TransferRequestId = transfer.Id,
                EmployeeId = transfer.EmployeeId,
                SalaryChange = salaryChange,
                AllowanceChange = allowanceChange,
                Notes = salaryChange >= 0
                    ? "أثر مالي ضمن سلم الدرجات"
                    : "انخفاض بدلات مرتبط بالوحدة المصدر",
                CalculatedAt = approvedAt.AddHours(2),
                CreatedAt = approvedAt,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedExecutionRecordsAsync(
        MovementsDbContext db,
        List<TransferRequest> transfers,
        Random rng,
        DateTime now,
        CancellationToken ct)
    {
        var approved = transfers
            .Where(t => t.Status == ApprovalStatus.Approved)
            .Take(20)
            .ToList();

        foreach (var transfer in approved)
        {
            var executedAt = (transfer.EffectiveDate.ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromHours(9)), DateTimeKind.Utc));
            if (executedAt > now)
                executedAt = now.AddDays(-rng.Next(1, 20));

            db.MovementExecutionRecords.Add(new MovementExecutionRecord
            {
                Id = DemoSeedIds.Named($"movement-exec-{transfer.Id:N}"),
                TenantId = TenantId,
                TransferRequestId = transfer.Id,
                EmployeeId = transfer.EmployeeId,
                ExecutedAt = executedAt,
                ExecutedBy = DemoSeedIds.EmployeeId(rng.Next(0, 40)),
                Notes = "تم تنفيذ النقل وتحديث الملاك",
                CreatedAt = executedAt,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedSecondmentsAsync(
        MovementsDbContext db,
        Random rng,
        DateTime now,
        DateOnly today,
        CancellationToken ct)
    {
        var statuses = Enumerable.Range(0, AcceptanceSeedExecution.SecondmentCount)
            .Select(i => (i % 5) switch
            {
                0 => SecondmentStatus.Pending,
                1 => SecondmentStatus.Approved,
                2 or 3 => SecondmentStatus.Active,
                _ => SecondmentStatus.Closed
            })
            .ToArray();

        var employeeIndices = Enumerable.Range(0, DemoSeedIds.EmployeeCount)
            .OrderBy(_ => rng.Next())
            .Take(statuses.Length)
            .ToList();

        for (var i = 0; i < statuses.Length; i++)
        {
            var daysAgo = rng.Next(20, 200);
            var start = today.AddDays(-daysAgo);
            var duration = rng.Next(30, 180);

            db.SecondmentRequests.Add(new SecondmentRequest
            {
                Id = DemoSeedIds.Named($"secondment-request-{i}"),
                TenantId = TenantId,
                EmployeeId = DemoSeedIds.EmployeeId(employeeIndices[i]),
                HostOrg = HostOrgs[i % HostOrgs.Length],
                StartDate = start,
                EndDate = start.AddDays(duration),
                Status = statuses[i],
                Terms = "انتداب وفق الأنظمة النافذة مع استمرار الراتب من الجهة الأم",
                RequestedBy = DemoSeedIds.EmployeeId(employeeIndices[i]),
                CreatedAt = now.AddDays(-daysAgo),
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedLoansAsync(
        MovementsDbContext db,
        Random rng,
        DateTime now,
        DateOnly today,
        CancellationToken ct)
    {
        var statuses = Enumerable.Range(0, AcceptanceSeedExecution.LoanCount)
            .Select(i => (i % 4) switch
            {
                0 => LoanAssignmentStatus.Pending,
                1 => LoanAssignmentStatus.Approved,
                2 => LoanAssignmentStatus.Active,
                _ => LoanAssignmentStatus.Closed
            })
            .ToArray();

        var employeeIndices = Enumerable.Range(0, DemoSeedIds.EmployeeCount)
            .OrderBy(_ => rng.Next())
            .Take(statuses.Length)
            .ToList();

        for (var i = 0; i < statuses.Length; i++)
        {
            var lendIdx = rng.Next(DemoSeedIds.OrgUnitCount);
            var borrowIdx = (lendIdx + 1 + rng.Next(DemoSeedIds.OrgUnitCount - 1)) % DemoSeedIds.OrgUnitCount;
            var daysAgo = rng.Next(15, 150);
            var start = today.AddDays(-daysAgo);

            db.LoanAssignmentRequests.Add(new LoanAssignmentRequest
            {
                Id = DemoSeedIds.Named($"loan-assignment-{i}"),
                TenantId = TenantId,
                EmployeeId = DemoSeedIds.EmployeeId(employeeIndices[i]),
                LendingOrgUnitId = DemoSeedIds.OrgUnitId(lendIdx),
                BorrowingOrgUnitId = DemoSeedIds.OrgUnitId(borrowIdx),
                StartDate = start,
                EndDate = start.AddDays(rng.Next(60, 240)),
                Status = statuses[i],
                Terms = "إعارة داخلية لدعم الاحتياج التشغيلي",
                RequestedBy = DemoSeedIds.EmployeeId(rng.Next(0, 40)),
                CreatedAt = now.AddDays(-daysAgo),
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedDistributionPlansAsync(
        MovementsDbContext db,
        List<TransferRequest> transfers,
        Random rng,
        DateTime now,
        DateOnly today,
        CancellationToken ct)
    {
        var statuses = new[]
        {
            DistributionPlanStatus.Draft,
            DistributionPlanStatus.Approved,
            DistributionPlanStatus.Approved,
            DistributionPlanStatus.Active,
            DistributionPlanStatus.Active,
        };

        var approvedTransfers = transfers
            .Where(t => t.Status == ApprovalStatus.Approved)
            .Take(statuses.Length)
            .ToList();

        for (var i = 0; i < statuses.Length; i++)
        {
            var orgIdx = rng.Next(DemoSeedIds.OrgUnitCount);
            var daysOffset = rng.Next(-30, 90);

            db.DistributionPlans.Add(new DistributionPlan
            {
                Id = DemoSeedIds.Named($"distribution-plan-{i}"),
                TenantId = TenantId,
                OrgUnitId = DemoSeedIds.OrgUnitId(orgIdx),
                EffectiveDate = today.AddDays(daysOffset),
                PlannedPositions = $"خطة توزيع — {rng.Next(3, 12)} شاغر / إعادة توزيع",
                CreatedBy = DemoSeedIds.EmployeeId(rng.Next(0, 40)),
                Status = statuses[i],
                TransferRequestId = i < approvedTransfers.Count ? approvedTransfers[i].Id : null,
                CreatedAt = now.AddDays(-rng.Next(5, 60)),
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedNoticesAsync(
        MovementsDbContext db,
        Random rng,
        DateTime now,
        DateOnly today,
        CancellationToken ct)
    {
        for (var i = 0; i < 10; i++)
        {
            var daysAgo = 15 + i * 20;
            var issueDate = today.AddDays(-daysAgo);
            var period = $"{issueDate.Year}-Q{(issueDate.Month - 1) / 3 + 1}";

            db.MovementNotices.Add(new MovementNotice
            {
                Id = DemoSeedIds.Named($"movement-notice-{i}"),
                TenantId = TenantId,
                Title = $"نشرة التنقلات — {period}",
                IssueDate = issueDate,
                Period = period,
                Items = $"بند {i + 1}: نقل / انتداب / إعارة وفق قرارات الإدارة",
                IssuedBy = DemoSeedIds.EmployeeId(rng.Next(0, 40)),
                CreatedAt = now.AddDays(-daysAgo),
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedChecklistsAsync(
        MovementsDbContext db,
        List<TransferRequest> transfers,
        Random rng,
        DateTime now,
        CancellationToken ct)
    {
        // Full checklist for a few approved transfers; partial progress for a couple pending ones.
        var approvedSample = transfers
            .Where(t => t.Status == ApprovalStatus.Approved)
            .Take(5)
            .ToList();

        foreach (var transfer in approvedSample)
        {
            var steps = MovementChecklistDefaults.CreateFor(MovementKind.Transfer, transfer.Id).ToList();
            var completedThrough = rng.Next(4, steps.Count); // most steps done for approved/executed

            for (var s = 0; s < steps.Count; s++)
            {
                var step = steps[s];
                step.Id = DemoSeedIds.Named($"movement-checklist-{transfer.Id:N}-{s}");
                step.TenantId = TenantId;

                if (s <= completedThrough)
                {
                    step.IsCompleted = true;
                    step.CompletedAt = now.AddDays(-(steps.Count - s));
                    step.CompletedBy = DemoSeedIds.EmployeeId(rng.Next(0, 40));
                    step.CompletedByName = "موظف الموارد البشرية";
                }

                step.CreatedAt = transfer.CreatedAt;
                step.UpdatedAt = now;
                db.MovementChecklists.Add(step);
            }
        }

        var pendingSample = transfers
            .Where(t => t.Status == ApprovalStatus.Pending)
            .Take(2)
            .ToList();

        foreach (var transfer in pendingSample)
        {
            var steps = MovementChecklistDefaults.CreateFor(MovementKind.Transfer, transfer.Id)
                .Take(2)
                .ToList();

            for (var s = 0; s < steps.Count; s++)
            {
                var step = steps[s];
                step.Id = DemoSeedIds.Named($"movement-checklist-{transfer.Id:N}-{s}");
                step.TenantId = TenantId;
                step.IsCompleted = s == 0;
                step.CompletedAt = s == 0 ? transfer.CreatedAt.AddHours(1) : null;
                step.CompletedBy = s == 0 ? transfer.RequestedBy : null;
                step.CreatedAt = transfer.CreatedAt;
                step.UpdatedAt = now;
                db.MovementChecklists.Add(step);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
