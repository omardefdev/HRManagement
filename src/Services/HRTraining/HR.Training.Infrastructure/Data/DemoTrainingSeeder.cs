using System.Text.Json;
using HR.Training.Domain.Entities;
using HR.Training.Domain.Enums;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Seeding;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HR.Training.Infrastructure.Data;

/// <summary>
/// Populates the Training database with comprehensive demo classroom and e-learning data.
/// Idempotent relative to <see cref="ELearningSeeder"/> (runs first; uses distinct course codes).
/// </summary>
public static class DemoTrainingSeeder
{
    private static Guid TenantId => AcceptanceSeedExecution.TenantId;
    private static string ScopedCode(string code) => AcceptanceSeedExecution.CurrentAgency is { } agency
        ? $"{agency.Code}-{code}"
        : code;

    private static readonly (string name, string country)[] Providers =
    [
        ("المعهد الوطني للإدارة العامة", "سوريا"),
        ("مركز التدريب الإداري الحكومي", "سوريا"),
        ("معهد التخطيط للتنمية الاقتصادية والاجتماعية", "سوريا"),
        ("الجامعة الافتراضية السورية", "سوريا"),
        ("مركز تطوير الإدارة المحلية", "سوريا"),
        ("معهد الماليّة العامة", "سوريا"),
        ("أكاديمية القيادة الحكومية", "سوريا"),
        ("مركز التدريب على تقنية المعلومات", "سوريا"),
        ("المعهد العالي للتنمية الإدارية", "سوريا"),
        ("مركز اللغات والترجمة الحكومية", "سوريا"),
    ];

    private static readonly (string code, string titleAr, string? titleEn, int days)[] Courses =
    [
        ("ADM-101", "إدارة الموارد البشرية الحديثة", "Modern HR Management", 5),
        ("ADM-102", "إدارة الأداء الوظيفي", "Performance Management", 3),
        ("ADM-201", "تخطيط القوى العاملة", "Workforce Planning", 4),
        ("IT-201", "أمن المعلومات والشبكات", "Information Security", 3),
        ("IT-202", "التحول الرقمي في القطاع العام", "Digital Transformation", 4),
        ("IT-301", "حوكمة تقنية المعلومات", "IT Governance", 3),
        ("FIN-301", "المحاسبة الحكومية المتقدمة", "Advanced Gov Accounting", 4),
        ("FIN-302", "إعداد الموازنة والرقابة", "Budgeting & Control", 3),
        ("FIN-401", "التدقيق الداخلي الحكومي", "Internal Audit", 5),
        ("LEG-101", "القانون الإداري والتنظيم المؤسسي", "Administrative Law", 3),
        ("LEG-201", "العقود الإدارية والمناقصات", "Public Procurement Law", 4),
        ("LEG-301", "النزاهة ومكافحة الفساد", "Integrity & Anti-Corruption", 2),
        ("MGT-401", "القيادة الإدارية والتخطيط الاستراتيجي", "Strategic Leadership", 5),
        ("MGT-402", "إدارة المشاريع الحكومية", "Public Project Management", 4),
        ("MGT-501", "اتخاذ القرار وحل المشكلات", "Decision Making", 3),
        ("COM-101", "مهارات التواصل والتفاوض", "Communication Skills", 2),
        ("COM-201", "إعداد التقارير الرسمية", "Official Reporting", 2),
        ("COM-301", "العلاقات العامة الحكومية", "Public Relations", 3),
        ("HR-201", "نظام الخدمة العامة", "Civil Service System", 3),
        ("HR-301", "إدارة المسارات الوظيفية", "Career Path Management", 3),
        ("SAFE-101", "السلامة المهنية في بيئة العمل", "Occupational Safety", 2),
        ("SAFE-201", "الإسعافات الأولية للموظفين", "First Aid for Staff", 2),
        ("QA-101", "إدارة الجودة الشاملة", "Total Quality Management", 3),
        ("QA-201", "مؤشرات الأداء المؤسسي", "KPI Management", 3),
        ("ARC-101", "إدارة الوثائق والأرشفة", "Records Management", 2),
        ("ARC-201", "الحفظ الإلكتروني للوثائق", "Electronic Archiving", 3),
        ("ENG-101", "اللغة الإنكليزية الإدارية", "Business English", 5),
        ("ENG-201", "المراسلات الرسمية بالإنكليزية", "Official English Writing", 3),
        ("ETH-101", "أخلاقيات الوظيفة العامة", "Public Service Ethics", 2),
        ("ETH-201", "حماية البيانات الشخصية", "Personal Data Protection", 2),
    ];

