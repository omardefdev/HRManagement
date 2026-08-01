using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Exports;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Payroll.Domain.Constants;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Infrastructure.Data;

/// <summary>
/// Populates the Payroll database with comprehensive demo salary, run, and settlement data.
/// </summary>
public static class DemoPayrollSeeder
{
    private static readonly Guid TenantId = TenantDefaults.DefaultTenantId;

    private static readonly (string Code, string NameAr, decimal Base, decimal Min, decimal Max)[] GradeDefs =
    [
        ("G1", "الدرجة الأولى (مدير عام)", 1_200_000, 1_000_000, 1_500_000),
        ("G2", "الدرجة الثانية (مدير دائرة)", 900_000, 750_000, 1_100_000),
        ("G3", "الدرجة الثالثة (رئيس شعبة)", 700_000, 600_000, 850_000),
        ("G4", "الدرجة الرابعة (موظف أول)", 550_000, 450_000, 650_000),
        ("G5", "الدرجة الخامسة (موظف)", 400_000, 300_000, 500_000),
        ("G6", "الدرجة السادسة (موظف مبتدئ)", 280_000, 250_000, 350_000),
        ("G7", "الدرجة السابعة (كاتب)", 240_000, 220_000, 280_000),
        ("G8", "الدرجة الثامنة (فني)", 220_000, 200_000, 260_000),
        ("G9", "الدرجة التاسعة (عامل مهني)", 200_000, 180_000, 230_000),
        ("G10", "الدرجة العاشرة (عامل)", 180_000, 160_000, 200_000),
    ];

    private static readonly (string Code, string NameAr, string? NameEn)[] CompensationDefs =
    [
        ("ALL-TRANS", "بدل انتقال", "Transport allowance"),
        ("ALL-MEAL", "بدل وجبة", "Meal allowance"),
        ("ALL-HOUS", "بدل سكن", "Housing allowance"),
        ("ALL-PHONE", "بدل هاتف", "Phone allowance"),
        ("ALL-RESP", "بدل مسؤولية", "Responsibility allowance"),
        ("ALL-RISK", "بدل مخاطر", "Hazard allowance"),
        ("ALL-NAT", "تعويض عائلي", "Family compensation"),
        ("ALL-OVER", "تعويض ساعات إضافية", "Overtime"),
        ("ALL-SPEC", "بدل اختصاص", "Specialty allowance"),
        ("ALL-FIELD", "بدل ميداني", "Field allowance"),
        ("ALL-REP", "بدل تمثيل", "Representation allowance"),
        ("ALL-LANG", "بدل لغة", "Language allowance"),
        ("ALL-TECH", "بدل تقني", "Technical allowance"),
        ("ALL-SHIFT", "بدل وردية", "Shift allowance"),
        ("ALL-OTHER", "بدلات أخرى", "Other allowances"),
    ];

    private static readonly (string Code, string NameAr, bool Statutory, decimal? Pct, decimal? Fixed)[] DeductionDefs =
    [
        ("DED-TAX", "ضريبة الدخل", true, 5m, null),
        ("DED-SOC", "تأمينات اجتماعية", true, 7m, null),
        ("DED-HEALTH", "تأمين صحي", true, 2m, null),
        ("DED-PENS", "اشتراك تقاعد", true, 3m, null),
        ("DED-LOAN", "قسط سلفة", false, null, 25_000m),
        ("DED-ABS", "خصم غياب", false, null, 10_000m),
        ("DED-LATE", "خصم تأخير", false, null, 5_000m),
        ("DED-DISC", "جزاء مسلكي", false, null, 15_000m),
        ("DED-UNION", "اشتراك نقابة", false, null, 2_000m),
        ("DED-OTHER", "خصومات أخرى", false, null, 8_000m),
    ];

