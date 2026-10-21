using HR.Health.Domain.Entities;
using HR.Health.Domain.Enums;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Seeding;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HR.Health.Infrastructure.Data;

/// <summary>
/// Populates the Health database with comprehensive demo clinical, insurance, and safety data.
/// </summary>
public static class DemoHealthSeeder
{
    private static Guid TenantId => AcceptanceSeedExecution.TenantId;

    private static readonly string[] MedicalHistories =
    [
        "لا يوجد تاريخ مرضي يُذكر",
        "ارتفاع ضغط دم مضبوط دوائياً",
        "ربو خفيف موسمي",
        "داء سكري من النمط الثاني",
        "فقر دم بعوز الحديد",
        "حساسية موسمية",
    ];

    private static readonly string?[] Conditions =
    [
        null,
        "ضغط دم مرتفع",
        "سكري مضبوط",
        "آلام ظهر مزمنة",
        "حساسية غبار",
    ];

    private static readonly string?[] Allergies =
    [
        null,
        "بنسلين",
        "فول سوداني",
        "غبار الطلع",
        "لاكتوز",
    ];

    private static readonly string[] AccidentLocations =
    [
        "مستودع الإدارة",
        "موقف السيارات",
        "مختبر الحاسوب",
        "سلم الطابق الثاني",
        "ورشة الصيانة",
        "قاعة الأرشيف",
        "مطبخ الموظفين",
        "مدخل المبنى الرئيسي",
    ];

    private static readonly string[] InspectionLocations =
    [
        "المبنى الرئيسي",
        "مستودع التجهيزات",
        "مركز البيانات",
        "مواقف السيارات",
        "قاعات التدريب",
        "المختبرات",
        "الأرشيف المركزي",
        "وحدة الإطفاء",
        "المطبخ والكافتيريا",
        "غرف المولدات",
        "السطح والطوارئ",
        "مدخل الزوار",
    ];

    private static readonly string[] FirstAidProviders =
    [
        "الهلال الأحمر العربي السوري",
        "مديرية الصحة المهنية",
        "مركز التدريب الطبي الحكومي",
        "جمعية الإسعاف الأولي",
    ];

    private static readonly string[] StatusCodes =
    [
        "FIT",
        "FIT_LIMITED",
        "UNDER_REVIEW",
        "TEMP_UNFIT",
        "FOLLOW_UP",
    ];

    public static async Task SeedAsync(
        HealthDbContext db,
        CancellationToken ct = default)
    {
        if (await db.MedicalRecords.AnyAsync(ct)
            || await db.InsuranceContracts.AnyAsync(ct))
            return;

        var rng = AcceptanceSeedExecution.CreateRandom("health");
        var now = AcceptanceSeedExecution.UtcNow;
        var today = DateOnly.FromDateTime(now);

        var medicalRecords = SeedMedicalRecords(db, rng, now, today);
        await db.SaveChangesAsync(ct);

        SeedCommitteeReportsAndRecommendations(db, medicalRecords, rng, now, today);
        SeedPeriodicChecks(db, rng, now, today);
        await SeedInsuranceContractsAsync(db, now, today, ct);
        SeedInsuranceCards(db, rng, now, today);
        SeedHealthStatusEntries(db, rng, now);
        SeedWorkplaceAccidents(db, medicalRecords, rng, now, today);
        SeedSafetyInspections(db, rng, now, today);
        SeedFirstAidTraining(db, rng, now, today);

        await db.SaveChangesAsync(ct);
    }

    private static List<MedicalRecord> SeedMedicalRecords(
        HealthDbContext db,
        Random rng,
        DateTime now,
        DateOnly today)
    {
        var records = new List<MedicalRecord>(DemoSeedIds.EmployeeCount);

        for (var i = 0; i < DemoSeedIds.EmployeeCount; i++)
        {
            var monthsAgo = rng.Next(1, 24);
            var examDate = today.AddMonths(-monthsAgo);
            var consentAt = now.AddMonths(-monthsAgo);

            var record = new MedicalRecord
            {
                TenantId = TenantId,
                EmployeeId = DemoSeedIds.EmployeeId(i),
                MedicalHistory = MedicalHistories[rng.Next(MedicalHistories.Length)],
                CurrentConditions = Conditions[rng.Next(Conditions.Length)],
                Allergies = Allergies[rng.Next(Allergies.Length)],
                Disabilities = rng.Next(100) < 5 ? "إعاقة حركية خفيفة" : null,
                LastExamDate = examDate,
                ConsentGivenAt = consentAt,
                ConsentPurpose = "فحص طبي دوري سنوي ومتابعة صحية",
                ConsentExpiresAt = consentAt.AddMonths(12),
                CreatedAt = consentAt,
                UpdatedAt = now,
            };

            db.MedicalRecords.Add(record);
            records.Add(record);
        }

        return records;
    }

