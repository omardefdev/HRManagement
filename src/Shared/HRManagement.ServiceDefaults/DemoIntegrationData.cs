using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Exports;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.ServiceDefaults.Seeding;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// Cross-service demo rows (org snapshots, audit, export jobs, inbox receipts)
/// that satellite services can insert independently.
/// </summary>
public static class DemoIntegrationData
{
    private static Guid TenantId => AcceptanceSeedExecution.TenantId;

    private static readonly (string Code, string NameAr, int ParentIndex)[] OrgDefs =
    [
        ("ROOT", "المؤسسة العامة", -1),
        ("HR", "شعبة الموارد البشرية", 0),
        ("FIN", "الدائرة المالية", 0),
        ("LEG", "الدائرة القانونية", 0),
        ("IT", "دائرة تقانة المعلومات", 0),
        ("AUD", "دائرة الرقابة الداخلية", 0),
        ("ADM", "الدائرة الإدارية", 0),
        ("PLN", "دائرة التخطيط والتطوير", 0),
        ("PR", "دائرة العلاقات العامة", 0),
        ("DG", "مكتب المدير العام", 0),
        ("ARC", "شعبة الأرشيف", 0),
        ("HR-REC", "قسم التعيينات", 1),
        ("HR-PAY", "قسم الرواتب", 1),
        ("HR-TRN", "قسم التدريب", 1),
        ("HR-ATT", "قسم الحضور والانضباط", 1),
        ("FIN-ACC", "قسم المحاسبة", 2),
        ("FIN-BUD", "قسم الموازنة", 2),
        ("FIN-PUR", "قسم المشتريات", 2),
        ("IT-OPS", "قسم تشغيل النظم", 4),
        ("IT-SEC", "قسم أمن المعلومات", 4),
        ("IT-SUP", "قسم الدعم الفني", 4),
        ("ADM-LOG", "قسم اللوجستيات", 6),
        ("ADM-FAC", "قسم المرافق", 6),
        ("LEG-CON", "قسم العقود", 3),
        ("LEG-LIT", "قسم التقاضي", 3),
        ("AUD-INT", "قسم التدقيق الداخلي", 5),
        ("PLN-STR", "قسم التخطيط الاستراتيجي", 7),
        ("PLN-KPI", "قسم مؤشرات الأداء", 7),
        ("PR-MED", "قسم الإعلام", 8),
        ("ARC-DIG", "قسم الأرشفة الرقمية", 10),
    ];

    public static IReadOnlyList<OrgUnitSnapshot> BuildOrgUnitSnapshots()
    {
        if (AcceptanceSeedExecution.CurrentAgency is { } agency)
        {
            return agency.OrgUnits
                .Select(o => new OrgUnitSnapshot
                {
                    Id = o.Id,
                    TenantId = o.TenantId,
                    ParentId = o.ParentId,
                    MaterializedPath = o.MaterializedPath,
                    Code = o.Code,
                    NameAr = o.NameAr,
                })
                .ToArray();
        }

        var list = new List<OrgUnitSnapshot>(DemoSeedIds.OrgUnitCount);
        for (var i = 0; i < DemoSeedIds.OrgUnitCount && i < OrgDefs.Length; i++)
        {
            var (code, nameAr, parentIndex) = OrgDefs[i];
            var id = DemoSeedIds.OrgUnitId(i);
            var segments = new List<Guid> { id };
            var p = parentIndex;
            while (p >= 0)
            {
                segments.Insert(0, DemoSeedIds.OrgUnitId(p));
                p = OrgDefs[p].ParentIndex;
            }

            list.Add(new OrgUnitSnapshot
            {
                Id = id,
                TenantId = TenantId,
                ParentId = parentIndex < 0 ? null : DemoSeedIds.OrgUnitId(parentIndex),
                MaterializedPath = "/" + string.Join("/", segments.Select(g => g.ToString("D"))) + "/",
                Code = code,
                NameAr = nameAr,
            });
        }

        return list;
    }

    public static IEnumerable<EntityAuditLog> BuildAuditLogs(int count, string entityTypePrefix, DateTime now)
    {
        if (AcceptanceSeedExecution.IsAcceptance)
            yield break;

        for (var i = 0; i < count; i++)
        {
            yield return new EntityAuditLog
            {
                Id = DemoSeedIds.Named($"audit-{entityTypePrefix}-{i}"),
                TenantId = TenantId,
                EntityType = $"{entityTypePrefix}",
                EntityId = DemoSeedIds.EmployeeId(i % DemoSeedIds.EmployeeCount),
                Action = i % 3 == 0 ? "Created" : i % 3 == 1 ? "Updated" : "Deleted",
                ChangedFields = "{\"demo\":true}",
                UserId = DemoSeedIds.AuthUserId(i % 40),
                CreatedAt = now.AddDays(-i),
                UpdatedAt = now.AddDays(-i),
            };
        }
    }

    public static IEnumerable<ExportJob> BuildExportJobs(int count, string serviceKey, string templateCode, DateTime now)
    {
        for (var i = 0; i < count; i++)
        {
            var completed = i % 2 == 0;
            yield return new ExportJob
            {
                Id = DemoSeedIds.Named($"export-{serviceKey}-{i}"),
                TenantId = TenantId,
                ServiceKey = serviceKey,
                ExportType = "Csv",
                Status = completed ? ExportJobStatus.Completed : ExportJobStatus.Pending,
                RequestedByUserId = DemoSeedIds.AuthUserId(i % 10),
                FilterJson = "{}",
                AuthSnapshotJson = "{}",
                TemplateCode = templateCode,
                TemplateVersion = ExportJobTemplates.Version1,
                TotalCount = 100 + i * 10,
                ProcessedCount = completed ? 100 + i * 10 : 0,
                StartedAt = completed ? now.AddHours(-i) : null,
                CompletedAt = completed ? now.AddHours(-i).AddMinutes(5) : null,
                CreatedAt = now.AddHours(-i),
                UpdatedAt = now,
            };
        }
    }

    public static IEnumerable<IntegrationMessageReceipt> BuildMessageReceipts(int count, string consumerType, DateTime now)
    {
        if (AcceptanceSeedExecution.IsAcceptance)
            yield break;

        for (var i = 0; i < count; i++)
        {
            yield return new IntegrationMessageReceipt
            {
                Id = DemoSeedIds.Named($"receipt-{consumerType}-{i}"),
                TenantId = TenantId,
                MessageId = $"demo-msg-{consumerType}-{i:D4}",
                ConsumerType = consumerType,
                ReceivedAt = now.AddMinutes(-i * 7),
                ProcessedAt = now.AddMinutes(-i * 7 + 1),
                Outcome = "Processed",
                CreatedAt = now.AddMinutes(-i * 7),
                UpdatedAt = now,
            };
        }
    }
}
