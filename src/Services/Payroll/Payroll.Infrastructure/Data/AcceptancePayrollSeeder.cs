using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Seeding;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Services;
using Payroll.Domain.Constants;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;

namespace Payroll.Infrastructure.Data;

/// <summary>Acceptance v2 payroll data, sourced exclusively from the shared employee catalog.</summary>
public static class AcceptancePayrollSeeder
{
    public static async Task SeedAsync(PayrollDbContext db, CancellationToken ct = default)
    {
        await AcceptanceSeedReceiptStore.EnsureTableAsync(db, ct);
        foreach (var agency in AcceptanceSeedCatalog.Agencies)
        {
            if (!await AcceptanceSeedReceiptStore.ShouldApplyAsync(db, agency.Id, nameof(PayrollDbContext), ct))
                continue;
            using var frame = AcceptanceSeedExecution.Push(agency);
            await SeedAgencyAsync(db, agency, ct);
            db.ChangeTracker.Clear();
        }
    }

    private static async Task SeedAgencyAsync(
        PayrollDbContext db,
        AcceptanceAgency agency,
        CancellationToken ct)
    {
        var existingProfiles = await db.EmployeePayProfiles.CountAsync(ct);
        if (existingProfiles > 0)
        {
            if (existingProfiles != agency.PayableEmployeeCount)
            {
                throw new InvalidOperationException(
                    $"Payroll seed for {agency.Code} contains {existingProfiles} profiles; " +
                    $"Acceptance v2 expects {agency.PayableEmployeeCount}. Reset the acceptance environment.");
            }
            await AcceptanceSeedReceiptStore.WriteAppliedAsync(db, agency, nameof(PayrollDbContext), ct);
            return;
        }

        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        try
        {
            await EnsurePublishedConfigAsync(db, agency, ct);
            await DemoPayrollSeeder.SeedAsync(db, ct);
            await SeedScenarioRunsAsync(db, agency, ct);
            await SeedParallelAndResumableRunsAsync(db, agency, ct);
            await SeedPartialDisbursementAsync(db, agency, ct);
            await SeedFraudFixturesAsync(db, agency, ct);
            await SeedApprovalFixturesAsync(db, agency, ct);
            await AcceptanceSeedReceiptStore.WriteAppliedAsync(db, agency, nameof(PayrollDbContext), ct);

            if (transaction is not null)
                await transaction.CommitAsync(ct);
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(ct);
            throw;
        }
    }

    private static async Task EnsurePublishedConfigAsync(
        PayrollDbContext db,
        AcceptanceAgency agency,
        CancellationToken ct)
    {
        var config = await db.PayrollConfigurations
            .Include(c => c.TaxBrackets)
            .FirstOrDefaultAsync(c => c.Code == "LAW_50_2004", ct);
        if (config is not null)
            return;

        config = PayrollRegimeSeed.CreateLaw50Configuration(isActive: true);
        config.Id = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, "payroll-config-law50-v1");
        config.TenantId = agency.Id;
        config.PublishedAt = AcceptanceSeedCatalog.ReferenceDate.AddYears(-1);
        config.ApprovedAt = AcceptanceSeedCatalog.ReferenceDate.AddYears(-1).AddDays(-2);
        config.CreatedAt = AcceptanceSeedCatalog.ReferenceDate.AddYears(-1);
        config.UpdatedAt = AcceptanceSeedCatalog.ReferenceDate;