    private static readonly (string code, string titleAr, string? titleEn)[] Qualifications =
    [
        ("QC-HR-01", "أخصائي موارد بشرية", "HR Specialist"),
        ("QC-FIN-01", "محاسب حكومي معتمد", "Certified Gov Accountant"),
        ("QC-IT-01", "أخصائي أمن معلومات", "InfoSec Specialist"),
        ("QC-LEG-01", "مستشار قانوني إداري", "Admin Legal Advisor"),
        ("QC-MGT-01", "قائد إداري معتمد", "Certified Admin Leader"),
        ("QC-SAFE-01", "مسؤول سلامة مهنية", "Safety Officer"),
        ("QC-QA-01", "أخصائي جودة مؤسسية", "Quality Specialist"),
        ("QC-ARC-01", "أمين أرشيف معتمد", "Certified Archivist"),
    ];

    private static readonly (string code, string titleAr, string category, int hours)[] ELearningCatalog =
    [
        ("EL-301", "مهارات العمل عن بُعد", "المهارات الرقمية", 8),
        ("EL-302", "مقدمة في الأمن السيبراني للموظفين", "الأمن السيبراني", 6),
        ("EL-303", "إدارة الوقت والإنتاجية", "التطوير الذاتي", 5),
        ("EL-304", "خدمة المواطن والتميز المؤسسي", "الخدمة العامة", 7),
        ("EL-305", "أساسيات التحليل الإحصائي", "البيانات", 10),
    ];

    private static readonly string[] Destinations =
    [
        "دمشق", "حلب", "حمص", "اللاذقية", "بيروت", "عمّان", "القاهرة", "تونس", "الرباط", "إسطنبول",
        "دبي", "الدوحة", "مسقط", "الكويت", "جدة", "باريس", "برلين", "فيينا", "جنيف", "روما",
    ];

    public static async Task SeedAsync(
        TrainingDbContext db,
        CancellationToken ct = default)
    {
        if (await db.TrainingEnrollments.AnyAsync(ct)
            || await db.TrainingCourses.CountAsync(ct) >= DemoSeedIds.TrainingCourseCount)
            return;

            var rng = AcceptanceSeedExecution.CreateRandom("training");
            var now = AcceptanceSeedExecution.UtcNow;
        var today = DateOnly.FromDateTime(now);

        await SeedProvidersAsync(db, now, ct);
        await SeedCoursesAsync(db, now, ct);
        await SeedEnrollmentsAndResultsAsync(db, rng, now, ct);
        await SeedTrainingPlansAsync(db, now, ct);
        await SeedNominationsAsync(db, rng, now, ct);
        await SeedMissionFilesAsync(db, rng, now, today, ct);
        await SeedQualificationCodesAsync(db, now, ct);
        await SeedELearningDemoAsync(db, rng, now, ct);
    }