    public static async Task SeedAsync(PayrollDbContext db, CancellationToken ct = default)
    {
        var rng = new Random(42);
        var now = DateTime.UtcNow;

        await SeedIntegrationAsync(db, now, ct);

        if (await db.EmployeePayProfiles.AnyAsync(ct) || await db.PayrollRuns.AnyAsync(ct))
            return;

        await EnsureSalaryScaleAndGradesAsync(db, now, ct);
        var gradeSalaries = await SeedPayProfilesAsync(db, rng, now, ct);
        var compensationIds = await SeedCompensationTypesAsync(db, now, ct);
        await SeedAllowanceRecordsAsync(db, compensationIds, rng, now, ct);
        var deductionTypeCodes = await SeedDeductionTypesAsync(db, now, ct);
        await SeedDeductionRecordsAsync(db, deductionTypeCodes, rng, now, ct);
        var runIds = await SeedPayrollRunsAsync(db, gradeSalaries, now, ct);
        await SeedPaymentBatchesAsync(db, runIds, now, ct);
        await SeedAdjustmentsAsync(db, runIds, rng, now, ct);
        await SeedEndOfServiceAsync(db, gradeSalaries, rng, now, ct);
        await SeedArrearsAsync(db, runIds, rng, now, ct);
        await SeedProofOfLifeAsync(db, rng, now, ct);
    }

