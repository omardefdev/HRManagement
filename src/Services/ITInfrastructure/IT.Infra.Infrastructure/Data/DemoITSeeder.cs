using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Seeding;
using HRManagement.SharedKernel.Tenancy;
using IT.Infrastructure.Domain.Entities;
using IT.Infrastructure.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace IT.Infra.Infrastructure.Data;

/// <summary>Populates demo servers, user devices, backups, incidents, tickets, and permissions.</summary>
public static class DemoITSeeder
{
    private static Guid TenantId => AcceptanceSeedExecution.TenantId;

    private static readonly string[] ServerPurposes =
    [
        "تطبيقات الموارد البشرية", "قاعدة بيانات رئيسية", "خادم ملفات",
        "بوابة ويب", "خادم بريد", "مراقبة ومقاييس", "نسخ احتياطي",
        "بيئة اختبار", "دليل نشط", "خادم طباعة", "VPN",
        "تخزين مركزي", "خدمات تكامل", "سجل أحداث", "خادم تطوير",
    ];

    private static readonly string[] OperatingSystems =
    [
        "Ubuntu 22.04 LTS", "Windows Server 2022", "RHEL 9", "Debian 12",
    ];

    private static readonly string[] Locations =
    [
        "مركز بيانات دمشق", "غرفة سيرفرات الطابق 3", "موقع احتياطي حمص", "رف افتراضي سحابي",
    ];

    private static readonly string[] IncidentTitles =
    [
        "محاولة دخول غير مصرح", "برمجية خبيثة محتملة", "تسريب بيانات مشتبه",
        "هجوم حجب خدمة", "حساب مخترق", "ثغرة في خادم ويب",
        "وصول غير مصرح لملفات", "تصيد احتيالي موجّه", "جهاز غير محدث أمنياً",
        "تنبيه نظام كشف التسلل",
    ];

    private static readonly string[] TicketSummaries =
    [
        "تعطل طابعة الشبكة", "طلب إعادة تعيين كلمة المرور", "بطء في النظام",
        "تثبيت برنامج جديد", "مشكلة اتصال بالشبكة", "شاشة زرقاء",
        "طلب صلاحيات نظام", "تهيئة جهاز موظف جديد", "مشكلة بريد إلكتروني",
        "تحديث نظام التشغيل", "استعادة ملف محذوف", "مشكلة VPN",
    ];

    private static readonly string[] Systems =
    [
        "HR Core", "Payroll", "Archive", "Auth Directory", "File Storage",
        "Email", "VPN", "ERP Finance", "Helpdesk", "Monitoring",
    ];

    private static readonly string[] RoleSets =
    [
        "User", "User,Reporter", "Admin", "Viewer", "Operator",
        "User,Approver", "Auditor", "Support",
    ];

