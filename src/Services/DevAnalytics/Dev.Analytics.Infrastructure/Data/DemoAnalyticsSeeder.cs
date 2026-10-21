using Dev.Analytics.Domain.Entities;
using Dev.Analytics.Domain.Enums;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Seeding;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Exports;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Dev.Analytics.Infrastructure.Data;

/// <summary>
/// Populates Dev.Analytics with demo dashboards, reports, KPIs, ETL jobs, and read models.
/// Idempotent: skips when dashboards already exist.
/// </summary>
public static class DemoAnalyticsSeeder
{
    private static Guid TenantId => AcceptanceSeedExecution.TenantId;
    private static string ScopedCode(string code) => AcceptanceSeedExecution.CurrentAgency is { } agency
        ? $"{agency.Code}-{code}"
        : code;

    private static readonly string[] DashboardNames =
    [
        "لوحة الموارد البشرية التنفيذية",
        "مؤشرات الرواتب والتعويضات",
        "متابعة الشكاوى والامتثال",
        "مؤشرات التدريب والتطوير",
        "نظرة عامة على القوى العاملة",
    ];

    private static readonly string[] ReportTitles =
    [
        "تقرير الحضور الأسبوعي",
        "ملخص الرواتب الشهري",
        "تقرير التعيينات الجديدة",
        "تحليل معدل الدوران",
        "تقرير الإجازات المعتمدة",
        "مؤشرات الأداء حسب الوحدة",
        "تقرير الشكاوى المفتوحة",
        "ملخص التدريب المكتمل",
        "تقرير العقود المنتهية",
        "تحليل الهيكل التنظيمي",
        "تقرير التأمين الصحي",
        "ملخص الأصول المخصصة",
        "تقرير الإجراءات التأديبية",
        "تحليل فجوات الكفاءات",
        "تقرير التنقلات الداخلية",
        "ملخص طلبات DSAR",
        "تقرير الساعات الإضافية",
        "تحليل توزيع الدرجات الوظيفية",
        "تقرير التقاعد المتوقع",
        "ملخص الجاهزية التشغيلية",
    ];

    private static readonly (string Code, string Name, decimal Target)[] KpiDefs =
    [
        ("KPI-HEADCOUNT", "إجمالي القوى العاملة", 500m),
        ("KPI-VACANCY", "نسبة الشواغر", 5m),
        ("KPI-TURNOVER", "معدل الدوران السنوي", 8m),
        ("KPI-ATTEND", "نسبة الالتزام بالدوام", 96m),
        ("KPI-LEAVE", "متوسط أيام الإجازة المستخدمة", 18m),
        ("KPI-TRAIN", "نسبة إكمال التدريب", 85m),
        ("KPI-PAY-OT", "نسبة الساعات الإضافية", 12m),
        ("KPI-COMPLAINT", "متوسط زمن إغلاق الشكوى (يوم)", 21m),
        ("KPI-AUDIT", "نسبة إغلاق الملاحظات الرقابية", 90m),
        ("KPI-DSAR", "متوسط زمن الاستجابة لـ DSAR (يوم)", 14m),
        ("KPI-PROMOTION", "نسبة الترقيات الداخلية", 10m),
        ("KPI-FEMALE", "نسبة مشاركة الإناث", 40m),
        ("KPI-CONTRACT", "نسبة العقود النشطة", 98m),
        ("KPI-HEALTH", "نسبة اكتمال الفحوص الطبية", 92m),
        ("KPI-ASSET", "نسبة الأصول الموثقة", 95m),
    ];

    private static readonly string[] EtlNames =
    [
        "مزامنة الموظفين اليومية",
        "تجميع أحداث الرواتب",
        "تحديث مؤشرات الحضور",
        "تحميل بيانات التدريب",
        "مزامنة الشكاوى والامتثال",
        "تحديث نماذج القراءة التنظيمية",
        "استيراد العقود والإجازات",
        "تجميع أحداث التنقلات",
        "تحديث لوحة القوى العاملة",
        "أرشفة أحداث التحليلات القديمة",
    ];

    private static readonly string[] ReadModelNames =
    [
        "rm_workforce_summary",
        "rm_payroll_monthly",
        "rm_compliance_open_items",
        "rm_training_completion",
    ];