    private static void SeedCommitteeReportsAndRecommendations(
        HealthDbContext db,
        List<MedicalRecord> medicalRecords,
        Random rng,
        DateTime now,
        DateOnly today)
    {
        var committeeRecommendations = Enum.GetValues<MedicalCommitteeRecommendation>();
        var subset = medicalRecords.OrderBy(_ => rng.Next()).Take(30).ToList();

        for (var i = 0; i < subset.Count; i++)
        {
            var record = subset[i];
            var daysAgo = rng.Next(10, 400);
            var committeeDate = today.AddDays(-daysAgo);
            var recommendation = committeeRecommendations[rng.Next(committeeRecommendations.Length)];
            var created = now.AddDays(-daysAgo);

            var report = new MedicalCommitteeReport
            {
                TenantId = TenantId,
                MedicalRecordId = record.Id,
                CommitteeDate = committeeDate,
                Findings = recommendation switch
                {
                    MedicalCommitteeRecommendation.ReturnToWork => "الموظف لائق صحياً للعودة إلى العمل",
                    MedicalCommitteeRecommendation.LimitedDuties => "يُوصى بمهام محدودة لمدة مؤقتة",
                    MedicalCommitteeRecommendation.Retirement => "عدم اللياقة الصحية للاستمرار في العمل",
                    _ => "يحتاج متابعة طبية إضافية قبل القرار النهائي",
                },
                Recommendation = recommendation,
                IssuedBy = "اللجنة الطبية المركزية",
                RelatedDocuments = $"تقرير-لجنة-{i + 1:D3}",
                CreatedAt = created,
                UpdatedAt = now,
            };

            db.MedicalCommitteeReports.Add(report);

            db.MedicalRecommendations.Add(new MedicalRecommendation
            {
                TenantId = TenantId,
                ReportId = report.Id,
                EmployeeId = record.EmployeeId,
                RecommendationType = MapRecommendationType(recommendation),
                Details = report.Findings,
                IssuedAt = created,
                IssuedBy = report.IssuedBy,
                CreatedAt = created,
                UpdatedAt = now,
            });
        }
    }

    private static MedicalRecommendationType MapRecommendationType(
        MedicalCommitteeRecommendation recommendation) =>
        recommendation switch
        {
            MedicalCommitteeRecommendation.ReturnToWork => MedicalRecommendationType.FitnessForDuty,
            MedicalCommitteeRecommendation.LimitedDuties => MedicalRecommendationType.LimitedDuties,
            MedicalCommitteeRecommendation.Retirement => MedicalRecommendationType.Retirement,
            _ => MedicalRecommendationType.Other,
        };

    private static void SeedPeriodicChecks(
        HealthDbContext db,
        Random rng,
        DateTime now,
        DateOnly today)
    {
        var checkCount = AcceptanceSeedExecution.IsAcceptance
            ? Math.Max(10, (DemoSeedIds.EmployeeCount * 16 + 50) / 100)
            : 150;
        for (var i = 0; i < checkCount; i++)
        {
            var empIndex = i % DemoSeedIds.EmployeeCount;
            var daysAgo = rng.Next(5, 540);
            var checkDate = today.AddDays(-daysAgo);
            var created = now.AddDays(-daysAgo);

            db.PeriodicMedicalChecks.Add(new PeriodicMedicalCheck
            {
                TenantId = TenantId,
                EmployeeId = DemoSeedIds.EmployeeId(empIndex),
                CheckDate = checkDate,
                Summary = rng.Next(100) switch
                {
                    < 70 => "فحص دوري طبيعي، لا ملاحظات",
                    < 90 => "يحتاج متابعة ضغط الدم",
                    _ => "تحويل إلى اللجنة الطبية للمتابعة",
                },
                Vitals = $"BP={110 + rng.Next(0, 40)}/{70 + rng.Next(0, 20)}; HR={60 + rng.Next(0, 40)}; BMI={20 + rng.NextDouble() * 10:F1}",
                NextDue = checkDate.AddYears(1),
                CreatedAt = created,
                UpdatedAt = now,
            });
        }
    }

