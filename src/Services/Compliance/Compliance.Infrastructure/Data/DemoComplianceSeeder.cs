using Compliance.Domain.Entities;
using Compliance.Domain.Enums;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.Exports;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Compliance.Infrastructure.Data;

/// <summary>
/// Populates Compliance with demo complaints, findings, discipline, legal cases, and DSARs.
/// Idempotent: skips when complaints already exist.
/// </summary>
public static class DemoComplianceSeeder
{
    private static readonly Guid TenantId = TenantDefaults.DefaultTenantId;

    private static readonly string[] ComplaintSubjects =
    [
        "تظلم من تقييم الأداء السنوي",
        "شكوى بشأن بيئة العمل",
        "ادعاء تمييز في الترقية",
        "شكوى تتعلق بالدوام الإضافي",
        "تظلم من إجراء تأديبي سابق",
        "بلاغ عن مخالفة إدارية",
        "شكوى بخصوص بدل النقل",
        "ادعاء تحرش معنوي في مكان العمل",
        "تظلم من قرار النقل",
        "شكوى تتعلق بتأخر صرف المستحقات",
    ];

    private static readonly string[] FindingDescriptions =
    [
        "ضعف في توثيق إجراءات التوظيف",
        "عدم الالتزام بسياسة الفصل بين المهام",
        "تأخير في إغلاق ملاحظات التدقيق السابقة",
        "ثغرات في أرشفة المستندات السرية",
        "قصور في متابعة التوصيات الرقابية",
        "عدم اكتمال سجلات الحضور لبعض الوحدات",
        "مخالفة إجراءات المشتريات الصغيرة",
        "ضعف ضوابط الوصول إلى بيانات الموظفين",
    ];

    public static async Task SeedAsync(ComplianceDbContext db, CancellationToken ct = default)
    {
        if (await db.Complaints.AnyAsync(ct))
            return;

        var rng = new Random(42);
        var now = DateTime.UtcNow;

        var complaintIds = await SeedComplaintsAsync(db, rng, now, ct);
        await SeedFindingsAsync(db, rng, now, ct);
        await SeedDisciplinaryActionsAsync(db, complaintIds, rng, now, ct);
        await SeedLegalCasesAsync(db, rng, now, ct);
        await SeedDsarRequestsAsync(db, rng, now, ct);
        await SeedPhase3HelpersAsync(db, rng, now, ct);
    }

    private static async Task<List<Guid>> SeedComplaintsAsync(
        ComplianceDbContext db,
        Random rng,
        DateTime now,
        CancellationToken ct)
    {
        var complaintIds = new List<Guid>(25);
        var statuses = Enum.GetValues<ComplaintStatus>();

        for (var i = 0; i < 25; i++)
        {
            var empIndex = rng.Next(DemoSeedIds.EmployeeCount);
            var person = DemoNameBank.BuildPerson(empIndex);
            var daysAgo = rng.Next(5, 180);
            var filedAt = now.AddDays(-daysAgo);
            var status = statuses[rng.Next(statuses.Length)];
            var id = DemoSeedIds.Named($"complaint-{i}");

            var complaint = new Complaint
            {
                Id = id,
                TenantId = TenantId,
                FiledBy = DemoNameBank.FullName(person.First, person.Middle, person.Last),
                Against = DemoNameBank.JobTitle((empIndex + 3) % DemoNameBank.JobTitles.Length),
                Subject = ComplaintSubjects[i % ComplaintSubjects.Length],
                Description = $"وصف تفصيلي للشكوى رقم {i + 1:D2} — سجل تجريبي للتطوير.",
                FiledAt = filedAt,
                Status = status,
                AssignedTo = DemoSeedIds.IsSupervisor(empIndex)
                    ? DemoSeedIds.AuthUserId(empIndex)
                    : DemoSeedIds.AuthUserId(rng.Next(40)),
                IsAnonymous = i % 7 == 0,
                ReporterProtectedId = i % 7 == 0 ? DemoSeedIds.Named($"whistleblower-{i}") : null,
                Resolution = status is ComplaintStatus.Resolved or ComplaintStatus.Closed
                    ? "تم إغلاق الشكوى بعد التحقيق وفق الإجراءات المعتمدة."
                    : null,
                CreatedAt = filedAt,
                UpdatedAt = now,
            };

            db.Complaints.Add(complaint);
            complaintIds.Add(id);

            var followUpCount = 1 + (i % 3); // ~50 total across 25 complaints
            for (var f = 0; f < followUpCount; f++)
            {
                var performedAt = filedAt.AddDays(f * 3 + 1);
                db.ComplaintFollowUps.Add(new ComplaintFollowUp
                {
                    Id = DemoSeedIds.Named($"complaint-followup-{i}-{f}"),
                    TenantId = TenantId,
                    ComplaintId = id,
                    ActionTaken = f == 0
                        ? "استلام الشكوى وتسجيلها في السجل الرسمي"
                        : f == 1
                            ? "مقابلة الأطراف المعنية وجمع الإفادات"
                            : "مراجعة الأدلة ورفع تقرير أولي",
                    PerformedBy = DemoSeedIds.AuthUserId(rng.Next(40)),
                    PerformedAt = performedAt,
                    CreatedAt = performedAt,
                    UpdatedAt = now,
                });
            }
        }

        await db.SaveChangesAsync(ct);
        return complaintIds;
    }

