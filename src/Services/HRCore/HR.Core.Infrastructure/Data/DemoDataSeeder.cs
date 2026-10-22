using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.Exports;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Core.Infrastructure.Data;

/// <summary>
/// Populates HR Core with realistic Syrian government demo data (~500 employees).
/// Idempotent: skips if demo employees already exist.
/// Uses <see cref="DemoSeedIds"/> for deterministic IDs shared across services.
/// </summary>
public static class DemoDataSeeder
{
    private static readonly Guid TenantId = TenantDefaults.DefaultTenantId;

    private static readonly (string code, string nameAr, string nameEn, int parentIndex)[] OrgUnitDefs =
    [
        // 0 ROOT — seeded by HRCoreDataSeeder
        ("ROOT", "المؤسسة العامة", "Root Organization", -1),
        // 1 HR — seeded by HRCoreDataSeeder
        ("HR", "شعبة الموارد البشرية", "Human Resources", 0),
        // Level-1 (under ROOT)
        ("FIN", "الدائرة المالية", "Finance Department", 0),
        ("LEG", "الدائرة القانونية", "Legal Department", 0),
        ("IT", "دائرة تقانة المعلومات", "IT Department", 0),
        ("AUD", "دائرة الرقابة الداخلية", "Internal Audit", 0),
        ("ADM", "الدائرة الإدارية", "Administrative Affairs", 0),
        ("PLN", "دائرة التخطيط والتطوير", "Planning & Development", 0),
        ("PR", "دائرة العلاقات العامة", "Public Relations", 0),
        ("DG", "مكتب المدير العام", "Director General Office", 0),
        ("ARC", "شعبة الأرشيف", "Archive Division", 0),
        // Level-2 / Level-3 sections
        ("HR-REC", "قسم التعيينات", "Recruitment Section", 1),
        ("HR-PAY", "قسم الرواتب", "Payroll Section", 1),
        ("HR-TRN", "قسم التدريب", "Training Section", 1),
        ("HR-ATT", "قسم الحضور والانضباط", "Attendance Section", 1),
        ("FIN-ACC", "قسم المحاسبة", "Accounting Section", 2),
        ("FIN-BUD", "قسم الموازنة", "Budget Section", 2),
        ("FIN-PUR", "قسم المشتريات", "Procurement Section", 2),
        ("IT-OPS", "قسم تشغيل النظم", "Systems Operations", 4),
        ("IT-SEC", "قسم أمن المعلومات", "InfoSec Section", 4),
        ("IT-SUP", "قسم الدعم الفني", "Helpdesk Section", 4),
        ("ADM-LOG", "قسم اللوجستيات", "Logistics Section", 6),
        ("ADM-FAC", "قسم المرافق", "Facilities Section", 6),
        ("LEG-CON", "قسم العقود", "Contracts Legal Section", 3),
        ("LEG-LIT", "قسم التقاضي", "Litigation Section", 3),
        ("AUD-INT", "قسم التدقيق الداخلي", "Internal Audit Section", 5),
        ("PLN-STR", "قسم التخطيط الاستراتيجي", "Strategic Planning", 7),
        ("PLN-KPI", "قسم مؤشرات الأداء", "KPI Section", 7),
        ("PR-MED", "قسم الإعلام", "Media Section", 8),
        ("ARC-DIG", "قسم الأرشفة الرقمية", "Digital Archive Section", 10),
    ];