    private static async Task SeedIntegrationAsync(PayrollDbContext db, DateTime now, CancellationToken ct)
    {
        if (!await db.OrgUnitSnapshots.AnyAsync(ct))
            db.OrgUnitSnapshots.AddRange(DemoIntegrationData.BuildOrgUnitSnapshots());

        if (!await db.AuditLogs.AnyAsync(ct))
            db.AuditLogs.AddRange(DemoIntegrationData.BuildAuditLogs(100, "PayrollRun", now));

        if (!await db.ExportJobs.AnyAsync(ct))
        {
            db.ExportJobs.AddRange(
                DemoIntegrationData.BuildExportJobs(4, "payroll", ExportJobTemplates.PayrollRunCsv, now));
        }

        if (!await db.IntegrationMessageReceipts.AnyAsync(ct))
        {
            db.IntegrationMessageReceipts.AddRange(
                DemoIntegrationData.BuildMessageReceipts(40, "Payroll.ContractChangedConsumer", now));
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task EnsureSalaryScaleAndGradesAsync(PayrollDbContext db, DateTime now, CancellationToken ct)
    {
        var scaleId = DemoSeedIds.SalaryScaleId(0);

        if (!await db.SalaryScales.AnyAsync(s => s.Id == scaleId, ct))
        {
            db.SalaryScales.Add(new SalaryScale
            {
                Id = scaleId,
                TenantId = TenantId,
                Code = "GOV-2026",
                NameAr = "سلم رواتب القطاع العام 2026",
                EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
            });
            await db.SaveChangesAsync(ct);
        }

        for (var i = 0; i < GradeDefs.Length; i++)
        {
            var gradeId = DemoSeedIds.PayGradeId(i);
            if (await db.PayGrades.AnyAsync(g => g.Id == gradeId, ct))
                continue;

            var (code, name, baseSal, min, max) = GradeDefs[i];
            db.PayGrades.Add(new PayGrade
            {
                Id = gradeId,
                TenantId = TenantId,
                SalaryScaleId = scaleId,
                GradeCode = code,
                GradeNameAr = name,
                BaseSalary = baseSal,
                MinSalary = min,
                MaxSalary = max,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task<Dictionary<int, decimal>> SeedPayProfilesAsync(
        PayrollDbContext db,
        Random rng,
        DateTime now,
        CancellationToken ct)
    {
        var salaries = new Dictionary<int, decimal>();

        for (var i = 0; i < DemoSeedIds.EmployeeCount; i++)
        {
            if (!DemoSeedIds.IsActiveEmployee(i))
                continue;

            var gradeIndex = DemoSeedIds.IsSupervisor(i)
                ? i % 3 // G1–G3
                : 3 + (i % 7); // G4–G10
            if (gradeIndex >= GradeDefs.Length)
                gradeIndex = GradeDefs.Length - 1;

            var def = GradeDefs[gradeIndex];
            var span = (int)((def.Max - def.Base) / 1000);
            var salary = def.Base + (span <= 0 ? 0 : rng.Next(0, span + 1) * 1000);
            var yearsAgo = rng.Next(1, 12);
            var orgIndex = i < 10 ? 0 : 1 + (i % 8);

            db.EmployeePayProfiles.Add(new EmployeePayProfile
            {
                Id = DemoSeedIds.Named($"pay-profile-{i}"),
                TenantId = TenantId,
                EmployeeId = DemoSeedIds.EmployeeId(i),
                PayGradeId = DemoSeedIds.PayGradeId(gradeIndex),
                BaseSalary = salary,
                EffectiveFrom = now.AddYears(-yearsAgo),
                IsActive = true,
                OrgUnitId = DemoSeedIds.OrgUnitId(orgIndex),
                CreatedAt = now,
                UpdatedAt = now,
            });

            salaries[i] = salary;
        }

        await db.SaveChangesAsync(ct);
        return salaries;
    }

    private static async Task<Guid[]> SeedCompensationTypesAsync(
        PayrollDbContext db,
        DateTime now,
        CancellationToken ct)
    {
        var ids = new Guid[CompensationDefs.Length];
        for (var i = 0; i < CompensationDefs.Length; i++)
        {
            var id = DemoSeedIds.Named($"comp-type-{i}");
            ids[i] = id;
            var (code, nameAr, nameEn) = CompensationDefs[i];
            db.CompensationTypes.Add(new CompensationType
            {
                Id = id,
                TenantId = TenantId,
                Code = code,
                NameAr = nameAr,
                NameEn = nameEn,
                CalculationFormula = i < 4 ? "fixed" : "percent-of-base",
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
        return ids;
    }

    private static async Task SeedAllowanceRecordsAsync(
        PayrollDbContext db,
        Guid[] compensationIds,
        Random rng,
        DateTime now,
        CancellationToken ct)
    {
        var active = Enumerable.Range(0, DemoSeedIds.EmployeeCount)
            .Where(DemoSeedIds.IsActiveEmployee)
            .ToList();

        // ~800 allowances: ~2 each for first ~400 active employees
        var target = 800;
        var n = 0;
        foreach (var empIndex in active)
        {
            var count = empIndex % 5 == 0 ? 3 : 2;
            for (var a = 0; a < count && n < target; a++, n++)
            {
                var typeId = compensationIds[(empIndex + a) % compensationIds.Length];
                var amount = 10_000m + rng.Next(1, 40) * 2_500m;
                db.AllowanceRecords.Add(new AllowanceRecord
                {
                    Id = DemoSeedIds.Named($"allowance-{n}"),
                    TenantId = TenantId,
                    EmployeeId = DemoSeedIds.EmployeeId(empIndex),
                    AllowanceTypeId = typeId,
                    Amount = amount,
                    EffectiveFrom = now.AddMonths(-rng.Next(1, 18)),
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }

            if (n >= target)
                break;
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task<string[]> SeedDeductionTypesAsync(
        PayrollDbContext db,
        DateTime now,
        CancellationToken ct)
    {
        var codes = new string[DeductionDefs.Length];
        for (var i = 0; i < DeductionDefs.Length; i++)
        {
            var (code, nameAr, statutory, pct, fixedAmt) = DeductionDefs[i];
            codes[i] = code;
            db.DeductionTypes.Add(new DeductionType
            {
                Id = DemoSeedIds.Named($"deduction-type-{i}"),
                TenantId = TenantId,
                Code = code,
                NameAr = nameAr,
                IsStatutory = statutory,
                Percentage = pct,
                FixedAmount = fixedAmt,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
        return codes;
    }

    private static async Task SeedDeductionRecordsAsync(
        PayrollDbContext db,
        string[] deductionTypeCodes,
        Random rng,
        DateTime now,
        CancellationToken ct)
    {
        var active = Enumerable.Range(0, DemoSeedIds.EmployeeCount)
            .Where(DemoSeedIds.IsActiveEmployee)
            .ToList();

        for (var n = 0; n < 500; n++)
        {
            var empIndex = active[n % active.Count];
            var code = deductionTypeCodes[n % deductionTypeCodes.Length];
            var amount = 5_000m + rng.Next(1, 30) * 1_000m;

            db.DeductionRecords.Add(new DeductionRecord
            {
                Id = DemoSeedIds.Named($"deduction-rec-{n}"),
                TenantId = TenantId,
                EmployeeId = DemoSeedIds.EmployeeId(empIndex),
                DeductionType = code,
                Amount = amount,
                Reason = $"خصم تجريبي — {code}",
                CreatedAt = now.AddDays(-rng.Next(1, 90)),
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task<Guid[]> SeedPayrollRunsAsync(
        PayrollDbContext db,
        Dictionary<int, decimal> gradeSalaries,
        DateTime now,
        CancellationToken ct)
    {
        var activeIndices = gradeSalaries.Keys.OrderBy(x => x).ToList();
        var runIds = new Guid[6];

        for (var month = 1; month <= 6; month++)
        {
            var runId = DemoSeedIds.Named($"payroll-run-2026-{month:D2}");
            runIds[month - 1] = runId;
            var periodStart = new DateTime(2026, month, 1, 0, 0, 0, DateTimeKind.Utc);
            var periodEnd = periodStart.AddMonths(1).AddDays(-1);
            var status = month switch
            {
                <= 4 => PayrollRunStatus.Posted,
                5 => PayrollRunStatus.Approved,
                _ => PayrollRunStatus.Calculated
            };

            decimal totalNet = 0;
            var pendingLines = 0;

            db.PayrollRuns.Add(new PayrollRun
            {
                Id = runId,
                TenantId = TenantId,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                RunDate = periodEnd.AddDays(2),
                Status = status,
                RunType = PayrollRunType.Regular,
                ResultsFrozen = status is PayrollRunStatus.Posted or PayrollRunStatus.Approved,
                ValidatedAtCalculate = status != PayrollRunStatus.Draft,
                CreatedAt = periodEnd.AddDays(1),
                UpdatedAt = now,
            });
            await db.SaveChangesAsync(ct);

            foreach (var empIndex in activeIndices)
            {
                var baseSalary = gradeSalaries[empIndex];
                var allowance = Math.Round(baseSalary * 0.15m, 0);
                var deduction = Math.Round(baseSalary * 0.12m, 0);
                var gross = baseSalary + allowance;
                var net = gross - deduction;
                totalNet += net;

                var lineId = DemoSeedIds.Named($"sal-line-{month:D2}-{empIndex}");
                db.SalaryLines.Add(new SalaryLine
                {
                    Id = lineId,
                    TenantId = TenantId,
                    PayrollRunId = runId,
                    EmployeeId = DemoSeedIds.EmployeeId(empIndex),
                    GrossPay = gross,
                    TotalDeductions = deduction,
                    NetPay = net,
                    PayComponents = "Base+Allow-Ded",
                    CreatedAt = periodEnd.AddDays(1),
                    UpdatedAt = now,
                });

                db.SalaryLineComponents.AddRange(
                    new SalaryLineComponent
                    {
                        Id = DemoSeedIds.Named($"slc-{month:D2}-{empIndex}-base"),
                        TenantId = TenantId,
                        SalaryLineId = lineId,
                        ComponentType = SalaryComponentType.BaseSalary,
                        ComponentName = "الراتب الأساسي",
                        Amount = baseSalary,
                        CreatedAt = now,
                        UpdatedAt = now,
                    },
                    new SalaryLineComponent
                    {
                        Id = DemoSeedIds.Named($"slc-{month:D2}-{empIndex}-all"),
                        TenantId = TenantId,
                        SalaryLineId = lineId,
                        ComponentType = SalaryComponentType.Allowance,
                        ComponentName = "بدلات",
                        Amount = allowance,
                        CreatedAt = now,
                        UpdatedAt = now,
                    },
                    new SalaryLineComponent
                    {
                        Id = DemoSeedIds.Named($"slc-{month:D2}-{empIndex}-ded"),
                        TenantId = TenantId,
                        SalaryLineId = lineId,
                        ComponentType = SalaryComponentType.Deduction,
                        ComponentName = "خصومات",
                        Amount = deduction,
                        CreatedAt = now,
                        UpdatedAt = now,
                    });

                pendingLines++;
                if (pendingLines >= 200)
                {
                    await db.SaveChangesAsync(ct);
                    db.ChangeTracker.Clear();
                    pendingLines = 0;
                }
            }

            if (pendingLines > 0)
            {
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
            }

            // Re-attach run to update TotalNetPay after clearing tracker.
            var run = await db.PayrollRuns.FirstAsync(r => r.Id == runId, ct);
            run.TotalNetPay = totalNet;
            run.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }

        return runIds;
    }

    private static async Task SeedPaymentBatchesAsync(
        PayrollDbContext db,
        Guid[] runIds,
        DateTime now,
        CancellationToken ct)
    {
        for (var i = 0; i < runIds.Length; i++)
        {
            var run = await db.PayrollRuns.AsNoTracking().FirstAsync(r => r.Id == runIds[i], ct);
            var processed = run.Status is PayrollRunStatus.Posted or PayrollRunStatus.Approved;

            db.PaymentBatches.Add(new PaymentBatch
            {
                Id = DemoSeedIds.Named($"pay-batch-{i + 1}"),
                TenantId = TenantId,
                PayrollRunId = runIds[i],
                BankTransferReference = $"BT-2026-{(i + 1):D3}",
                TotalAmount = run.TotalNetPay,
                DisbursedAmount = processed ? run.TotalNetPay : 0,
                ProcessedAt = processed ? run.RunDate.AddDays(1) : null,
                Status = processed ? PaymentBatchStatus.Processed : PaymentBatchStatus.Pending,
                BatchKind = PaymentBatchKind.Disbursement,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedAdjustmentsAsync(
        PayrollDbContext db,
        Guid[] runIds,
        Random rng,
        DateTime now,
        CancellationToken ct)
    {
        var active = Enumerable.Range(0, DemoSeedIds.EmployeeCount)
            .Where(DemoSeedIds.IsActiveEmployee)
            .ToList();

        var reasons = new[]
        {
            "تسوية فرق دوام",
            "تصحيح بدل انتقال",
            "استرداد خصم خاطئ",
            "تعويض ساعات إضافية متأخرة",
            "تسوية ترقية",
        };

        for (var n = 0; n < 20; n++)
        {
            var empIndex = active[n % active.Count];
            var amount = (rng.Next(0, 2) == 0 ? 1 : -1) * (5_000m + rng.Next(1, 20) * 1_000m);

            db.PayrollAdjustments.Add(new PayrollAdjustment
            {
                Id = DemoSeedIds.Named($"payroll-adj-{n}"),
                TenantId = TenantId,
                EmployeeId = DemoSeedIds.EmployeeId(empIndex),
                PayrollRunId = runIds[n % runIds.Length],
                Amount = amount,
                AdjustmentReason = reasons[n % reasons.Length],
                CreatedAt = now.AddDays(-rng.Next(5, 60)),
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedEndOfServiceAsync(
        PayrollDbContext db,
        Dictionary<int, decimal> gradeSalaries,
        Random rng,
        DateTime now,
        CancellationToken ct)
    {
        var terminated = Enumerable.Range(0, DemoSeedIds.EmployeeCount)
            .Where(i => DemoSeedIds.StatusBucket(i) >= 95)
            .Take(10)
            .ToList();

        for (var n = 0; n < terminated.Count; n++)
        {
            var empIndex = terminated[n];
            // Fall back to a mid-band salary when the employee was inactive (no pay profile).
            var baseSalary = gradeSalaries.TryGetValue(empIndex, out var s)
                ? s
                : GradeDefs[5].Base;
            var years = 5m + rng.Next(0, 20);
            var leavePayout = Math.Round(baseSalary / 30m * rng.Next(5, 30), 0);
            var gratuity = Math.Round(baseSalary * years * 0.5m, 0);
            var other = rng.Next(0, 5) * 10_000m;
            var deductions = rng.Next(0, 3) * 15_000m;
            var net = leavePayout + gratuity + other - deductions;

            db.EndOfServiceSettlements.Add(new EndOfServiceSettlement
            {
                Id = DemoSeedIds.Named($"eos-{n}"),
                TenantId = TenantId,
                EmployeeId = DemoSeedIds.EmployeeId(empIndex),
                SeparationRecordId = DemoSeedIds.Named($"separation-{n}"),
                ServiceYears = years,
                BaseSalary = baseSalary,
                LeaveBalancePayout = leavePayout,
                GratuityAmount = gratuity,
                OtherCompensation = other,
                TotalDeductions = deductions,
                NetSettlement = net,
                Status = n % 3 == 0 ? EosStatus.Paid : n % 3 == 1 ? EosStatus.Approved : EosStatus.Draft,
                CalculatedAt = now.AddDays(-rng.Next(10, 120)),
                ApprovedBy = n % 3 != 2 ? DemoSeedIds.EmployeeId(0) : null,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedArrearsAsync(
        PayrollDbContext db,
        Guid[] runIds,
        Random rng,
        DateTime now,
        CancellationToken ct)
    {
        var active = Enumerable.Range(0, DemoSeedIds.EmployeeCount)
            .Where(DemoSeedIds.IsActiveEmployee)
            .ToList();

        for (var n = 0; n < 15; n++)
        {
            var empIndex = active[n * 11 % active.Count];
            var original = 50_000m + rng.Next(1, 20) * 10_000m;
            var paid = n % 3 == 0 ? original : n % 3 == 1 ? original / 2 : 0m;
            var arrears = original - paid;
            var status = arrears == 0
                ? ArrearsLedgerStatus.FullyPaid
                : paid > 0
                    ? ArrearsLedgerStatus.PartiallyPaid
                    : ArrearsLedgerStatus.Pending;

            db.ArrearsLedgerEntries.Add(new ArrearsLedgerEntry
            {
                Id = DemoSeedIds.Named($"arrears-{n}"),
                TenantId = TenantId,
                EmployeeId = DemoSeedIds.EmployeeId(empIndex),
                PayrollRunId = runIds[n % runIds.Length],
                OriginalAmount = original,
                PaidAmount = paid,
                ArrearsAmount = arrears,
                Reason = "تمويل جزئي لدفعة الرواتب",
                CurrencyEra = "SYP-new",
                Status = status,
                CreatedAt = now.AddDays(-rng.Next(5, 90)),
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedProofOfLifeAsync(
        PayrollDbContext db,
        Random rng,
        DateTime now,
        CancellationToken ct)
    {
        // StatusBucket >= 90 → seconded / terminated / retired ≈ 50 employees.
        var indices = Enumerable.Range(0, DemoSeedIds.EmployeeCount)
            .Where(i => DemoSeedIds.StatusBucket(i) >= 90)
            .Take(50)
            .ToList();

        var methods = new[] { "InPerson", "VideoCall", "NotaryAttestation", "EmbassyVisit" };

        for (var n = 0; n < indices.Count; n++)
        {
            var empIndex = indices[n];
            var verified = now.AddDays(-rng.Next(30, 400));
            var expiry = verified.AddYears(1);
            var status = expiry < now
                ? ProofOfLifeStatus.Expired
                : n % 11 == 0
                    ? ProofOfLifeStatus.Exception
                    : ProofOfLifeStatus.Valid;

            db.ProofOfLifeRecords.Add(new ProofOfLifeRecord
            {
                Id = DemoSeedIds.Named($"proof-of-life-{n}"),
                TenantId = TenantId,
                EmployeeId = DemoSeedIds.EmployeeId(empIndex),
                VerificationDate = verified,
                ExpiryDate = expiry,
                Method = methods[n % methods.Length],
                Status = status,
                ExceptionReason = status == ProofOfLifeStatus.Exception
                    ? "تعذر الحضور — استثناء مؤقت"
                    : null,
                VerifiedBy = DemoSeedIds.EmployeeId(0),
                CreatedAt = verified,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
