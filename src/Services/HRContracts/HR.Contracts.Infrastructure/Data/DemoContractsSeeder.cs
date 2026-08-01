using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.Exports;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HR.Contracts.Infrastructure.Data;

/// <summary>
/// Populates the Contracts database with comprehensive demo data for 500 employees.
/// </summary>
public static class DemoContractsSeeder
{
    private static readonly Guid TenantId = TenantDefaults.DefaultTenantId;

    private static readonly (string Code, string NameAr, string NameEn, string Description)[] ExtraContractTypes =
    [
        ("TEMP", "عقد مؤقت", "Temporary", "عقد عمل مؤقت محدد المدة"),
        ("SEASONAL", "عقد موسمي", "Seasonal", "عقد عمل موسمي"),
        ("CONSULT", "عقد استشاري", "Consultancy", "عقد خدمات استشارية"),
        ("TRAINEE", "عقد متدرب", "Trainee", "عقد تدريب وتأهيل"),
    ];

    public static async Task SeedAsync(ContractsDbContext db, CancellationToken ct = default)
    {
        var rng = new Random(42);
        var now = DateTime.UtcNow;

        await EnsureContractTypesAsync(db, now, ct);
        await SeedIntegrationAsync(db, now, ct);

        if (await db.Contracts.AnyAsync(ct))
            return;

        await SeedContractsAndSignaturesAsync(db, rng, now, ct);
        await SeedProbationRecordsAsync(db, now, ct);
        await SeedLeavePoliciesAsync(db, now, ct);
        await SeedLeaveBalancesAsync(db, rng, now, ct);
        await SeedLeaveRequestsAsync(db, rng, now, ct);
        await SeedAdvancesAsync(db, rng, now, ct);
        await SeedSeparationsAsync(db, rng, now, ct);
        await SeedAdministrativeDecisionsAsync(db, rng, now, ct);
        await SeedServiceTransactionsAsync(db, rng, now, ct);
        await SeedCommencementRecordsAsync(db, rng, now, ct);
    }