    public static async Task SeedAsync(HRCoreDbContext db, ILogger? logger = null, CancellationToken ct = default)
    {
        if (await db.Employees.AnyAsync(e => e.NationalId == "01010000001", ct))
        {
            logger?.LogInformation("Demo data already seeded — skipping.");
            return;
        }

        var now = DateTime.UtcNow;
        var rng = new Random(42);

        logger?.LogInformation("Seeding demo organizational structure ({Count} units)...", DemoSeedIds.OrgUnitCount);
        var orgIds = await SeedOrgUnitsAsync(db, ct);

        logger?.LogInformation("Seeding job catalog...");
        await SeedJobCatalogAsync(db, now, ct);

        logger?.LogInformation("Seeding {Count} demo employees...", DemoSeedIds.EmployeeCount);
        var employees = BuildEmployees(orgIds, now, rng);
        db.Employees.AddRange(employees);
        await db.SaveChangesAsync(ct);

        logger?.LogInformation("Seeding employee profiles, contacts, qualifications...");
        SeedEmployeeRelated(db, employees, now, rng);
        await db.SaveChangesAsync(ct);

        logger?.LogInformation("Seeding employment records and evaluations...");
        SeedEmploymentAndEvaluations(db, employees, now, rng);
        await db.SaveChangesAsync(ct);

        logger?.LogInformation("Seeding documents...");
        SeedDocuments(db, employees, now);
        await db.SaveChangesAsync(ct);

        logger?.LogInformation("Seeding attendance (last 60 days)...");
        await SeedAttendanceAsync(db, employees, now, rng, ct);

        logger?.LogInformation("Seeding funded positions...");
        SeedFundedPositions(db, employees, orgIds, now);
        await db.SaveChangesAsync(ct);

        logger?.LogInformation("Seeding recruitment requisitions...");
        SeedRecruitmentRequisitions(db, now);
        await db.SaveChangesAsync(ct);

        logger?.LogInformation("Seeding workforce plans, career paths, vacancies...");
        SeedOrgExtras(db, employees, orgIds, now, rng);
        await db.SaveChangesAsync(ct);

        logger?.LogInformation("Seeding shift roster assignments...");
        await SeedShiftsAsync(db, employees, orgIds, now, ct);

        logger?.LogInformation("Seeding integration audit/export receipts...");
        await SeedIntegrationAsync(db, now, ct);

        logger?.LogInformation("Demo data seeding complete: {Count} employees.", employees.Count);
    }