    private static async Task SeedInsuranceContractsAsync(
        HealthDbContext db,
        DateTime now,
        DateOnly today,
        CancellationToken ct)
    {
        var contract0Id = DemoSeedIds.InsuranceContractId(0);
        if (!await db.InsuranceContracts.AnyAsync(c => c.Id == contract0Id, ct))
        {
            db.InsuranceContracts.Add(new InsuranceContract
            {
                Id = contract0Id,
                TenantId = TenantId,
                Provider = "المؤسسة العامة السورية للتأمين",
                CoverageDetails = "تغطية شاملة للموظف وأفراد الأسرة من الدرجة الأولى",
                StartDate = new DateOnly(today.Year - 1, 1, 1),
                EndDate = new DateOnly(today.Year, 12, 31),
                CreatedAt = now.AddYears(-1),
                UpdatedAt = now,
            });
        }

        var contract1Id = DemoSeedIds.InsuranceContractId(1);
        if (!await db.InsuranceContracts.AnyAsync(c => c.Id == contract1Id, ct))
        {
            db.InsuranceContracts.Add(new InsuranceContract
            {
                Id = contract1Id,
                TenantId = TenantId,
                Provider = "شركة التأمين الصحي التعاوني",
                CoverageDetails = "تغطية أساسية للموظف مع سقف سنوي للعلاج الخارجي",
                StartDate = new DateOnly(today.Year, 1, 1),
                EndDate = new DateOnly(today.Year + 1, 12, 31),
                CreatedAt = now.AddMonths(-6),
                UpdatedAt = now,
            });
        }
    }

    private static void SeedInsuranceCards(
        HealthDbContext db,
        Random rng,
        DateTime now,
        DateOnly today)
    {
        var cardSeq = 0;
        for (var i = 0; i < DemoSeedIds.EmployeeCount; i++)
        {
            if (!DemoSeedIds.IsActiveEmployee(i))
                continue;

            var contractIndex = i % 2;
            var issueDate = today.AddMonths(-rng.Next(1, 18));
            var expiryDate = issueDate.AddYears(2);
            var status = expiryDate < today
                ? InsuranceCardStatus.Expired
                : rng.Next(100) switch
                {
                    < 85 => InsuranceCardStatus.Active,
                    < 92 => InsuranceCardStatus.Suspended,
                    < 97 => InsuranceCardStatus.Draft,
                    _ => InsuranceCardStatus.Revoked,
                };

            var person = DemoNameBank.BuildPerson(i);
            var created = issueDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

            db.InsuranceCards.Add(new InsuranceCard
            {
                TenantId = TenantId,
                EmployeeId = DemoSeedIds.EmployeeId(i),
                MemberNumber = $"INS-{today.Year}-{(++cardSeq):D5}",
                Provider = contractIndex == 0
                    ? "المؤسسة العامة السورية للتأمين"
                    : "شركة التأمين الصحي التعاوني",
                IssueDate = issueDate,
                ExpiryDate = expiryDate,
                FamilyMembers = rng.Next(100) < 40
                    ? $"{person.First} — زوج/ة + {rng.Next(1, 4)} أبناء"
                    : null,
                InsuranceContractId = DemoSeedIds.InsuranceContractId(contractIndex),
                Status = status,
                IsCancelled = status == InsuranceCardStatus.Revoked,
                CancelledAt = status == InsuranceCardStatus.Revoked ? now.AddDays(-rng.Next(1, 60)) : null,
                CancellationReason = status == InsuranceCardStatus.Revoked ? "إلغاء بسبب انتهاء الخدمة أو المخالفة" : null,
                CreatedAt = created,
                UpdatedAt = now,
            });
        }
    }

    private static void SeedHealthStatusEntries(
        HealthDbContext db,
        Random rng,
        DateTime now)
    {
        for (var i = 0; i < 100; i++)
        {
            var empIndex = rng.Next(DemoSeedIds.EmployeeCount);
            var daysAgo = rng.Next(1, 365);
            var entryDate = now.AddDays(-daysAgo);

            db.HealthStatusEntries.Add(new HealthStatusEntry
            {
                TenantId = TenantId,
                EmployeeId = DemoSeedIds.EmployeeId(empIndex),
                EntryDate = entryDate,
                StatusCode = StatusCodes[rng.Next(StatusCodes.Length)],
                Notes = rng.Next(100) switch
                {
                    < 50 => "حالة صحية مستقرة",
                    < 80 => "متابعة بعد الفحص الدوري",
                    _ => "تسجيل مؤقت بانتظار تقرير اللجنة",
                },
                CreatedAt = entryDate,
                UpdatedAt = now,
            });
        }
    }