    public static async Task SeedAsync(
        ITInfraDbContext db,
        CancellationToken ct = default)
    {
        if (await db.ServerDevices.AnyAsync(ct))
            return;

        var rng = AcceptanceSeedExecution.CreateRandom("it-infra");
        var now = AcceptanceSeedExecution.UtcNow;

        for (var i = 0; i < 15; i++)
        {
            db.ServerDevices.Add(new ServerDevice
            {
                TenantId = TenantId,
                Hostname = AcceptanceSeedExecution.CurrentAgency is { } agency
                    ? $"srv-{agency.Code.ToLowerInvariant()}-{(i + 1):D2}.acceptance.local"
                    : $"srv-demo-{(i + 1):D2}.hr.gov.sy",
                IpAddress = $"10.10.{(i / 5) + 1}.{10 + i}",
                Os = OperatingSystems[i % OperatingSystems.Length],
                Purpose = ServerPurposes[i],
                Location = Locations[i % Locations.Length],
                Status = i == 14 ? ServerStatus.Decommissioned : ServerStatus.Active,
                CreatedAt = now.AddDays(-rng.Next(90, 900)),
                UpdatedAt = now,
            });
        }

        var deviceTypes = Enum.GetValues<DeviceType>();
        var deviceStatuses = Enum.GetValues<DeviceStatus>();
        for (var i = 0; i < 300; i++)
        {
            var status = deviceStatuses[i % deviceStatuses.Length];
            db.UserDevices.Add(new UserDevice
            {
                TenantId = TenantId,
                AssetTag = $"IT-DEV-{i + 1:D5}",
                AssignedTo = status == DeviceStatus.Retired
                    ? null
                    : DemoSeedIds.EmployeeId(i % DemoSeedIds.EmployeeCount),
                DeviceType = deviceTypes[i % deviceTypes.Length],
                SerialNumber = $"SN{rng.Next(100000, 999999)}{i:D4}",
                Status = status,
                CreatedAt = now.AddDays(-rng.Next(10, 700)),
                UpdatedAt = now,
            });
        }

        var backupStatuses = Enum.GetValues<BackupStatus>();
        for (var i = 0; i < 30; i++)
        {
            var lastRun = now.AddHours(-rng.Next(1, 72));
            db.BackupJobs.Add(new BackupJob
            {
                TenantId = TenantId,
                Target = $"backup-target-{(i % 15) + 1:D2} / {ServerPurposes[i % ServerPurposes.Length]}",
                Schedule = i % 3 == 0 ? "0 2 * * *" : i % 3 == 1 ? "0 */6 * * *" : "0 1 * * 0",
                LastRunAt = lastRun,
                Status = backupStatuses[i % backupStatuses.Length],
                CreatedAt = now.AddDays(-rng.Next(30, 400)),
                UpdatedAt = now,
            });
        }

        var severities = Enum.GetValues<Severity>();
        var ticketStatuses = Enum.GetValues<TicketStatus>();
        for (var i = 0; i < 10; i++)
        {
            var reported = now.AddDays(-rng.Next(1, 90));
            var status = ticketStatuses[i % ticketStatuses.Length];
            db.SecurityIncidents.Add(new SecurityIncident
            {
                TenantId = TenantId,
                Title = IncidentTitles[i],
                Description = $"تفاصيل الحادثة الأمنية: {IncidentTitles[i]}",
                Severity = severities[i % severities.Length],
                ReportedAt = reported,
                Status = status,
                HandledBy = status is TicketStatus.Open ? null : DemoSeedIds.EmployeeId(i % 20),
                CreatedAt = reported,
                UpdatedAt = now,
            });
        }

        var provisionStatuses = Enum.GetValues<ProvisioningStatus>();
        for (var i = 0; i < 100; i++)
        {
            var created = now.AddDays(-rng.Next(1, 180));
            var status = ticketStatuses[i % ticketStatuses.Length];
            db.ITSupportTickets.Add(new ITSupportTicket
            {
                TenantId = TenantId,
                ReportedBy = DemoSeedIds.EmployeeId(i % DemoSeedIds.EmployeeCount),
                IssueSummary = TicketSummaries[i % TicketSummaries.Length],
                Description = $"تذكرة دعم رقم {i + 1}: {TicketSummaries[i % TicketSummaries.Length]}",
                Status = status,
                AssignedTo = status is TicketStatus.Open
                    ? null
                    : DemoSeedIds.EmployeeId((i + 5) % 40),
                ResolvedAt = status is TicketStatus.Resolved or TicketStatus.Closed
                    ? created.AddDays(rng.Next(1, 10))
                    : null,
                ProvisioningStatus = provisionStatuses[i % provisionStatuses.Length],
                ProvisioningError = i % 17 == 0 ? "فشل مزامنة الدليل" : null,
                ProvisionedAt = i % 17 == 0 ? null : created.AddHours(rng.Next(1, 48)),
                CreatedAt = created,
                UpdatedAt = now,
            });
        }

        for (var i = 0; i < 200; i++)
        {
            var granted = now.AddDays(-rng.Next(10, 600));
            var revoked = i % 8 == 0 ? granted.AddDays(rng.Next(30, 200)) : (DateTime?)null;
            db.SystemPermissionRecords.Add(new SystemPermissionRecord
            {
                TenantId = TenantId,
                UserId = DemoSeedIds.EmployeeId(i % DemoSeedIds.EmployeeCount),
                System = Systems[i % Systems.Length],
                Roles = RoleSets[i % RoleSets.Length],
                GrantedAt = granted,
                RevokedAt = revoked,
                CreatedAt = granted,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