        var index = 0;
        foreach (var bracket in config.TaxBrackets)
        {
            bracket.Id = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, $"tax-bracket-{index:D2}");
            bracket.TenantId = agency.Id;
            bracket.PayrollConfigurationId = config.Id;
            bracket.CreatedAt = config.CreatedAt;
            bracket.UpdatedAt = config.UpdatedAt;
            index++;
        }

        config.ContentHash = PayrollConfigurationHashService.ComputeContentHash(config);
        db.PayrollConfigurations.Add(config);
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedScenarioRunsAsync(
        PayrollDbContext db,
        AcceptanceAgency agency,
        CancellationToken ct)
    {
        var statuses = new[]
        {
            PayrollRunStatus.Draft,
            PayrollRunStatus.Calculating,
            PayrollRunStatus.Calculated,
            PayrollRunStatus.UnderReview,
            PayrollRunStatus.Approved,
            PayrollRunStatus.Cancelled,
            PayrollRunStatus.Reversed
        };

        for (var i = 0; i < statuses.Length; i++)
        {
            var status = statuses[i];
            var id = agency.Ordinal == 1 && status == PayrollRunStatus.Calculated
                ? AcceptanceSeedIds.AcceptancePayrollRunId
                : AcceptanceSeedCatalog.DeterministicGuid(agency.Code, $"payroll-scenario-{status}");
            var periodStart = AcceptanceSeedCatalog.ReferenceDate.AddMonths(i);
            db.PayrollRuns.Add(new PayrollRun
            {
                Id = id,
                TenantId = agency.Id,
                PeriodStart = periodStart,
                PeriodEnd = periodStart.AddMonths(1).AddDays(-1),
                RunDate = periodStart.AddDays(1),
                Status = status,
                RunType = i % 2 == 0 ? PayrollRunType.Correction : PayrollRunType.FinalSettlement,
                TotalNetPay = status is PayrollRunStatus.Draft or PayrollRunStatus.Calculating ? 0 : 1_000_000m + i * 50_000m,
                ResultsFrozen = status is PayrollRunStatus.UnderReview
                    or PayrollRunStatus.Approved
                    or PayrollRunStatus.Reversed,
                ValidatedAtCalculate = status is PayrollRunStatus.Calculated
                    or PayrollRunStatus.UnderReview
                    or PayrollRunStatus.Approved
                    or PayrollRunStatus.Reversed,
                CreatedAt = AcceptanceSeedCatalog.ReferenceDate.AddDays(-10 + i),
                UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedParallelAndResumableRunsAsync(
        PayrollDbContext db,
        AcceptanceAgency agency,
        CancellationToken ct)
    {
        var calculatedRunId = GetScenarioRunId(agency, PayrollRunStatus.Calculated);
        var calculatingRunId = GetScenarioRunId(agency, PayrollRunStatus.Calculating);
        var profiles = await db.EmployeePayProfiles.AsNoTracking()
            .OrderBy(profile => profile.EmployeeId)
            .ToListAsync(ct);
        var configuration = await db.PayrollConfigurations.AsNoTracking()
            .OrderByDescending(config => config.VersionNumber)
            .FirstAsync(ct);

        var employeeIds = profiles.Select(profile => profile.EmployeeId).ToHashSet();
        var resumeStart = AcceptanceSeedCatalog.ReferenceDate.AddMonths(1);
        var resumeEnd = resumeStart.AddMonths(1).AddDays(-1);
        var resumeStartDate = DateOnly.FromDateTime(resumeStart);
        var resumeEndDate = DateOnly.FromDateTime(resumeEnd);
        var allowances = await db.AllowanceRecords.AsNoTracking()
            .Where(item => employeeIds.Contains(item.EmployeeId)
                && item.EffectiveFrom <= resumeEnd
                && (item.EffectiveTo == null || item.EffectiveTo >= resumeStart))
            .ToListAsync(ct);
        var deductions = await db.DeductionRecords.AsNoTracking()
            .Where(item => employeeIds.Contains(item.EmployeeId)
                && item.AppliedInRunId == null
                && (item.DueDate == null || item.DueDate <= resumeEndDate))
            .ToListAsync(ct);
        var adjustments = await db.PayrollAdjustments.AsNoTracking()
            .Where(item => employeeIds.Contains(item.EmployeeId)
                && (item.PayrollRunId == null || item.PayrollRunId == calculatingRunId))
            .ToListAsync(ct);
        var absences = await db.AbsenceDeductions.AsNoTracking()
            .Where(item => employeeIds.Contains(item.EmployeeId)
                && item.AppliedInRunId == null
                && item.PeriodStart <= resumeEndDate
                && item.PeriodEnd >= resumeStartDate)
            .ToListAsync(ct);
        var allowancesByEmployee = allowances.GroupBy(item => item.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());
        var deductionsByEmployee = deductions.GroupBy(item => item.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());
        var adjustmentsByEmployee = adjustments.GroupBy(item => item.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());
        var absencesByEmployee = absences.GroupBy(item => item.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());

        var completedJobId = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, "payroll-job-completed");
        var resumableJobId = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, "payroll-job-resumable");
        db.PayrollRunJobs.AddRange(
            new PayrollRunJob
            {
                Id = completedJobId,
                TenantId = agency.Id,
                PayrollRunId = calculatedRunId,
                Phase = PayrollRunJobPhase.Validate,
                Status = PayrollRunJobStatus.Completed,
                Attempt = 1,
                LastCheckpointAt = AcceptanceSeedCatalog.ReferenceDate.AddMinutes(-20),
                PartitionSize = 200,
                ResultsValidated = true,
                TotalEmployeeCount = agency.PayableEmployeeCount,
                ProcessedEmployeeCount = agency.PayableEmployeeCount,
                CalculateStartedAt = AcceptanceSeedCatalog.ReferenceDate.AddHours(-2),
                EstimatedCompletionAt = AcceptanceSeedCatalog.ReferenceDate.AddHours(-1),
                CreatedAt = AcceptanceSeedCatalog.ReferenceDate.AddHours(-2),
                UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
            },
            new PayrollRunJob
            {
                Id = resumableJobId,
                TenantId = agency.Id,
                PayrollRunId = calculatingRunId,
                Phase = PayrollRunJobPhase.Calculate,
                Status = PayrollRunJobStatus.Paused,
                Attempt = 2,
                LeaseOwner = null,
                LeaseUntil = null,
                LastCheckpointAt = AcceptanceSeedCatalog.ReferenceDate.AddMinutes(-5),
                Error = "Acceptance fixture: worker interruption after a durable checkpoint.",
                PartitionSize = 200,
                ResultsValidated = false,
                TotalEmployeeCount = agency.PayableEmployeeCount,
                ProcessedEmployeeCount = Math.Min(400, agency.PayableEmployeeCount),
                CalculateStartedAt = AcceptanceSeedCatalog.ReferenceDate.AddMinutes(-45),
                EstimatedCompletionAt = AcceptanceSeedCatalog.ReferenceDate.AddMinutes(30),
                CreatedAt = AcceptanceSeedCatalog.ReferenceDate.AddHours(-1),
                UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
            });

        var partitionCount = Math.Max(1, (agency.PayableEmployeeCount + 199) / 200);
        for (var i = 0; i < partitionCount; i++)
        {
            var employeeCount = Math.Min(200, agency.PayableEmployeeCount - i * 200);
            var completed = i < 2;
            db.PayrollRunPartitions.Add(new PayrollRunPartition
            {
                Id = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, $"payroll-resume-partition-{i:D2}"),
                TenantId = agency.Id,
                PayrollRunJobId = resumableJobId,
                PartitionIndex = i,
                Status = completed ? PayrollRunPartitionStatus.Completed : PayrollRunPartitionStatus.Pending,
                EmployeeCount = employeeCount,
                ProcessedCount = completed ? employeeCount : 0,
                CursorEmployeeId = completed ? agency.EmployeeAt(Math.Min((i + 1) * 200 - 1, agency.EmployeeCount - 1)).Id : null,
                WorkerId = completed ? $"acceptance-worker-{1 + i % 2}" : null,
                CreatedAt = AcceptanceSeedCatalog.ReferenceDate.AddMinutes(-40 + i),
                UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
            });
        }

        for (var i = 0; i < profiles.Count; i++)
        {
            var profile = profiles[i];
            db.PayrollRunInputSnapshots.Add(new PayrollRunInputSnapshot
            {
                Id = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, $"payroll-input-snapshot-{i:D3}"),
                TenantId = agency.Id,
                PayrollRunJobId = resumableJobId,
                EmployeeId = profile.EmployeeId,
                EmployeePayProfileId = profile.Id,
                ProfileVersion = PayrollEmployeeCalculationUnit.ComputeProfileVersion(profile),
                InputFingerprint = PayrollInputFingerprint.Compute(
                    allowancesByEmployee.GetValueOrDefault(profile.EmployeeId, []),
                    deductionsByEmployee.GetValueOrDefault(profile.EmployeeId, []),
                    adjustmentsByEmployee.GetValueOrDefault(profile.EmployeeId, []),
                    absencesByEmployee.GetValueOrDefault(profile.EmployeeId, [])),
                PayrollConfigurationId = configuration.Id,
                ConfigContentHash = configuration.ContentHash,
                SortOrder = i,
                CreatedAt = AcceptanceSeedCatalog.ReferenceDate.AddMinutes(-40),
                UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedPartialDisbursementAsync(
        PayrollDbContext db,
        AcceptanceAgency agency,
        CancellationToken ct)
    {
        var run = await db.PayrollRuns
            .Where(item => item.RunType == PayrollRunType.Regular && item.Status == PayrollRunStatus.Posted)
            .OrderByDescending(item => item.PeriodStart)
            .FirstAsync(ct);
        var batch = await db.PaymentBatches.FirstAsync(item => item.PayrollRunId == run.Id, ct);
        var ruleSetId = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, "partial-disbursement-rule-set");
        var decisionId = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, "partial-funding-decision");
        var approvedAmount = Math.Round(batch.TotalAmount * 0.62m, 2);

        db.DisbursementPriorityRules.AddRange(
            new DisbursementPriorityRule
            {
                Id = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, "priority-rule-low-income"),
                TenantId = agency.Id,
                RuleSetId = ruleSetId,
                Category = "LowIncome",
                Priority = 1,
                MinimumAmount = 250_000m,
                LegalSource = "Acceptance v2 synthetic treasury directive",
                IsActive = true,
                CreatedAt = AcceptanceSeedCatalog.ReferenceDate.AddDays(-5),
                UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
            },
            new DisbursementPriorityRule
            {
                Id = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, "priority-rule-family"),
                TenantId = agency.Id,
                RuleSetId = ruleSetId,
                Category = "FamilySupport",
                Priority = 2,
                MinimumAmount = 150_000m,
                LegalSource = "Acceptance v2 synthetic treasury directive",
                IsActive = true,
                CreatedAt = AcceptanceSeedCatalog.ReferenceDate.AddDays(-5),
                UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
            },
            new DisbursementPriorityRule
            {
                Id = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, "priority-rule-general"),
                TenantId = agency.Id,
                RuleSetId = ruleSetId,
                Category = "General",
                Priority = 3,
                MinimumAmount = 0,
                LegalSource = "Acceptance v2 synthetic treasury directive",
                IsActive = true,
                CreatedAt = AcceptanceSeedCatalog.ReferenceDate.AddDays(-5),
                UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
            });

        db.FundingDecisions.Add(new FundingDecision
        {
            Id = decisionId,
            TenantId = agency.Id,
            PayrollRunId = run.Id,
            RequestedAmount = batch.TotalAmount,
            ApprovedAmount = approvedAmount,
            FundingRatio = 0.62m,
            PriorityRuleSetId = ruleSetId,
            DecisionDate = AcceptanceSeedCatalog.ReferenceDate.AddDays(-2),
            ApprovedBy = agency.EmployeeAt(1).Id,
            Notes = "قرار تمويل جزئي اصطناعي لاختبار ترتيب الأولويات والمتأخرات.",
            CreatedAt = AcceptanceSeedCatalog.ReferenceDate.AddDays(-2),
            UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
        });
        await db.SaveChangesAsync(ct);

        batch.FundingDecisionId = decisionId;
        batch.IsPartial = true;
        batch.DisbursedAmount = approvedAmount;
        batch.Status = PaymentBatchStatus.Acked;
        batch.SubmittedAt = AcceptanceSeedCatalog.ReferenceDate.AddDays(-1);
        batch.AckedAt = AcceptanceSeedCatalog.ReferenceDate.AddHours(-12);
        batch.ProcessedAt = null;
        batch.SettledAt = null;
        batch.SettlementReference = null;
        batch.UpdatedAt = AcceptanceSeedCatalog.ReferenceDate;

        var lines = await db.SalaryLines.AsNoTracking()
            .Where(line => line.PayrollRunId == run.Id)
            .OrderBy(line => line.EmployeeId)
            .Take(24)
            .ToListAsync(ct);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var status = (i % 5) switch
            {
                0 => PaymentOrderStatus.Settled,
                1 => PaymentOrderStatus.PartiallySettled,
                2 => PaymentOrderStatus.Acknowledged,
                3 => PaymentOrderStatus.Rejected,
                _ => PaymentOrderStatus.Submitted
            };
            var orderId = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, $"payment-order-{i:D3}");
            var iban = $"SY80{agency.Ordinal:D2}{i + 1:D018}";
            db.PaymentOrders.Add(new PaymentOrder
            {
                Id = orderId,
                TenantId = agency.Id,
                IdempotencyKey = $"{AcceptanceSeedCatalog.PackId}:{agency.Code}:payment:{i:D3}",
                PayrollRunId = run.Id,
                PaymentBatchId = batch.Id,
                SalaryLineId = line.Id,
                EmployeeId = line.EmployeeId,
                Amount = line.NetPay,
                CurrencyCode = "SYP",
                Channel = "acceptance-treasury",
                BeneficiaryIban = iban,
                Status = status,
                ExternalReference = $"ACC-{agency.Code}-PAY-{i + 1:D4}",
                SettlementReference = status is PaymentOrderStatus.Settled or PaymentOrderStatus.PartiallySettled
                    ? $"ACC-{agency.Code}-SET-{i + 1:D4}"
                    : null,
                RejectionReason = status == PaymentOrderStatus.Rejected ? "Synthetic closed beneficiary account" : null,
                SubmittedAt = AcceptanceSeedCatalog.ReferenceDate.AddDays(-1),
                AcknowledgedAt = status is PaymentOrderStatus.Draft or PaymentOrderStatus.Submitted ? null : AcceptanceSeedCatalog.ReferenceDate.AddHours(-18),
                SettledAt = status is PaymentOrderStatus.Settled or PaymentOrderStatus.PartiallySettled ? AcceptanceSeedCatalog.ReferenceDate.AddHours(-8) : null,
                RejectedAt = status == PaymentOrderStatus.Rejected ? AcceptanceSeedCatalog.ReferenceDate.AddHours(-8) : null,
                CreatedAt = AcceptanceSeedCatalog.ReferenceDate.AddDays(-1),
                UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
            });

            if (status is PaymentOrderStatus.Settled or PaymentOrderStatus.PartiallySettled)
            {
                db.BankStatementLines.Add(new BankStatementLine
                {
                    Id = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, $"bank-statement-{i:D3}"),
                    TenantId = agency.Id,
                    StatementReference = $"ACC-{agency.Code}-STM-{i + 1:D4}",
                    BankReference = $"ACC-{agency.Code}-BNK-{i + 1:D4}",
                    Amount = status == PaymentOrderStatus.PartiallySettled ? Math.Round(line.NetPay / 2m, 2) : line.NetPay,
                    BeneficiaryIban = iban,
                    ValueDate = AcceptanceSeedCatalog.ReferenceDate.AddHours(-8),
                    MatchedPaymentOrderId = orderId,
                    MatchedAt = AcceptanceSeedCatalog.ReferenceDate.AddHours(-6),
                    CreatedAt = AcceptanceSeedCatalog.ReferenceDate.AddHours(-8),
                    UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedFraudFixturesAsync(
        PayrollDbContext db,
        AcceptanceAgency agency,
        CancellationToken ct)
    {
        var runId = await db.PayrollRuns.AsNoTracking()
            .Where(item => item.RunType == PayrollRunType.Regular)
            .OrderByDescending(item => item.PeriodStart)
            .Select(item => item.Id)
            .FirstAsync(ct);
        var rules = new[]
        {
            FraudRuleIds.GhostEmployee,
            FraudRuleIds.DuplicateEmployment,
            FraudRuleIds.SharedIban,
            FraudRuleIds.AnomalousCompensation,
            FraudRuleIds.SingleApproverVolume,
            FraudRuleIds.SameDayHireAndPay,
            FraudRuleIds.MissingSourceRecord,
            FraudRuleIds.ProofOfLifeExpiry
        };

        for (var i = 0; i < rules.Length; i++)
        {
            var alertId = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, $"fraud-alert-{i:D2}");
            var holdId = i < 4
                ? AcceptanceSeedCatalog.DeterministicGuid(agency.Code, $"payment-hold-{i:D2}")
                : (Guid?)null;
            var resolved = i is 2 or 3 or 6;
            db.FraudAlerts.Add(new FraudAlert
            {
                Id = alertId,
                TenantId = agency.Id,
                PayrollRunId = runId,
                EmployeeId = agency.EmployeeAt(20 + i).Id,
                RuleId = rules[i],
                Category = i % 2 == 0 ? "Identity" : "Payment",
                Severity = i == 0 ? FraudAlertSeverity.Critical : i < 4 ? FraudAlertSeverity.High : FraudAlertSeverity.Medium,
                Status = resolved ? FraudAlertStatus.Resolved : i % 2 == 0 ? FraudAlertStatus.Open : FraudAlertStatus.UnderReview,
                OwnerId = agency.EmployeeAt(2).Id,
                Details = "Synthetic Acceptance v2 anomaly for fraud-control verification.",
                ResolutionReason = resolved ? "تم التحقق المستقل وإغلاق الحالة الاصطناعية." : null,
                ResolvedBy = resolved ? agency.EmployeeAt(3).Id : null,
                ApprovedBy = resolved ? agency.EmployeeAt(4).Id : null,
                DetectedAtUtc = AcceptanceSeedCatalog.ReferenceDate.AddDays(-8 + i),
                PaymentHoldId = holdId,
                CreatedAt = AcceptanceSeedCatalog.ReferenceDate.AddDays(-8 + i),
                UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
            });

            if (holdId.HasValue)
            {
                var released = i >= 2;
                db.PaymentHolds.Add(new PaymentHold
                {
                    Id = holdId.Value,
                    TenantId = agency.Id,
                    EmployeeId = agency.EmployeeAt(20 + i).Id,
                    PayrollRunId = runId,
                    FraudAlertId = alertId,
                    Reason = "حجز صرف اصطناعي مرتبط بتنبيه احتيال.",
                    HoldType = PaymentHoldType.Fraud,
                    Status = released ? PaymentHoldStatus.Released : PaymentHoldStatus.Active,
                    ReleasedBy = released ? agency.EmployeeAt(3).Id : null,
                    ReleaseApprovedBy = released ? agency.EmployeeAt(4).Id : null,
                    ReleaseReason = released ? "تحقق مستقل ثنائي الأطراف." : null,
                    HeldAtUtc = AcceptanceSeedCatalog.ReferenceDate.AddDays(-7 + i),
                    ReleasedAtUtc = released ? AcceptanceSeedCatalog.ReferenceDate.AddDays(-2) : null,
                    CreatedAt = AcceptanceSeedCatalog.ReferenceDate.AddDays(-7 + i),
                    UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedApprovalFixturesAsync(
        PayrollDbContext db,
        AcceptanceAgency agency,
        CancellationToken ct)
    {
        var approvedRunId = GetScenarioRunId(agency, PayrollRunStatus.Approved);
        var reversedRunId = GetScenarioRunId(agency, PayrollRunStatus.Reversed);
        db.PayrollRunApprovals.AddRange(
            new PayrollRunApproval
            {
                Id = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, "payroll-approval-approved"),
                TenantId = agency.Id,
                PayrollRunId = approvedRunId,
                ApproverId = agency.EmployeeAt(1).Id,
                ApproverRole = "payroll-approver",
                Decision = ApprovalDecision.Approved,
                Notes = "اعتماد مستقل عن منشئ التشغيل ضمن بيانات القبول.",
                DecidedAt = AcceptanceSeedCatalog.ReferenceDate.AddDays(-1),
                CreatedAt = AcceptanceSeedCatalog.ReferenceDate.AddDays(-2),
                UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
            },
            new PayrollRunApproval
            {
                Id = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, "payroll-approval-reversal"),
                TenantId = agency.Id,
                PayrollRunId = reversedRunId,
                ApproverId = agency.EmployeeAt(2).Id,
                ApproverRole = "payroll-reverser",
                Decision = ApprovalDecision.Approved,
                Notes = "اعتماد عكس مستقل لأغراض سيناريو القبول.",
                DecidedAt = AcceptanceSeedCatalog.ReferenceDate,
                CreatedAt = AcceptanceSeedCatalog.ReferenceDate.AddDays(-1),
                UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
            });
        await db.SaveChangesAsync(ct);
    }

    private static Guid GetScenarioRunId(AcceptanceAgency agency, PayrollRunStatus status) =>
        agency.Ordinal == 1 && status == PayrollRunStatus.Calculated
            ? AcceptanceSeedIds.AcceptancePayrollRunId
            : AcceptanceSeedCatalog.DeterministicGuid(agency.Code, $"payroll-scenario-{status}");
}