    private static async Task SeedIntegrationAsync(HRCoreDbContext db, DateTime now, CancellationToken ct)
    {
        if (!await db.AuditLogs.AnyAsync(ct))
            db.AuditLogs.AddRange(DemoIntegrationData.BuildAuditLogs(120, "Employee", now));

        if (!await db.ExportJobs.AnyAsync(ct))
        {
            db.ExportJobs.AddRange(
                DemoIntegrationData.BuildExportJobs(3, "hrcore", ExportJobTemplates.HrCoreEmployeesCsv, now));
        }

        if (!await db.IntegrationMessageReceipts.AnyAsync(ct))
        {
            db.IntegrationMessageReceipts.AddRange(
                DemoIntegrationData.BuildMessageReceipts(50, "HRCore.OrgUnitPublishedConsumer", now));
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task<Guid[]> SeedOrgUnitsAsync(HRCoreDbContext db, CancellationToken ct)
    {
        var existing = await db.OrgUnits.Select(o => o.Id).ToListAsync(ct);
        var ids = new Guid[DemoSeedIds.OrgUnitCount];

        for (var i = 0; i < DemoSeedIds.OrgUnitCount; i++)
        {
            ids[i] = DemoSeedIds.OrgUnitId(i);
            var (code, nameAr, nameEn, parentIndex) = OrgUnitDefs[i];
            if (existing.Contains(ids[i]))
                continue;

            var parentId = parentIndex < 0 ? (Guid?)null : DemoSeedIds.OrgUnitId(parentIndex);
            var path = parentIndex < 0
                ? $"/{ids[i]:D}/"
                : $"/{DemoSeedIds.OrgUnitId(parentIndex):D}/{ids[i]:D}/";

            // Rebuild full path for deeper nesting
            if (parentIndex >= 0)
            {
                var segments = new List<Guid> { ids[i] };
                var p = parentIndex;
                while (p >= 0)
                {
                    segments.Insert(0, DemoSeedIds.OrgUnitId(p));
                    p = OrgUnitDefs[p].parentIndex;
                }
                path = "/" + string.Join("/", segments.Select(g => g.ToString("D"))) + "/";
            }

            db.OrgUnits.Add(new OrgUnit
            {
                Id = ids[i],
                TenantId = TenantId,
                Code = code,
                NameAr = nameAr,
                NameEn = nameEn,
                ParentId = parentId,
                MaterializedPath = path,
            });
        }

        await db.SaveChangesAsync(ct);
        return ids;
    }

    private static async Task SeedJobCatalogAsync(HRCoreDbContext db, DateTime now, CancellationToken ct)
    {
        if (await db.JobCatalogEntries.AnyAsync(ct))
            return;

        for (var i = 0; i < DemoNameBank.JobTitles.Length; i++)
        {
            var title = DemoNameBank.JobTitles[i];
            var grade = i switch
            {
                < 2 => "G1",
                < 5 => "G2",
                < 10 => "G3",
                < 20 => "G4",
                < 30 => "G5",
                _ => "G6"
            };
            db.JobCatalogEntries.Add(new JobCatalogEntry
            {
                Id = DemoSeedIds.JobCatalogId(i),
                TenantId = TenantId,
                JobCode = $"JOB-{i + 1:D3}",
                TitleAr = title,
                TitleEn = title,
                GradeCode = grade,
                OccupationalGroup = i < 10 ? "إدارية" : i < 25 ? "فنية" : "خدمية",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static List<Employee> BuildEmployees(Guid[] orgIds, DateTime now, Random rng)
    {
        var employees = new List<Employee>(DemoSeedIds.EmployeeCount);

        for (var i = 0; i < DemoSeedIds.EmployeeCount; i++)
        {
            var (first, middle, last, isFemale) = DemoNameBank.BuildPerson(i);
            var fullName = DemoNameBank.FullName(first, middle, last);
            var birthYear = 1960 + (i * 7 + 13) % 41; // 1960–2000
            var birth = new DateTime(birthYear, (i % 12) + 1, (i % 28) + 1, 0, 0, 0, DateTimeKind.Utc);
            var hireYear = Math.Max(2005, birthYear + 22 + i % 5);
            hireYear = Math.Min(hireYear, 2024);
            var hire = new DateTime(hireYear, ((i + 3) % 12) + 1, ((i + 5) % 28) + 1, 0, 0, 0, DateTimeKind.Utc);

            var orgIndex = i < 10
                ? 0 // directors under ROOT / DG
                : 1 + (i % (orgIds.Length - 1));
            if (orgIndex >= orgIds.Length) orgIndex = 1;

            var status = DemoSeedIds.StatusBucket(i) switch
            {
                >= 98 => EmploymentStatus.Retired,
                >= 95 => EmploymentStatus.Terminated,
                >= 90 => EmploymentStatus.Seconded,
                >= 85 => EmploymentStatus.OnLeave,
                _ => EmploymentStatus.Active
            };

            var cadre = DemoSeedIds.IsSupervisor(i) ? EmployeeCadre.Supervisory : EmployeeCadre.Executive;
            var marital = (i % 7) switch
            {
                0 or 1 or 2 or 3 => MaritalStatus.Married,
                4 or 5 => MaritalStatus.Single,
                6 => MaritalStatus.Divorced,
                _ => MaritalStatus.Widowed
            };

            employees.Add(new Employee
            {
                Id = DemoSeedIds.EmployeeId(i),
                TenantId = TenantId,
                NationalId = DemoNameBank.NationalId(i),
                FirstName = first,
                MiddleName = middle,
                LastName = last,
                FullName = fullName,
                BirthDate = birth,
                Gender = isFemale ? Gender.Female : Gender.Male,
                MaritalStatus = marital,
                PrimaryOrgUnitId = orgIds[orgIndex],
                CurrentStatus = status,
                HireDate = hire,
                Cadre = cadre,
                WorkScheduleMode = i % 17 == 0 ? WorkScheduleMode.PartTime : WorkScheduleMode.FullTime,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        return employees;
    }

    private static void SeedEmployeeRelated(HRCoreDbContext db, List<Employee> employees, DateTime now, Random rng)
    {
        for (var i = 0; i < employees.Count; i++)
        {
            var emp = employees[i];
            var gov = DemoNameBank.Governorate(i);

            db.EmployeeProfiles.Add(new EmployeeProfile
            {
                TenantId = TenantId,
                EmployeeId = emp.Id,
                Nationality = "سوري",
                PlaceOfBirth = gov,
                NationalIdIssuePlace = gov,
                ResidentialAddress = DemoNameBank.Address(i),
                PermanentAddress = DemoNameBank.Address(i + 17),
                EmergencyContact = $"{DemoNameBank.BuildPerson(i + 50).First} — {DemoNameBank.Phone(i + 900)}",
                Notes = null,
                CreatedAt = now,
                UpdatedAt = now,
            });

            var phone = new ContactInfo
            {
                TenantId = TenantId,
                EmployeeId = emp.Id,
                Type = ContactType.Phone,
                Value = DemoNameBank.Phone(i),
                IsPrimary = true,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.ContactInfos.Add(phone);

            db.ContactInfos.Add(new ContactInfo
            {
                TenantId = TenantId,
                EmployeeId = emp.Id,
                Type = ContactType.Email,
                Value = DemoNameBank.Email(emp.FirstName, emp.LastName, i),
                IsPrimary = false,
                CreatedAt = now,
                UpdatedAt = now,
            });

            var qualCount = 1 + i % 3;
            for (var q = 0; q < qualCount; q++)
            {
                var (code, title, provider) = q switch
                {
                    0 => ("SEC", "شهادة الثانوية العامة", "وزارة التربية"),
                    1 => ("UNI", "إجازة جامعية", "جامعة دمشق"),
                    _ => ("MSC", "ماجستير", "جامعة حلب"),
                };
                db.Qualifications.Add(new Qualification
                {
                    TenantId = TenantId,
                    EmployeeId = emp.Id,
                    QualificationCode = code,
                    Title = title,
                    Provider = provider,
                    CompletionDate = emp.BirthDate.AddYears(18 + q * 4),
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
        }
    }

    private static void SeedEmploymentAndEvaluations(
        HRCoreDbContext db, List<Employee> employees, DateTime now, Random rng)
    {
        for (var i = 0; i < employees.Count; i++)
        {
            var emp = employees[i];
            var title = DemoNameBank.JobTitle(
                DemoSeedIds.IsSupervisor(i) ? i % 5 : 5 + (i % 20));

            db.EmploymentRecords.Add(new EmploymentRecord
            {
                TenantId = TenantId,
                EmployeeId = emp.Id,
                PositionTitle = title,
                PositionCode = $"POS-{i + 1:D4}",
                OrgUnitId = emp.PrimaryOrgUnitId,
                StartDate = emp.HireDate ?? now.AddYears(-5),
                EmploymentType = EmploymentType.Permanent,
                Status = "active",
                CreatedAt = now,
                UpdatedAt = now,
            });

            // Occasional promotion history
            if (i % 3 == 0 && emp.HireDate is { } hire && hire.Year < 2020)
            {
                db.EmploymentRecords.Add(new EmploymentRecord
                {
                    TenantId = TenantId,
                    EmployeeId = emp.Id,
                    PositionTitle = "موظف مبتدئ",
                    PositionCode = $"POS-{i + 1:D4}-A",
                    OrgUnitId = emp.PrimaryOrgUnitId,
                    StartDate = hire,
                    EndDate = hire.AddYears(3),
                    EmploymentType = EmploymentType.Permanent,
                    Status = "ended",
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }

            db.EmploymentStatusHistories.Add(new EmploymentStatusHistory
            {
                TenantId = TenantId,
                EmployeeId = emp.Id,
                OldStatus = null,
                NewStatus = EmploymentStatus.Active.ToString(),
                ChangedAt = emp.HireDate ?? now.AddYears(-5),
                Reason = "تعيين",
                CreatedAt = now,
                UpdatedAt = now,
            });

            if (emp.CurrentStatus != EmploymentStatus.Active)
            {
                db.EmploymentStatusHistories.Add(new EmploymentStatusHistory
                {
                    TenantId = TenantId,
                    EmployeeId = emp.Id,
                    OldStatus = EmploymentStatus.Active.ToString(),
                    NewStatus = emp.CurrentStatus.ToString(),
                    ChangedAt = now.AddMonths(-rng.Next(1, 18)),
                    Reason = emp.CurrentStatus switch
                    {
                        EmploymentStatus.OnLeave => "إجازة طويلة",
                        EmploymentStatus.Seconded => "انتداب خارجي",
                        EmploymentStatus.Terminated => "إنهاء خدمة",
                        EmploymentStatus.Retired => "إحالة على التقاعد",
                        _ => "تغيير حالة"
                    },
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }

            // Performance evaluations 2022–2025
            for (var year = 2022; year <= 2025; year++)
            {
                if (emp.HireDate?.Year > year) continue;
                db.PerformanceEvaluations.Add(new PerformanceEvaluation
                {
                    TenantId = TenantId,
                    EmployeeId = emp.Id,
                    Year = year,
                    EvaluatorId = DemoSeedIds.EmployeeId(0),
                    Score = 60 + rng.Next(0, 40),
                    Summary = "تقييم أداء سنوي",
                    Status = year == 2025 ? "draft" : "approved",
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
        }
    }

    private static void SeedDocuments(HRCoreDbContext db, List<Employee> employees, DateTime now)
    {
        foreach (var emp in employees)
        {
            db.EmployeeDocuments.Add(new EmployeeDocument
            {
                TenantId = TenantId,
                EmployeeId = emp.Id,
                DocType = DocumentType.NationalId,
                DocTitle = $"الهوية الوطنية - {emp.FullName}",
                IssuedBy = "السجل المدني",
                IssueDate = emp.BirthDate.AddYears(14),
                Verified = true,
                CreatedAt = now,
                UpdatedAt = now,
            });

            db.EmployeeDocuments.Add(new EmployeeDocument
            {
                TenantId = TenantId,
                EmployeeId = emp.Id,
                DocType = DocumentType.Certificate,
                DocTitle = $"شهادة جامعية - {emp.FullName}",
                IssuedBy = "جامعة دمشق",
                IssueDate = emp.BirthDate.AddYears(22),
                Verified = true,
                CreatedAt = now,
                UpdatedAt = now,
            });

            if (emp.HireDate is { } hire)
            {
                db.EmployeeDocuments.Add(new EmployeeDocument
                {
                    TenantId = TenantId,
                    EmployeeId = emp.Id,
                    DocType = DocumentType.Other,
                    DocTitle = $"قرار التعيين - {emp.FullName}",
                    IssuedBy = "المدير العام",
                    IssueDate = hire,
                    Verified = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
        }
    }

    private static async Task SeedAttendanceAsync(
        HRCoreDbContext db, List<Employee> employees, DateTime now, Random rng, CancellationToken ct)
    {
        var active = employees.Where(e => e.CurrentStatus == EmploymentStatus.Active).ToList();
        var today = DateOnly.FromDateTime(now);
        var start = today.AddDays(-60);
        var batch = 0;

        foreach (var emp in active)
        {
            for (var d = start; d < today; d = d.AddDays(1))
            {
                if (d.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday)
                    continue;

                var status = rng.Next(100) switch
                {
                    < 85 => DailyAttendanceStatus.Present,
                    < 92 => DailyAttendanceStatus.Remote,
                    < 97 => DailyAttendanceStatus.Absent,
                    _ => DailyAttendanceStatus.OnLeave
                };

                db.DailyAttendanceRecords.Add(new DailyAttendanceRecord
                {
                    TenantId = TenantId,
                    EmployeeId = emp.Id,
                    AttendanceDate = d,
                    Status = status,
                    CreatedAt = d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                    UpdatedAt = d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                });
                batch++;

                if (batch >= 500)
                {
                    await db.SaveChangesAsync(ct);
                    batch = 0;
                }
            }
        }

        if (batch > 0)
            await db.SaveChangesAsync(ct);
    }

    private static void SeedFundedPositions(
        HRCoreDbContext db, List<Employee> employees, Guid[] orgIds, DateTime now)
    {
        for (var i = 0; i < employees.Count; i++)
        {
            var emp = employees[i];
            var grade = DemoSeedIds.IsSupervisor(i)
                ? (i < 10 ? "G1" : "G2")
                : i % 4 == 0 ? "G4" : i % 4 == 1 ? "G5" : "G6";

            db.FundedPositions.Add(new FundedPosition
            {
                TenantId = TenantId,
                PositionCode = $"FP-{i + 1:D4}",
                GradeCode = grade,
                FTE = 1.0m,
                BudgetYear = 2026,
                Status = emp.CurrentStatus == EmploymentStatus.Active
                    ? FundedPositionStatus.Occupied
                    : FundedPositionStatus.Vacant,
                OrgUnitId = emp.PrimaryOrgUnitId ?? orgIds[0],
                JobCatalogEntryId = DemoSeedIds.JobCatalogId(i % DemoNameBank.JobTitles.Length),
                OccupantEmployeeId = emp.CurrentStatus == EmploymentStatus.Active ? emp.Id : null,
                CostCenter = $"CC-{(i % DemoSeedIds.OrgUnitCount) + 1:D2}",
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
    }

    private static void SeedRecruitmentRequisitions(HRCoreDbContext db, DateTime now)
    {
        if (db.RecruitmentRequisitions.Any(r => r.TenantId == TenantId))
            return;

        var vacant = db.FundedPositions
            .Include(p => p.JobCatalogEntry)
            .Where(p => p.TenantId == TenantId && p.Status == FundedPositionStatus.Vacant)
            .OrderBy(p => p.PositionCode)
            .Take(3)
            .ToList();

        for (var i = 0; i < vacant.Count; i++)
        {
            var position = vacant[i];
            var requisition = new RecruitmentRequisition
            {
                TenantId = TenantId,
                RequisitionNumber = $"REQ-{now.Year}-{(i + 1):D5}",
                FundedPositionId = position.Id,
                JobTitle = position.JobCatalogEntry?.TitleAr ?? position.GradeCode,
                Status = i switch
                {
                    1 => RequisitionStatus.Approved,
                    2 => RequisitionStatus.Published,
                    _ => RequisitionStatus.Draft
                },
                RequestedDate = now.AddDays(-(i + 1)),
                ApprovedDate = i >= 1 ? now.AddDays(-i) : null,
                PublishedAt = i == 2 ? now.AddDays(-1) : null,
                // HCM-026 (F2): published demo requisition gets a 30-day application window (copied to the posting).
                ApplicationClosingDate = i == 2 ? DateOnly.FromDateTime(now).AddDays(30) : null,
                JustificationNotes = "طلب شاغر تجريبي لاختبار دورة التوظيف.",
                VacancyCount = 1,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.RecruitmentRequisitions.Add(requisition);

            if (requisition.Status is RequisitionStatus.Approved or RequisitionStatus.Published)
            {
                // HCM-026: publishing requires an active criteria version (new JSON format).
                db.RecruitmentAssessmentCriteria.Add(new RecruitmentAssessmentCriteria
                {
                    TenantId = TenantId,
                    RequisitionId = requisition.Id,
                    VersionNumber = 1,
                    Title = "معايير التقييم v1",
                    CriteriaJson = Seeding.RecruitmentSeedCriteria.Json,
                    IsActive = true,
                    EffectiveFrom = now.AddDays(-(i + 1)),
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }

            if (i == 2)
            {
                var posting = RecruitmentRequisitionService.MapToPublicJobPosting(requisition, position);
                db.PublicJobPostings.Add(posting);
                requisition.PublicJobPosting = posting;
            }
        }
    }

    private static void SeedOrgExtras(
        HRCoreDbContext db, List<Employee> employees, Guid[] orgIds, DateTime now, Random rng)
    {
        foreach (var year in new[] { 2024, 2025, 2026 })
        {
            db.WorkforcePlans.Add(new WorkforcePlan
            {
                TenantId = TenantId,
                Title = $"خطة القوى العاملة {year}",
                FiscalYear = year,
                OrgUnitId = orgIds[0],
                Summary = $"خطة توزيع الكوادر للعام {year}",
                Status = year == 2026 ? "draft" : "approved",
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        var pathTitles = new[]
        {
            ("المسار الإداري", "من كاتب إلى مدير دائرة"),
            ("المسار المالي", "من محاسب إلى مدير مالي"),
            ("المسار التقني", "من مبرمج إلى مدير تقانة"),
            ("المسار القانوني", "من باحث إلى مستشار"),
            ("مسار الموارد البشرية", "من موظف موارد إلى رئيس شعبة"),
        };

        for (var p = 0; p < pathTitles.Length; p++)
        {
            var path = new CareerPath
            {
                Id = DemoSeedIds.Named($"career-path-{p}"),
                TenantId = TenantId,
                Title = pathTitles[p].Item1,
                Description = pathTitles[p].Item2,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.CareerPaths.Add(path);

            for (var s = 0; s < 4; s++)
            {
                db.CareerPathSteps.Add(new CareerPathStep
                {
                    TenantId = TenantId,
                    CareerPathId = path.Id,
                    Order = s + 1,
                    PositionTitle = DemoNameBank.JobTitles[Math.Min(s + p, DemoNameBank.JobTitles.Length - 1)],
                    MinYears = s * 3,
                    Requirements = "خبرة ومؤهلات مناسبة",
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
        }

        for (var k = 0; k < 10; k++)
        {
            var holder = employees[k];
            var key = new KeyPosition
            {
                Id = DemoSeedIds.Named($"key-position-{k}"),
                TenantId = TenantId,
                Title = DemoNameBank.JobTitles[k],
                OrgUnitId = orgIds[k % orgIds.Length],
                CurrentHolderEmployeeId = holder.Id,
                Criticality = 5 - (k % 5),
                Notes = "موقع حرج",
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.KeyPositions.Add(key);

            var plan = new SuccessionPlan
            {
                Id = DemoSeedIds.Named($"succession-{k}"),
                TenantId = TenantId,
                KeyPositionId = key.Id,
                Year = 2026,
                Notes = "خطة تعاقب",
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.SuccessionPlans.Add(plan);

            for (var c = 0; c < 2; c++)
            {
                var candIdx = 40 + k * 2 + c;
                if (candIdx >= employees.Count) break;
                db.SuccessorCandidates.Add(new SuccessorCandidate
                {
                    TenantId = TenantId,
                    SuccessionPlanId = plan.Id,
                    EmployeeId = employees[candIdx].Id,
                    LatestEvaluationScore = 70 + rng.Next(0, 25),
                    YearsInService = 5 + rng.Next(0, 15),
                    MeetsQualification = true,
                    Readiness = c == 0 ? ReadinessLevel.ReadyNow : ReadinessLevel.ReadyWithinYear,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
        }

        for (var v = 0; v < 5; v++)
        {
            var vacancy = new InternalVacancy
            {
                Id = DemoSeedIds.Named($"internal-vacancy-{v}"),
                TenantId = TenantId,
                Title = $"شاغر داخلي — {DemoNameBank.JobTitles[10 + v]}",
                OrgUnitId = orgIds[(v + 2) % orgIds.Length],
                Description = "إعلان شاغر للترشيح الداخلي",
                RequiredQualifications = "شهادة جامعية + خبرة 3 سنوات",
                ClosingDate = DateOnly.FromDateTime(now.AddDays(30 + v * 7)),
                IsOpen = true,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.InternalVacancies.Add(vacancy);

            for (var a = 0; a < 4; a++)
            {
                var empIdx = 50 + v * 4 + a;
                if (empIdx >= employees.Count) break;
                db.InternalApplications.Add(new InternalApplication
                {
                    TenantId = TenantId,
                    InternalVacancyId = vacancy.Id,
                    EmployeeId = employees[empIdx].Id,
                    AppliedAt = now.AddDays(-rng.Next(1, 20)),
                    Status = (InternalApplicationStatus)(a % 5),
                    Motivation = "أرغب في التطور المهني ضمن المؤسسة",
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
        }

        // Additional public postings (HRCoreDataSeeder may have seeded one already)
        var publishedRequisition = db.RecruitmentRequisitions
            .Include(r => r.PublicJobPosting)
            .FirstOrDefault(r => r.TenantId == TenantId && r.Status == RequisitionStatus.Published);

        for (var p = 0; p < 3; p++)
        {
            PublicJobPosting posting;
            if (p == 0 && publishedRequisition?.PublicJobPosting is not null)
            {
                posting = publishedRequisition.PublicJobPosting;
            }
            else
            {
                posting = new PublicJobPosting
                {
                    Id = DemoSeedIds.Named($"public-posting-{p}"),
                    TenantId = TenantId,
                    Title = $"وظيفة شاغرة — {DemoNameBank.JobTitles[15 + p]}",
                    Entity = "المؤسسة العامة",
                    Location = DemoNameBank.Cities[p],
                    Description = "إعلان توظيف عام",
                    Requirements = "جنسية سورية — مؤهل مناسب",
                    ClosingDate = DateOnly.FromDateTime(now.AddDays(45)),
                    IsPublished = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                db.PublicJobPostings.Add(posting);
            }

            for (var c = 0; c < 17; c++)
            {
                var idx = p * 17 + c;
                var (first, middle, last, _) = DemoNameBank.BuildPerson(3000 + idx);
                db.CitizenApplications.Add(new CitizenApplication
                {
                    TenantId = TenantId,
                    PublicJobPostingId = posting.Id,
                    RequisitionId = posting.RequisitionId,
                    ReferenceNumber = $"CA-2026-{idx + 1:D4}",
                    FullName = DemoNameBank.FullName(first, middle, last),
                    NationalId = $"0202{(idx + 1):D7}",
                    Phone = DemoNameBank.Phone(2000 + idx),
                    Email = $"citizen{idx}@mail.sy",
                    CoverNote = "أتقدم بطلبي شاغراً",
                    SubmittedAt = now.AddDays(-rng.Next(1, 30)),
                    Status = (CitizenApplicationStatus)(c % 5),
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
        }

        var publishedRequisitions = db.RecruitmentRequisitions
            .Where(r => r.TenantId == TenantId)
            .OrderBy(r => r.RequisitionNumber)
            .ToList();

        for (var r = 0; r < 10; r++)
        {
            var (first, middle, last, _) = DemoNameBank.BuildPerson(4000 + r);
            var requisition = r < 9 && publishedRequisitions.Count > 0
                ? publishedRequisitions[r % publishedRequisitions.Count]
                : null;
            db.RecruitmentApplications.Add(new RecruitmentApplication
            {
                TenantId = TenantId,
                ApplicantFullName = DemoNameBank.FullName(first, middle, last),
                NationalId = $"0303{(r + 1):D7}",
                RequisitionId = requisition?.Id,
                AppliedPosition = requisition?.JobTitle ?? DemoNameBank.JobTitles[20 + r % 10],
                AppliedAt = now.AddDays(-rng.Next(5, 60)),
                Status = (RecruitmentApplicationStatus)(r % 5),
                IntakeChannel = RecruitmentIntakeChannel.HrEntered,
                Notes = "طلب توظيف",
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        db.HrStatisticReports.Add(new HrStatisticReport
        {
            TenantId = TenantId,
            Title = "تقرير إحصائي ربع سنوي",
            PeriodStart = new DateOnly(2026, 1, 1),
            PeriodEnd = new DateOnly(2026, 3, 31),
            OrgUnitId = orgIds[0],
            MetricsSummary = "عدد الموظفين النشطين، معدل الغياب، الترقيات",
            CreatedAt = now,
            UpdatedAt = now,
        });
    }

    private static async Task SeedShiftsAsync(
        HRCoreDbContext db, List<Employee> employees, Guid[] orgIds, DateTime now, CancellationToken ct)
    {
        var shifts = await db.Shifts.ToListAsync(ct);
        if (shifts.Count == 0)
            return;

        var dayShift = shifts.FirstOrDefault(s => s.Code == "DAY") ?? shifts[0];
        var today = DateOnly.FromDateTime(now);
        var roster = new ShiftRoster
        {
            Id = DemoSeedIds.Named("shift-roster-demo"),
            TenantId = TenantId,
            Title = "جدول الورديات التجريبي — الشهر الحالي",
            OrgUnitId = orgIds[1],
            PeriodStart = today.AddDays(-14),
            PeriodEnd = today.AddDays(14),
            Notes = "بيانات تجريبية",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.ShiftRosters.Add(roster);
        await db.SaveChangesAsync(ct);

        var assignees = employees
            .Where(e => e.CurrentStatus == EmploymentStatus.Active)
            .Take(40)
            .ToList();

        foreach (var emp in assignees)
        {
            for (var d = 0; d < 10; d++)
            {
                var workDate = today.AddDays(-d);
                if (workDate.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday)
                    continue;

                db.ShiftAssignments.Add(new ShiftAssignment
                {
                    TenantId = TenantId,
                    ShiftRosterId = roster.Id,
                    ShiftId = dayShift.Id,
                    EmployeeId = emp.Id,
                    WorkDate = workDate,
                    OvertimeHours = d % 5 == 0 ? 2 : 0,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