    private static async Task SeedProvidersAsync(TrainingDbContext db, DateTime now, CancellationToken ct)
    {
        for (var i = 0; i < Providers.Length; i++)
        {
            var (name, country) = Providers[i];
            db.TrainingProviders.Add(new TrainingProvider
            {
                Id = DemoSeedIds.TrainingProviderId(i),
                TenantId = TenantId,
                Name = name,
                Country = country,
                ContactInfo = JsonSerializer.Serialize(new
                {
                    phone = $"011-{2000000 + i * 111}",
                    email = $"training{i + 1}@gov.sy",
                    city = DemoNameBank.Cities[i % DemoNameBank.Cities.Length],
                }),
                CreatedAt = now.AddMonths(-18 + i),
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedCoursesAsync(TrainingDbContext db, DateTime now, CancellationToken ct)
    {
        for (var i = 0; i < Courses.Length; i++)
        {
            var (code, titleAr, titleEn, days) = Courses[i];
            db.TrainingCourses.Add(new TrainingCourse
            {
                Id = DemoSeedIds.TrainingCourseId(i),
                TenantId = TenantId,
                Code = ScopedCode(code),
                TitleAr = titleAr,
                TitleEn = titleEn,
                Description = $"دورة تدريبية حول: {titleAr}",
                DurationDays = days,
                ProviderId = DemoSeedIds.TrainingProviderId(i % Providers.Length),
                CreatedAt = now.AddMonths(-12 + (i % 10)),
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedEnrollmentsAndResultsAsync(
        TrainingDbContext db,
        Random rng,
        DateTime now,
        CancellationToken ct)
    {
        for (var i = 0; i < AcceptanceSeedExecution.TrainingEnrollmentCount; i++)
        {
            var empIndex = i % DemoSeedIds.EmployeeCount;
            var courseIndex = (i * 7 + 3) % DemoSeedIds.TrainingCourseCount;
            var monthsAgo = 1 + (i % 18);
            var enrolledAt = now.AddMonths(-monthsAgo).AddDays(-(i % 20));
            var status = (i % 10) switch
            {
                < 4 => EnrollmentStatus.Completed,
                < 7 => EnrollmentStatus.Attended,
                < 9 => EnrollmentStatus.Enrolled,
                _ => EnrollmentStatus.Cancelled,
            };

            var enrollment = new TrainingEnrollment
            {
                TenantId = TenantId,
                CourseId = DemoSeedIds.TrainingCourseId(courseIndex),
                EmployeeId = DemoSeedIds.EmployeeId(empIndex),
                EnrolledAt = enrolledAt,
                Status = status,
                FinalScore = status is EnrollmentStatus.Completed or EnrollmentStatus.Attended
                    ? 60 + rng.Next(0, 41)
                    : null,
                CertificateNo = status == EnrollmentStatus.Completed
                    ? ScopedCode($"CERT-{enrolledAt.Year}-{(i + 1):D4}")
                    : null,
                CancelledAt = status == EnrollmentStatus.Cancelled ? enrolledAt.AddDays(rng.Next(2, 20)) : null,
                CancellationReason = status == EnrollmentStatus.Cancelled ? "انسحاب الموظف أو تعارض الدوام" : null,
                CreatedAt = enrolledAt,
                UpdatedAt = now,
            };

            db.TrainingEnrollments.Add(enrollment);

            if (status is EnrollmentStatus.Completed or EnrollmentStatus.Attended)
            {
                var score = enrollment.FinalScore ?? 70;
                db.TrainingResults.Add(new TrainingResult
                {
                    TenantId = TenantId,
                    EnrollmentId = enrollment.Id,
                    Score = score,
                    EvaluationNotes = score >= 85
                        ? "أداء متميز واستيعاب كامل للمادة"
                        : score >= 70
                            ? "أداء جيد مع حاجة لبعض التحسين"
                            : "أتم الدورة بنتيجة مقبولة",
                    GradedBy = DemoSeedIds.EmployeeId(rng.Next(0, 40)),
                    GradedAt = enrolledAt.AddDays(Courses[courseIndex].days + rng.Next(1, 10)),
                    CreatedAt = enrolledAt.AddDays(Courses[courseIndex].days),
                    UpdatedAt = now,
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedTrainingPlansAsync(TrainingDbContext db, DateTime now, CancellationToken ct)
    {
        for (var yearOffset = 0; yearOffset < 3; yearOffset++)
        {
            var year = 2024 + yearOffset;
            var courseIds = Enumerable.Range(yearOffset * 8, 10)
                .Select(i => DemoSeedIds.TrainingCourseId(i % DemoSeedIds.TrainingCourseCount))
                .ToArray();

            db.TrainingPlans.Add(new TrainingPlan
            {
                TenantId = TenantId,
                Year = year,
                OrgUnitId = DemoSeedIds.OrgUnitId(1 + yearOffset),
                PlannedCourses = JsonSerializer.Serialize(courseIds),
                ApprovedBy = year < now.Year ? DemoSeedIds.EmployeeId(yearOffset) : null,
                ApprovedAt = year < now.Year
                    ? new DateTime(year, 2, 15, 0, 0, 0, DateTimeKind.Utc)
                    : null,
                CreatedAt = new DateTime(year, 1, 10, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedNominationsAsync(
        TrainingDbContext db,
        Random rng,
        DateTime now,
        CancellationToken ct)
    {
        var planIds = await db.TrainingPlans
            .OrderBy(p => p.Year)
            .Select(p => p.Id)
            .ToListAsync(ct);

        for (var i = 0; i < 80; i++)
        {
            var empIndex = (i * 3 + 11) % DemoSeedIds.EmployeeCount;
            var courseIndex = (i * 5 + 1) % DemoSeedIds.TrainingCourseCount;
            var daysAgo = 5 + (i % 200);
            var nominatedAt = now.AddDays(-daysAgo);
            var status = (i % 10) switch
            {
                < 5 => ApprovalStatus.Approved,
                < 7 => ApprovalStatus.Pending,
                < 8 => ApprovalStatus.Rejected,
                < 9 => ApprovalStatus.Executed,
                _ => ApprovalStatus.Cancelled,
            };

            db.TrainingNominations.Add(new TrainingNomination
            {
                TenantId = TenantId,
                EmployeeId = DemoSeedIds.EmployeeId(empIndex),
                CourseId = DemoSeedIds.TrainingCourseId(courseIndex),
                NominatedBy = DemoSeedIds.EmployeeId(rng.Next(0, 40)),
                NominatedAt = nominatedAt,
                Status = status,
                TrainingPlanId = planIds.Count > 0 ? planIds[i % planIds.Count] : null,
                CreatedAt = nominatedAt,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedMissionFilesAsync(
        TrainingDbContext db,
        Random rng,
        DateTime now,
        DateOnly today,
        CancellationToken ct)
    {
        var statuses = Enum.GetValues<MissionStatus>();

        for (var i = 0; i < 20; i++)
        {
            var empIndex = (i * 13 + 5) % DemoSeedIds.EmployeeCount;
            var daysAgo = 30 + rng.Next(0, 400);
            var startDate = today.AddDays(-daysAgo);
            var duration = 5 + rng.Next(0, 25);
            var status = statuses[i % statuses.Length];
            var created = now.AddDays(-daysAgo - 10);

            db.MissionFiles.Add(new MissionFile
            {
                TenantId = TenantId,
                EmployeeId = DemoSeedIds.EmployeeId(empIndex),
                Destination = Destinations[i % Destinations.Length],
                StartDate = startDate,
                EndDate = startDate.AddDays(duration),
                Purpose = $"مهمة تدريبية / مؤتمر تخصصي — {Courses[i % Courses.Length].titleAr}",
                Sponsor = Providers[i % Providers.Length].name,
                Status = status,
                Documents = JsonSerializer.Serialize(new[]
                {
                    $"موافقة-مهمة-{i + 1:D3}.pdf",
                    $"برنامج-البعثة-{i + 1:D3}.pdf",
                }),
                CreatedAt = created,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedQualificationCodesAsync(TrainingDbContext db, DateTime now, CancellationToken ct)
    {
        for (var i = 0; i < Qualifications.Length; i++)
        {
            var (code, titleAr, titleEn) = Qualifications[i];
            var required = Enumerable.Range(i * 3, 3)
                .Select(idx => DemoSeedIds.TrainingCourseId(idx % DemoSeedIds.TrainingCourseCount))
                .ToArray();

            db.QualificationCodes.Add(new QualificationCode
            {
                TenantId = TenantId,
                Code = ScopedCode(code),
                TitleAr = titleAr,
                TitleEn = titleEn,
                RequiredCourses = JsonSerializer.Serialize(required),
                CreatedAt = now.AddMonths(-24 + i),
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedELearningDemoAsync(
        TrainingDbContext db,
        Random rng,
        DateTime now,
        CancellationToken ct)
    {
        // ELearningSeeder may already have seeded GOV-101 / DP-201 — use distinct codes and IDs.
        var demoCourseIds = new List<Guid>(ELearningCatalog.Length);

        for (var i = 0; i < ELearningCatalog.Length; i++)
        {
            var courseId = DemoSeedIds.Named($"elearning-course-{i}");
            demoCourseIds.Add(courseId);

            var scopedCode = ScopedCode(ELearningCatalog[i].code);
            if (await db.ELearningCourses.AnyAsync(c => c.Id == courseId || c.Code == scopedCode, ct))
                continue;

            var (code, titleAr, category, hours) = ELearningCatalog[i];
            var course = new ELearningCourse
            {
                Id = courseId,
                TenantId = TenantId,
                Code = scopedCode,
                TitleAr = titleAr,
                Description = $"محتوى تعلّم إلكتروني: {titleAr}",
                Category = category,
                EstimatedHours = hours,
                IsPublished = true,
                CreatedAt = now.AddMonths(-6 + i),
                UpdatedAt = now,
            };

            for (var m = 0; m < 4; m++)
            {
                var module = new CourseModule
                {
                    Id = DemoSeedIds.Named($"elearning-module-{i}-{m}"),
                    TenantId = TenantId,
                    ELearningCourseId = courseId,
                    Order = m + 1,
                    Title = $"الوحدة {m + 1}: {titleAr}",
                    CreatedAt = course.CreatedAt,
                    UpdatedAt = now,
                };

                for (var l = 0; l < 5; l++)
                {
                    module.Lessons.Add(new Lesson
                    {
                        Id = DemoSeedIds.Named($"elearning-lesson-{i}-{m}-{l}"),
                        TenantId = TenantId,
                        CourseModuleId = module.Id,
                        Order = l + 1,
                        Title = $"الدرس {l + 1} — {titleAr}",
                        Content = $"محتوى تعليمي للدرس رقم {l + 1} ضمن الوحدة {m + 1}.",
                        CreatedAt = course.CreatedAt,
                        UpdatedAt = now,
                    });
                }

                course.Modules.Add(module);
            }

            db.ELearningCourses.Add(course);
        }

        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync(ct);

        if (await db.LearningEnrollments.AnyAsync(ct))
            return;

        // Prefer demo courses; fall back to any published course if needed.
        var courseIds = await db.ELearningCourses
            .Where(c => demoCourseIds.Contains(c.Id) || c.IsPublished)
            .OrderBy(c => c.Code)
            .Select(c => c.Id)
            .ToListAsync(ct);

        if (courseIds.Count == 0)
            return;

        var completedEnrollments = new List<LearningEnrollment>();

        for (var i = 0; i < 100; i++)
        {
            var empIndex = (i * 2 + 7) % DemoSeedIds.EmployeeCount;
            var courseId = courseIds[i % courseIds.Count];
            var daysAgo = 3 + (i % 180);
            var enrolledAt = now.AddDays(-daysAgo);
            var status = (i % 10) switch
            {
                < 6 => LearningEnrollmentStatus.Completed,
                < 8 => LearningEnrollmentStatus.InProgress,
                _ => LearningEnrollmentStatus.Enrolled,
            };

            var enrollment = new LearningEnrollment
            {
                TenantId = TenantId,
                ELearningCourseId = courseId,
                EmployeeId = DemoSeedIds.EmployeeId(empIndex),
                EnrolledAt = enrolledAt,
                ProgressPercent = status switch
                {
                    LearningEnrollmentStatus.Completed => 100,
                    LearningEnrollmentStatus.InProgress => 20 + rng.Next(0, 70),
                    _ => rng.Next(0, 15),
                },
                Status = status,
                CompletedAt = status == LearningEnrollmentStatus.Completed
                    ? enrolledAt.AddDays(rng.Next(3, 40))
                    : null,
                CreatedAt = enrolledAt,
                UpdatedAt = now,
            };

            db.LearningEnrollments.Add(enrollment);
            if (status == LearningEnrollmentStatus.Completed)
                completedEnrollments.Add(enrollment);
        }

        await db.SaveChangesAsync(ct);

        var certCount = Math.Min(60, completedEnrollments.Count);
        for (var i = 0; i < certCount; i++)
        {
            var enrollment = completedEnrollments[i];
            db.LearningCertificates.Add(new LearningCertificate
            {
                TenantId = TenantId,
                LearningEnrollmentId = enrollment.Id,
                CertificateNo = ScopedCode($"ELC-{(enrollment.CompletedAt ?? now).Year}-{(i + 1):D4}"),
                IssuedAt = enrollment.CompletedAt ?? now,
                CreatedAt = enrollment.CompletedAt ?? now,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