    public static async Task SeedAsync(AnalyticsDbContext db, CancellationToken ct = default)
    {
        if (await db.Dashboards.AnyAsync(ct))
            return;

        var rng = AcceptanceSeedExecution.CreateRandom("analytics");
        var now = AcceptanceSeedExecution.UtcNow;

        await SeedDashboardsAsync(db, rng, now, ct);
        await SeedReportsAsync(db, rng, now, ct);
        await SeedKpisAsync(db, rng, now, ct);
        await SeedEtlJobsAsync(db, rng, now, ct);
        await SeedReadModelsAndSchedulesAsync(db, rng, now, ct);
        await SeedPhase3HelpersAsync(db, rng, now, ct);
    }

    private static async Task SeedDashboardsAsync(
        AnalyticsDbContext db,
        Random rng,
        DateTime now,
        CancellationToken ct)
    {
        for (var i = 0; i < DashboardNames.Length; i++)
        {
            var created = now.AddDays(-rng.Next(10, 120));
            db.Dashboards.Add(new Dashboard
            {
                Id = DemoSeedIds.Named($"dashboard-{i}"),
                TenantId = TenantId,
                Name = DashboardNames[i],
                Widgets = $$"""[{"type":"kpi","order":{{i + 1}}},{"type":"chart","order":{{i + 2}}}]""",
                OwnerId = DemoSeedIds.AuthUserId(rng.Next(40)),
                CreatedAt = created,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedReportsAsync(
        AnalyticsDbContext db,
        Random rng,
        DateTime now,
        CancellationToken ct)
    {
        var reportTypes = Enum.GetValues<ReportType>();

        for (var i = 0; i < ReportTitles.Length; i++)
        {
            var daysAgo = rng.Next(1, 90);
            var generatedAt = now.AddDays(-daysAgo);
            var reportType = reportTypes[i % reportTypes.Length];

            db.Reports.Add(new Report
            {
                Id = DemoSeedIds.Named($"report-{i}"),
                TenantId = TenantId,
                Title = ReportTitles[i],
                ReportType = reportType,
                Parameters = $$"""{"period":"{{reportType}}","orgUnitIndex":{{i % 9}}}""",
                GeneratedAt = generatedAt,
                GeneratedBy = DemoSeedIds.AuthUserId(rng.Next(40)),
                Url = $"/reports/demo/{i + 1:D2}",
                ScopeGrain = i % 3 == 0 ? "Tenant" : "Branch",
                SourceEvent = i % 2 == 0 ? "PayrollProcessed" : "EmployeeUpdated",
                LastFingerprint = DemoSeedIds.Named($"report-fp-{i}").ToString("N")[..16],
                CreatedAt = generatedAt,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedKpisAsync(
        AnalyticsDbContext db,
        Random rng,
        DateTime now,
        CancellationToken ct)
    {
        for (var i = 0; i < KpiDefs.Length; i++)
        {
            var (code, name, target) = KpiDefs[i];
            if (code == "KPI-HEADCOUNT" && AcceptanceSeedExecution.IsAcceptance)
                target = DemoSeedIds.EmployeeCount;
            var measuredAt = now.AddDays(-rng.Next(0, 14));
            var variance = (decimal)(rng.NextDouble() * 0.2 - 0.05);

            db.KPIs.Add(new KPI
            {
                Id = DemoSeedIds.Named($"kpi-{code.ToLowerInvariant()}"),
                TenantId = TenantId,
                Code = ScopedCode(code),
                Name = name,
                Definition = $"تعريف تجريبي للمؤشر: {name}",
                TargetValue = target,
                CurrentValue = Math.Round(target * (1 + variance), 2),
                MeasuredAt = measuredAt,
                CreatedAt = measuredAt.AddDays(-30),
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedEtlJobsAsync(
        AnalyticsDbContext db,
        Random rng,
        DateTime now,
        CancellationToken ct)
    {
        var cronHints = new[]
        {
            "0 2 * * *",
            "0 */6 * * *",
            "30 1 * * *",
            "0 3 * * 0",
            "15 */4 * * *",
        };

        for (var i = 0; i < EtlNames.Length; i++)
        {
            var lastRun = now.AddHours(-rng.Next(1, 72));
            var ok = i % 5 != 4;

            db.ETLJobs.Add(new ETLJob
            {
                Id = DemoSeedIds.Named($"etl-job-{i}"),
                TenantId = TenantId,
                Name = EtlNames[i],
                Schedule = cronHints[i % cronHints.Length],
                LastRun = lastRun,
                Status = ok ? JobStatus.Ok : JobStatus.Failed,
                Log = ok
                    ? "اكتمل التشغيل بنجاح — سجل تجريبي."
                    : "فشل جزئي في المصدر — يعاد المحاولة لاحقاً.",
                CreatedAt = lastRun.AddDays(-rng.Next(10, 90)),
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedReadModelsAndSchedulesAsync(
        AnalyticsDbContext db,
        Random rng,
        DateTime now,
        CancellationToken ct)
    {
        var readModelIds = new List<Guid>(ReadModelNames.Length);

        for (var i = 0; i < ReadModelNames.Length; i++)
        {
            var id = DemoSeedIds.Named($"read-model-{i}");
            readModelIds.Add(id);
            var refreshed = now.AddHours(-rng.Next(1, 48));

            db.ReadModels.Add(new ReadModel
            {
                Id = id,
                TenantId = TenantId,
                Name = ReadModelNames[i],
                Schema = $$"""{"version":1,"name":"{{ReadModelNames[i]}}"}""",
                LastRefreshed = refreshed,
                LastFingerprint = DemoSeedIds.Named($"rm-fp-{i}").ToString("N")[..16],
                SourceEventName = i % 2 == 0 ? "EmployeeUpdated" : "PayrollProcessed",
                CreatedAt = refreshed.AddDays(-60),
                UpdatedAt = now,
            });
        }

        var crons = new[]
        {
            "0 * * * *",
            "0 */2 * * *",
            "15 0 * * *",
            "0 4 * * *",
            "30 */6 * * *",
        };

        for (var i = 0; i < 5; i++)
        {
            var lastRun = now.AddHours(-rng.Next(1, 24));
            db.DataRefreshSchedules.Add(new DataRefreshSchedule
            {
                Id = DemoSeedIds.Named($"data-refresh-schedule-{i}"),
                TenantId = TenantId,
                TargetReadModelId = readModelIds[i % readModelIds.Count],
                CronExpression = crons[i],
                LastRunAt = lastRun,
                NextRunAt = lastRun.AddHours(i == 0 ? 1 : 2 + i),
                CreatedAt = lastRun.AddDays(-30),
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedPhase3HelpersAsync(
        AnalyticsDbContext db,
        Random rng,
        DateTime now,
        CancellationToken ct)
    {
        if (!await db.AuditLogs.AnyAsync(ct))
        {
            for (var i = 0; i < 5; i++)
            {
                var created = now.AddDays(-rng.Next(1, 40));
                db.AuditLogs.Add(new EntityAuditLog
                {
                    Id = DemoSeedIds.Named($"analytics-audit-log-{i}"),
                    TenantId = TenantId,
                    EntityType = i % 2 == 0 ? nameof(Report) : nameof(KPI),
                    EntityId = DemoSeedIds.Named(i % 2 == 0 ? $"report-{i}" : $"kpi-{KpiDefs[i % KpiDefs.Length].Code.ToLowerInvariant()}"),
                    Action = "Updated",
                    ChangedFields = """{"MeasuredAt":"refreshed"}""",
                    UserId = DemoSeedIds.AuthUserId(rng.Next(40)),
                    CreatedAt = created,
                    UpdatedAt = now,
                });
            }
        }

        if (!await db.ExportJobs.AnyAsync(ct))
        {
            for (var i = 0; i < 2; i++)
            {
                var created = now.AddDays(-rng.Next(1, 20));
                db.ExportJobs.Add(new ExportJob
                {
                    Id = DemoSeedIds.Named($"analytics-export-job-{i}"),
                    TenantId = TenantId,
                    ServiceKey = "analytics",
                    ExportType = "workforce.csv",
                    Status = i == 0 ? ExportJobStatus.Completed : ExportJobStatus.Pending,
                    RequestedByUserId = DemoSeedIds.AuthUserId(rng.Next(40)),
                    FilterJson = "{}",
                    AuthSnapshotJson = "{}",
                    TemplateCode = ExportJobTemplates.AnalyticsWorkforceCsv,
                    TemplateVersion = ExportJobTemplates.Version1,
                    TotalCount = i == 0 ? 500 : 0,
                    ProcessedCount = i == 0 ? 500 : 0,
                    StartedAt = i == 0 ? created : null,
                    CompletedAt = i == 0 ? created.AddMinutes(8) : null,
                    CreatedAt = created,
                    UpdatedAt = now,
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