    private static async Task SeedFindingsAsync(
        ComplianceDbContext db,
        Random rng,
        DateTime now,
        CancellationToken ct)
    {
        var severities = Enum.GetValues<FindingSeverity>();
        var findingStatuses = Enum.GetValues<FindingStatus>();
        var approvalStatuses = Enum.GetValues<ApprovalStatus>();

        for (var i = 0; i < 15; i++)
        {
            var daysAgo = rng.Next(10, 200);
            var created = now.AddDays(-daysAgo);
            var status = findingStatuses[rng.Next(findingStatuses.Length)];
            var findingId = DemoSeedIds.Named($"audit-finding-{i}");

            db.AuditFindings.Add(new AuditFinding
            {
                Id = findingId,
                TenantId = TenantId,
                AuditId = DemoSeedIds.Named($"audit-cycle-{i / 3}"),
                Description = FindingDescriptions[i % FindingDescriptions.Length],
                Severity = severities[rng.Next(severities.Length)],
                Recommendation = "معالجة الملاحظة ووضع خطة تصحيحية خلال المهلة المحددة.",
                Status = status,
                ResolvedAt = status is FindingStatus.Mitigated or FindingStatus.Closed
                    ? created.AddDays(rng.Next(7, 60))
                    : null,
                CreatedAt = created,
                UpdatedAt = now,
            });

            db.ComplianceRecommendations.Add(new ComplianceRecommendation
            {
                Id = DemoSeedIds.Named($"compliance-recommendation-{i}"),
                TenantId = TenantId,
                FindingId = findingId,
                RecommendedAction = $"تنفيذ إجراء تصحيحي للملاحظة رقم {i + 1:D2}",
                DueDate = created.AddDays(30 + rng.Next(0, 60)),
                ResponsibleUnitId = DemoSeedIds.OrgUnitId(1 + (i % 8)),
                Status = approvalStatuses[rng.Next(approvalStatuses.Length)],
                CreatedAt = created,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedDisciplinaryActionsAsync(
        ComplianceDbContext db,
        IReadOnlyList<Guid> complaintIds,
        Random rng,
        DateTime now,
        CancellationToken ct)
    {
        var actionTypes = Enum.GetValues<ActionType>();
        var appealStatuses = Enum.GetValues<AppealStatus>();

        for (var i = 0; i < 20; i++)
        {
            var empIndex = rng.Next(DemoSeedIds.EmployeeCount);
            var daysAgo = rng.Next(15, 240);
            var issuedAt = now.AddDays(-daysAgo);

            db.DisciplinaryActions.Add(new DisciplinaryAction
            {
                Id = DemoSeedIds.Named($"disciplinary-action-{i}"),
                TenantId = TenantId,
                EmployeeId = DemoSeedIds.EmployeeId(empIndex),
                ActionType = actionTypes[rng.Next(actionTypes.Length)],
                IssuedBy = DemoSeedIds.AuthUserId(rng.Next(40)),
                IssuedAt = issuedAt,
                Notes = $"إجراء تأديبي تجريبي رقم {i + 1:D2}",
                AppealStatus = appealStatuses[rng.Next(appealStatuses.Length)],
                ComplaintId = i < complaintIds.Count && i % 2 == 0 ? complaintIds[i] : null,
                CreatedAt = issuedAt,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedLegalCasesAsync(
        ComplianceDbContext db,
        Random rng,
        DateTime now,
        CancellationToken ct)
    {
        var caseStatuses = Enum.GetValues<CaseStatus>();
        var subjects = new[]
        {
            "نزاع عقد عمل",
            "دعوى تعويض إداري",
            "طعن بقرار فصل",
            "منازعة بدل مستحقات",
            "قضية إخلال بالالتزامات الوظيفية",
        };

        for (var i = 0; i < 5; i++)
        {
            var daysAgo = rng.Next(30, 400);
            var created = now.AddDays(-daysAgo);

            db.LegalCases.Add(new LegalCase
            {
                Id = DemoSeedIds.Named($"legal-case-{i}"),
                TenantId = TenantId,
                CaseNumber = $"LEG-2026-{i + 1:D3}",
                Subject = subjects[i],
                Parties = "المؤسسة العامة / الموظف المعني",
                Status = caseStatuses[rng.Next(caseStatuses.Length)],
                CourtDetails = "محكمة القضاء الإداري — دمشق",
                AssignedTo = DemoSeedIds.AuthUserId(rng.Next(10)),
                CreatedAt = created,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedDsarRequestsAsync(
        ComplianceDbContext db,
        Random rng,
        DateTime now,
        CancellationToken ct)
    {
        var types = Enum.GetValues<DsarRequestType>();
        var statuses = Enum.GetValues<DsarRequestStatus>();

        for (var i = 0; i < 8; i++)
        {
            var empIndex = rng.Next(DemoSeedIds.EmployeeCount);
            var daysAgo = rng.Next(3, 90);
            var requestedAt = now.AddDays(-daysAgo);
            var status = statuses[rng.Next(statuses.Length)];

            db.DsarRequests.Add(new DsarRequest
            {
                Id = DemoSeedIds.Named($"dsar-request-{i}"),
                TenantId = TenantId,
                RequestType = types[i % types.Length],
                Status = status,
                SubjectEmployeeId = DemoSeedIds.EmployeeId(empIndex),
                RequestedAt = requestedAt,
                CompletedAt = status is DsarRequestStatus.Fulfilled or DsarRequestStatus.Rejected
                    ? requestedAt.AddDays(rng.Next(2, 20))
                    : null,
                Notes = $"طلب حقوق ذات تجريبي رقم {i + 1:D2}",
                CreatedAt = requestedAt,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedPhase3HelpersAsync(
        ComplianceDbContext db,
        Random rng,
        DateTime now,
        CancellationToken ct)
    {
        if (!await db.AuditLogs.AnyAsync(ct))
        {
            for (var i = 0; i < 8; i++)
            {
                var created = now.AddDays(-rng.Next(1, 60));
                db.AuditLogs.Add(new EntityAuditLog
                {
                    Id = DemoSeedIds.Named($"compliance-audit-log-{i}"),
                    TenantId = TenantId,
                    EntityType = i % 2 == 0 ? nameof(Complaint) : nameof(DisciplinaryAction),
                    EntityId = DemoSeedIds.Named(i % 2 == 0 ? $"complaint-{i % 25}" : $"disciplinary-action-{i % 20}"),
                    Action = i % 3 == 0 ? "Created" : "Updated",
                    ChangedFields = """{"Status":"Open"}""",
                    UserId = DemoSeedIds.AuthUserId(rng.Next(40)),
                    CreatedAt = created,
                    UpdatedAt = now,
                });
            }
        }

        if (!await db.ExportJobs.AnyAsync(ct))
        {
            for (var i = 0; i < 3; i++)
            {
                var created = now.AddDays(-rng.Next(1, 30));
                var completed = i < 2;
                db.ExportJobs.Add(new ExportJob
                {
                    Id = DemoSeedIds.Named($"compliance-export-job-{i}"),
                    TenantId = TenantId,
                    ServiceKey = "compliance",
                    ExportType = "complaints.csv",
                    Status = completed ? ExportJobStatus.Completed : ExportJobStatus.Pending,
                    RequestedByUserId = DemoSeedIds.AuthUserId(rng.Next(40)),
                    FilterJson = "{}",
                    AuthSnapshotJson = "{}",
                    TemplateCode = ExportJobTemplates.ComplianceComplaintsCsv,
                    TemplateVersion = ExportJobTemplates.Version1,
                    TotalCount = completed ? 25 : 0,
                    ProcessedCount = completed ? 25 : 0,
                    StartedAt = completed ? created : null,
                    CompletedAt = completed ? created.AddMinutes(5) : null,
                    CreatedAt = created,
                    UpdatedAt = now,
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