    private static void SeedWorkplaceAccidents(
        HealthDbContext db,
        List<MedicalRecord> medicalRecords,
        Random rng,
        DateTime now,
        DateOnly today)
    {
        var severities = Enum.GetValues<AccidentSeverity>();

        for (var i = 0; i < 8; i++)
        {
            var empIndex = rng.Next(DemoSeedIds.EmployeeCount);
            var person = DemoNameBank.BuildPerson(empIndex);
            var daysAgo = rng.Next(15, 500);
            var accidentDate = today.AddDays(-daysAgo);
            var severity = severities[rng.Next(severities.Length)];
            var created = now.AddDays(-daysAgo);
            var linkedRecord = medicalRecords.FirstOrDefault(r => r.EmployeeId == DemoSeedIds.EmployeeId(empIndex));

            db.WorkplaceAccidents.Add(new WorkplaceAccident
            {
                TenantId = TenantId,
                EmployeeId = DemoSeedIds.EmployeeId(empIndex),
                EmployeeName = DemoNameBank.FullName(person.First, person.Middle, person.Last),
                AccidentDate = accidentDate,
                Location = AccidentLocations[i % AccidentLocations.Length],
                Description = severity switch
                {
                    AccidentSeverity.Minor => "سقوط خفيف دون إصابات بالغة",
                    AccidentSeverity.Moderate => "إصابة متوسطة استدعت إسعافاً أولياً ومراجعة طبية",
                    AccidentSeverity.Severe => "إصابة بليغة استدعت نقلاً إلى المستشفى",
                    _ => "حادث جسيم قيد التحقيق",
                },
                Severity = severity,
                RelatedSickLeaveDays = severity switch
                {
                    AccidentSeverity.Minor => rng.Next(0, 3),
                    AccidentSeverity.Moderate => rng.Next(3, 15),
                    AccidentSeverity.Severe => rng.Next(15, 60),
                    _ => rng.Next(60, 180),
                },
                InvestigationNotes = "تم فتح محضر تحقيق سلامة مهنية وتوثيق الإجراءات الوقائية.",
                MedicalRecordId = linkedRecord?.Id,
                CreatedAt = created,
                UpdatedAt = now,
            });
        }
    }

    private static void SeedSafetyInspections(
        HealthDbContext db,
        Random rng,
        DateTime now,
        DateOnly today)
    {
        for (var i = 0; i < 12; i++)
        {
            var daysAgo = rng.Next(10, 400);
            var inspectionDate = today.AddDays(-daysAgo);
            var passed = rng.Next(100) < 75;
            var created = now.AddDays(-daysAgo);

            db.SafetyInspections.Add(new SafetyInspection
            {
                TenantId = TenantId,
                Location = InspectionLocations[i % InspectionLocations.Length],
                InspectionDate = inspectionDate,
                Inspector = $"مفتش السلامة {(i % 4) + 1}",
                Passed = passed,
                Findings = passed
                    ? "الموقع مطابق لمعايير السلامة المهنية"
                    : "وجود مخالفات تتعلق بطفايات الحريق ومخارج الطوارئ",
                NextDue = inspectionDate.AddMonths(6),
                CreatedAt = created,
                UpdatedAt = now,
            });
        }
    }

    private static void SeedFirstAidTraining(
        HealthDbContext db,
        Random rng,
        DateTime now,
        DateOnly today)
    {
        var picked = Enumerable.Range(0, DemoSeedIds.EmployeeCount)
            .OrderBy(_ => rng.Next())
            .Take(40)
            .ToList();

        for (var i = 0; i < picked.Count; i++)
        {
            var empIndex = picked[i];
            var person = DemoNameBank.BuildPerson(empIndex);
            var daysAgo = rng.Next(20, 700);
            var trainingDate = today.AddDays(-daysAgo);
            var created = now.AddDays(-daysAgo);

            db.FirstAidTrainingRecords.Add(new FirstAidTrainingRecord
            {
                TenantId = TenantId,
                EmployeeId = DemoSeedIds.EmployeeId(empIndex),
                EmployeeName = DemoNameBank.FullName(person.First, person.Middle, person.Last),
                TrainingDate = trainingDate,
                Provider = FirstAidProviders[rng.Next(FirstAidProviders.Length)],
                CertificateNo = $"FA-{trainingDate.Year}-{(i + 1):D4}",
                ExpiryDate = trainingDate.AddYears(2),
                CreatedAt = created,
                UpdatedAt = now,
            });
        }
    }
}