    private static async Task SeedIntegrationAsync(ContractsDbContext db, DateTime now, CancellationToken ct)
    {
        if (!await db.OrgUnitSnapshots.AnyAsync(ct))
        {
            db.OrgUnitSnapshots.AddRange(DemoIntegrationData.BuildOrgUnitSnapshots());
        }

        if (!await db.AuditLogs.AnyAsync(ct))
        {
            db.AuditLogs.AddRange(DemoIntegrationData.BuildAuditLogs(80, "Contract", now));
        }

        if (!await db.ExportJobs.AnyAsync(ct))
        {
            db.ExportJobs.AddRange(
                DemoIntegrationData.BuildExportJobs(4, "contracts", ExportJobTemplates.ContractsLeaveCsv, now));
        }

        if (!await db.IntegrationMessageReceipts.AnyAsync(ct))
        {
            db.IntegrationMessageReceipts.AddRange(
                DemoIntegrationData.BuildMessageReceipts(40, "Contracts.EmployeeHiredConsumer", now));
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task EnsureContractTypesAsync(ContractsDbContext db, DateTime now, CancellationToken ct)
    {
        // Index 0 = DEFAULT (usually created by ContractsDataSeeder).
        if (!await db.ContractTypes.AnyAsync(t => t.Id == DemoSeedIds.ContractTypeId(0), ct))
        {
            db.ContractTypes.Add(new ContractType
            {
                Id = DemoSeedIds.ContractTypeId(0),
                TenantId = TenantId,
                Code = "DEFAULT",
                NameAr = "عقد افتراضي",
                NameEn = "Default contract",
                Description = "يُنشأ تلقائياً عند تسجيل موظف جديد",
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        for (var i = 0; i < ExtraContractTypes.Length; i++)
        {
            var typeId = DemoSeedIds.ContractTypeId(i + 1);
            if (await db.ContractTypes.AnyAsync(t => t.Id == typeId, ct))
                continue;

            var (code, nameAr, nameEn, description) = ExtraContractTypes[i];
            db.ContractTypes.Add(new ContractType
            {
                Id = typeId,
                TenantId = TenantId,
                Code = code,
                NameAr = nameAr,
                NameEn = nameEn,
                Description = description,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedContractsAndSignaturesAsync(
        ContractsDbContext db,
        Random rng,
        DateTime now,
        CancellationToken ct)
    {
        var signerId = DemoSeedIds.EmployeeId(0);

        for (var i = 0; i < DemoSeedIds.EmployeeCount; i++)
        {
            var empId = DemoSeedIds.EmployeeId(i);
            var contractId = DemoSeedIds.ContractId(i);
            var bucket = DemoSeedIds.StatusBucket(i);
            var isEnded = bucket >= 94; // ~30 per 500

            var yearsAgo = rng.Next(1, 15);
            var startDate = DateOnly.FromDateTime(now.AddYears(-yearsAgo).AddDays(-rng.Next(0, 300)));
            var typeIndex = i % 5;

            ContractStatus status;
            DateOnly? endDate = null;
            if (isEnded)
            {
                status = bucket >= 97 ? ContractStatus.Expired : ContractStatus.Terminated;
                endDate = DateOnly.FromDateTime(now.AddDays(-rng.Next(30, 400)));
                if (endDate < startDate)
                    endDate = startDate.AddMonths(6);
            }
            else
            {
                status = ContractStatus.Active;
                if (typeIndex is 1 or 2)
                    endDate = startDate.AddYears(2);
            }

            db.Contracts.Add(new Contract
            {
                Id = contractId,
                TenantId = TenantId,
                EmployeeId = empId,
                ContractTypeId = DemoSeedIds.ContractTypeId(typeIndex),
                StartDate = startDate,
                EndDate = endDate,
                Status = status,
                ProbationMonths = typeIndex == 4 ? 6 : 3,
                SignedBy = signerId,
                SignedDate = startDate,
                Terms = "عقد عمل وفق نظام العاملين في الدولة",
                CreatedAt = now.AddYears(-yearsAgo),
                UpdatedAt = now,
            });

            db.ContractSignatures.Add(new ContractSignature
            {
                Id = DemoSeedIds.Named($"contract-sig-{i}"),
                TenantId = TenantId,
                ContractId = contractId,
                SignedBy = signerId,
                SignedAt = startDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                SignatureType = i % 3 == 0 ? SignatureType.Physical : SignatureType.Digital,
                SignatureData = $"demo-sig-{i:D4}",
                CreatedAt = now.AddYears(-yearsAgo),
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedProbationRecordsAsync(ContractsDbContext db, DateTime now, CancellationToken ct)
    {
        // First 50 employees = newer hires still on / recently through probation.
        for (var i = 0; i < 50; i++)
        {
            var start = DateOnly.FromDateTime(now.AddMonths(-6 + (i % 5)));
            var end = start.AddMonths(3);
            var result = (i % 10) switch
            {
                9 => ProbationResult.Extended,
                8 => ProbationResult.Failed,
                _ => ProbationResult.Passed
            };

            db.ProbationRecords.Add(new ProbationRecord
            {
                Id = DemoSeedIds.Named($"probation-{i}"),
                TenantId = TenantId,
                ContractId = DemoSeedIds.ContractId(i),
                EmployeeId = DemoSeedIds.EmployeeId(i),
                StartDate = start,
                EndDate = end,
                Result = result,
                DecisionBy = DemoSeedIds.EmployeeId(0),
                DecisionDate = result == ProbationResult.Extended ? null : end,
                Notes = result switch
                {
                    ProbationResult.Passed => "اجتاز فترة الاختبار بنجاح",
                    ProbationResult.Failed => "لم يستوفِ متطلبات الوظيفة",
                    ProbationResult.Extended => "تمديد فترة الاختبار لثلاثة أشهر إضافية",
                    _ => null
                },
                CreatedAt = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedLeavePoliciesAsync(ContractsDbContext db, DateTime now, CancellationToken ct)
    {
        if (await db.LeavePolicies.AnyAsync(ct))
            return;

        var policies = new (LeaveType Type, int MaxDays, bool Medical, bool Paid, int Notice, string Desc)[]
        {
            (LeaveType.Annual, 30, false, true, 7, "إجازة سنوية اعتيادية"),
            (LeaveType.Sick, 15, true, true, 0, "إجازة مرضية بتقرير طبي"),
            (LeaveType.Unpaid, 90, false, false, 14, "إجازة بدون راتب"),
            (LeaveType.Maternity, 120, true, true, 30, "إجازة أمومة"),
            (LeaveType.Other, 10, false, true, 3, "إجازات أخرى"),
        };

        foreach (var (type, maxDays, medical, paid, notice, desc) in policies)
        {
            db.LeavePolicies.Add(new LeavePolicy
            {
                Id = DemoSeedIds.Named($"leave-policy-{(int)type}"),
                TenantId = TenantId,
                LeaveType = type,
                MaxDaysPerYear = maxDays,
                RequiresMedicalCertificate = medical,
                IsPaid = paid,
                MinNoticeDays = notice,
                Description = desc,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedLeaveBalancesAsync(
        ContractsDbContext db,
        Random rng,
        DateTime now,
        CancellationToken ct)
    {
        var year = now.Year;

        for (var i = 0; i < DemoSeedIds.EmployeeCount; i++)
        {
            if (!DemoSeedIds.IsActiveEmployee(i))
                continue;

            var used = rng.Next(0, 16);
            db.LeaveBalances.Add(new LeaveBalance
            {
                Id = DemoSeedIds.Named($"leave-bal-{i}-{year}"),
                TenantId = TenantId,
                EmployeeId = DemoSeedIds.EmployeeId(i),
                LeaveType = LeaveType.Annual,
                Year = year,
                TotalEntitlement = 30,
                Used = used,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedLeaveRequestsAsync(
        ContractsDbContext db,
        Random rng,
        DateTime now,
        CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(now);
        var leaveTypes = Enum.GetValues<LeaveType>();
        var statuses = new[]
        {
            ApprovalStatus.Approved,
            ApprovalStatus.Approved,
            ApprovalStatus.Pending,
            ApprovalStatus.Rejected,
            ApprovalStatus.Executed,
            ApprovalStatus.Cancelled,
        };

        for (var n = 0; n < 200; n++)
        {
            var empIndex = n % DemoSeedIds.EmployeeCount;
            if (!DemoSeedIds.IsActiveEmployee(empIndex))
                empIndex = (empIndex + 1) % DemoSeedIds.EmployeeCount;
            while (!DemoSeedIds.IsActiveEmployee(empIndex))
                empIndex = (empIndex + 1) % DemoSeedIds.EmployeeCount;

            var daysAgo = rng.Next(5, 180);
            var duration = rng.Next(1, 15);
            var startDate = today.AddDays(-daysAgo);
            var leaveType = leaveTypes[n % leaveTypes.Length];
            var status = statuses[n % statuses.Length];
            var orgIndex = 1 + (empIndex % 8);

            db.LeaveRequests.Add(new LeaveRequest
            {
                Id = DemoSeedIds.Named($"leave-req-{n}"),
                TenantId = TenantId,
                EmployeeId = DemoSeedIds.EmployeeId(empIndex),
                OrgUnitId = DemoSeedIds.OrgUnitId(orgIndex),
                LeaveType = leaveType,
                StartDate = startDate,
                EndDate = startDate.AddDays(duration),
                DurationDays = duration,
                Status = status,
                ApprovedBy = status is ApprovalStatus.Approved or ApprovalStatus.Executed
                    ? DemoSeedIds.EmployeeId(0)
                    : null,
                Notes = leaveType switch
                {
                    LeaveType.Annual => "إجازة سنوية اعتيادية",
                    LeaveType.Sick => "إجازة مرضية بتقرير طبي",
                    LeaveType.Unpaid => "إجازة بدون راتب لظروف شخصية",
                    LeaveType.Maternity => "إجازة أمومة",
                    LeaveType.Other => "إجازة خاصة",
                    _ => null
                },
                CreatedAt = now.AddDays(-daysAgo),
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedAdvancesAsync(
        ContractsDbContext db,
        Random rng,
        DateTime now,
        CancellationToken ct)
    {
        var activeIndices = Enumerable.Range(0, DemoSeedIds.EmployeeCount)
            .Where(DemoSeedIds.IsActiveEmployee)
            .ToList();

        for (var n = 0; n < 30; n++)
        {
            var empIndex = activeIndices[n % activeIndices.Count];
            var amount = 100_000m + rng.Next(1, 20) * 50_000m;
            var installments = rng.Next(3, 13);
            var status = (n % 5) switch
            {
                0 => ApprovalStatus.Pending,
                1 => ApprovalStatus.Rejected,
                _ => ApprovalStatus.Approved
            };

            var advanceId = DemoSeedIds.Named($"advance-{n}");
            db.AdvanceRequests.Add(new AdvanceRequest
            {
                Id = advanceId,
                TenantId = TenantId,
                EmployeeId = DemoSeedIds.EmployeeId(empIndex),
                OrgUnitId = DemoSeedIds.OrgUnitId(1 + empIndex % 8),
                Amount = amount,
                Reason = "سلفة على الراتب لظروف طارئة",
                Status = status,
                ApprovedBy = status == ApprovalStatus.Approved ? DemoSeedIds.EmployeeId(0) : null,
                CreatedAt = now.AddDays(-rng.Next(10, 120)),
                UpdatedAt = now,
            });

            if (status == ApprovalStatus.Approved)
            {
                var monthly = Math.Round(amount / installments, 2);
                var paidInstallments = rng.Next(0, installments / 2 + 1);
                db.AdvanceInstallmentPlans.Add(new AdvanceInstallmentPlan
                {
                    Id = DemoSeedIds.Named($"advance-plan-{n}"),
                    TenantId = TenantId,
                    AdvanceRequestId = advanceId,
                    TotalAmount = amount,
                    MonthlyInstallment = monthly,
                    NumberOfInstallments = installments,
                    StartMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc)
                        .AddMonths(-paidInstallments),
                    RemainingAmount = amount - monthly * paidInstallments,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedSeparationsAsync(
        ContractsDbContext db,
        Random rng,
        DateTime now,
        CancellationToken ct)
    {
        var terminatedIndices = Enumerable.Range(0, DemoSeedIds.EmployeeCount)
            .Where(i => DemoSeedIds.StatusBucket(i) >= 95)
            .Take(25)
            .ToList();

        var sepTypes = Enum.GetValues<SeparationType>();
        var today = DateOnly.FromDateTime(now);

        foreach (var (empIndex, n) in terminatedIndices.Select((idx, n) => (idx, n)))
        {
            var sepId = DemoSeedIds.Named($"separation-{n}");
            var effective = today.AddDays(-rng.Next(30, 365));
            var sepType = sepTypes[n % sepTypes.Length];

            db.SeparationRecords.Add(new SeparationRecord
            {
                Id = sepId,
                TenantId = TenantId,
                EmployeeId = DemoSeedIds.EmployeeId(empIndex),
                ContractId = DemoSeedIds.ContractId(empIndex),
                SeparationType = sepType,
                EffectiveDate = effective,
                Reason = sepType switch
                {
                    SeparationType.Resignation => "استقالة بناء على طلب الموظف",
                    SeparationType.Termination => "إنهاء خدمة لمصلحة العمل",
                    SeparationType.EndOfContract => "انتهاء مدة العقد",
                    SeparationType.Medical => "عجز صحي مثبت",
                    SeparationType.Judicial => "قرار قضائي",
                    SeparationType.Disciplinary => "فصل تأديبي",
                    _ => "إنهاء خدمة"
                },
                ApprovedBy = DemoSeedIds.EmployeeId(0),
                SeparationEventPublished = n % 3 != 0,
                CreatedAt = effective.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                UpdatedAt = now,
            });

            foreach (var step in SeparationChecklistDefaults.DefaultSteps)
            {
                var (owner, sla) = SeparationChecklistDefaults.GovernanceFor(step);
                var completed = step <= SeparationStep.PayrollSettled || n % 2 == 0;
                db.SeparationChecklists.Add(new SeparationChecklist
                {
                    Id = DemoSeedIds.Named($"sep-check-{n}-{(int)step}"),
                    TenantId = TenantId,
                    SeparationRecordId = sepId,
                    Step = step,
                    IsCompleted = completed,
                    CompletedAt = completed ? now.AddDays(-rng.Next(1, 20)) : null,
                    CompletedBy = completed ? DemoSeedIds.EmployeeId(0) : null,
                    IsRequired = true,
                    SourceSystem = "HRContracts",
                    OwnerRole = owner,
                    SlaHours = sla,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedAdministrativeDecisionsAsync(
        ContractsDbContext db,
        Random rng,
        DateTime now,
        CancellationToken ct)
    {
        var subjects = new[]
        {
            "منح مكافأة تشجيعية",
            "تكليف بمهمة رسمية",
            "تعديل الوضع الوظيفي",
            "صرف تعويض عائلي",
            "إقرار عقوبة مسلكية",
            "ترفيع درجة",
            "نقل داخلي",
            "تمديد عقد",
        };

        for (var n = 0; n < 50; n++)
        {
            var empIndex = n % DemoSeedIds.EmployeeCount;
            var issued = DateOnly.FromDateTime(now.AddDays(-rng.Next(10, 400)));

            db.AdministrativeDecisions.Add(new AdministrativeDecision
            {
                Id = DemoSeedIds.Named($"admin-decision-{n}"),
                TenantId = TenantId,
                Subject = subjects[n % subjects.Length],
                DecisionNumber = $"AD-2026-{(n + 1):D4}",
                IssuedBy = "مديرية الشؤون الإدارية",
                IssuedDate = issued,
                RelatedEmployeeId = DemoSeedIds.EmployeeId(empIndex),
                CreatedAt = issued.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedServiceTransactionsAsync(
        ContractsDbContext db,
        Random rng,
        DateTime now,
        CancellationToken ct)
    {
        var types = Enum.GetValues<ServiceTransactionType>();
        var actorId = DemoSeedIds.EmployeeId(0);

        for (var n = 0; n < 40; n++)
        {
            var empIndex = (n * 7) % DemoSeedIds.EmployeeCount;
            var txType = types[n % types.Length];
            var txId = DemoSeedIds.Named($"svc-tx-{n}");

            var tx = new ServiceTransaction
            {
                Id = txId,
                TenantId = TenantId,
                EmployeeId = DemoSeedIds.EmployeeId(empIndex),
                TransactionType = txType,
                ReferenceNumber = $"ST-2026-{(n + 1):D4}",
                Notes = txType switch
                {
                    ServiceTransactionType.Appointment => "معاملة تعيين",
                    ServiceTransactionType.Confirmation => "تثبيت بعد فترة الاختبار",
                    ServiceTransactionType.LeaveAdvance => "معاملة إجازة / سلفة",
                    ServiceTransactionType.JobUpdate => "تعديل بيانات وظيفية",
                    ServiceTransactionType.Separation => "معاملة إنهاء خدمة",
                    _ => null
                },
                CreatedAt = now.AddDays(-rng.Next(5, 90)),
                UpdatedAt = now,
            };

            AdvanceTransaction(tx, TargetStateFor(n), actorId);

            foreach (var h in tx.StateHistory)
            {
                h.TenantId = TenantId;
                h.CreatedAt = tx.CreatedAt;
                h.UpdatedAt = now;
            }

            db.ServiceTransactions.Add(tx);

            if (txType == ServiceTransactionType.Appointment)
            {
                foreach (var step in HireChecklistDefaults.DefaultSteps)
                {
                    var (owner, sla, _) = HireChecklistDefaults.GovernanceFor(step);
                    var completed = step <= HireStep.PayrollLinked || n % 3 == 0;
                    db.HireChecklists.Add(new HireChecklist
                    {
                        Id = DemoSeedIds.Named($"hire-check-{n}-{(int)step}"),
                        TenantId = TenantId,
                        ServiceTransactionId = txId,
                        Step = step,
                        IsCompleted = completed,
                        CompletedAt = completed ? now.AddDays(-rng.Next(1, 15)) : null,
                        CompletedBy = completed ? actorId : null,
                        OwnerRole = owner,
                        SlaHours = sla,
                        Notes = completed ? HireChecklistDefaults.StepLabel(step) : null,
                        CreatedAt = now,
                        UpdatedAt = now,
                    });
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static ServiceTransactionState TargetStateFor(int n) => (n % 8) switch
    {
        0 => ServiceTransactionState.Draft,
        1 => ServiceTransactionState.Submitted,
        2 => ServiceTransactionState.UnderReview,
        3 => ServiceTransactionState.InProcess,
        4 => ServiceTransactionState.PendingApproval,
        5 => ServiceTransactionState.Approved,
        6 => ServiceTransactionState.Completed,
        _ => ServiceTransactionState.Archived,
    };

    private static void AdvanceTransaction(
        ServiceTransaction tx,
        ServiceTransactionState target,
        Guid? byUserId)
    {
        if (target == ServiceTransactionState.Draft)
            return;

        // Happy-path chain (skips NeedsInfo / Rejected branches).
        ServiceTransactionState[] path =
        [
            ServiceTransactionState.Submitted,
            ServiceTransactionState.UnderReview,
            ServiceTransactionState.InProcess,
            ServiceTransactionState.PendingApproval,
            ServiceTransactionState.Approved,
            ServiceTransactionState.Executing,
            ServiceTransactionState.PostProcessing,
            ServiceTransactionState.Completed,
            ServiceTransactionState.Archived,
        ];

        foreach (var next in path)
        {
            tx.MoveTo(next, byUserId, null);
            if (tx.State == target)
                break;
        }
    }

    private static async Task SeedCommencementRecordsAsync(
        ContractsDbContext db,
        Random rng,
        DateTime now,
        CancellationToken ct)
    {
        for (var i = 0; i < 100; i++)
        {
            var start = DateOnly.FromDateTime(now.AddMonths(-rng.Next(1, 36)).AddDays(-rng.Next(0, 20)));
            db.CommencementRecords.Add(new CommencementRecord
            {
                Id = DemoSeedIds.Named($"commencement-{i}"),
                TenantId = TenantId,
                EmployeeId = DemoSeedIds.EmployeeId(i),
                ContractId = DemoSeedIds.ContractId(i),
                StartDate = start,
                NotifiedBy = DemoSeedIds.EmployeeId(0),
                NotificationDate = start.ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromHours(9)), DateTimeKind.Utc),
                CreatedAt = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
