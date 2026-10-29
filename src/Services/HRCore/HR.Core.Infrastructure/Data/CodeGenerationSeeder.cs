using System.Text.Json;
using System.Text.Json.Serialization;
using HR.Core.Domain.Entities.CodeGeneration;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Data;

public static class CodeGenerationSeeder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task SeedAsync(HRCoreDbContext db, CancellationToken ct = default)
    {
        if (!await db.CodePolicyTemplates.AnyAsync(ct))
            await SeedNationalDefaultsAsync(db, ct);

        await SeedPrivilegedAssetTagIfMissingAsync(db, ct);
        await SeedInventorySkuIfMissingAsync(db, ct);
        await SeedJobCatalogEntryIfMissingAsync(db, ct);
        await SeedAdministrativeDecisionIfMissingAsync(db, ct);
        await SeedRecruitmentCompetitionIfMissingAsync(db, ct);
    }

    /// <summary>HCM-031 (PLAN-31): CMP-YYYY-00001 for recruitment competitions.</summary>
    private static async Task SeedRecruitmentCompetitionIfMissingAsync(HRCoreDbContext db, CancellationToken ct)
    {
        if (await db.CodePolicyTemplates.AnyAsync(t => t.EntityKind == CodeEntityKind.RecruitmentCompetition, ct))
            return;

        await SeedPolicyAsync(db, CodeEntityKind.RecruitmentCompetition, "ترميز مسابقات التوظيف", "CMP",
            CodeSequenceReset.Yearly,
            """
            [
              {"kind":"Literal","value":"CMP-"},
              {"kind":"Date","value":"YYYY"},
              {"kind":"Literal","value":"-"},
              {"kind":"Sequence","padWidth":5}
            ]
            """, ct);
    }

    private static async Task SeedNationalDefaultsAsync(HRCoreDbContext db, CancellationToken ct)
    {
        await SeedPolicyAsync(db, CodeEntityKind.OrgUnit, "ترميز الوحدات التنظيمية", "OU",
            CodeSequenceReset.PerScope,
            """
            [
              {"kind":"Field","value":"ParentCode"},
              {"kind":"Literal","value":"."},
              {"kind":"Sequence","padWidth":2,"scopeFields":["ParentCode"]}
            ]
            """, ct);

        await SeedPolicyAsync(db, CodeEntityKind.FundedPosition, "ترميز المناصب الممولة", "POS",
            CodeSequenceReset.PerScope,
            """
            [
              {"kind":"Field","value":"OrgUnitCode"},
              {"kind":"Literal","value":"-"},
              {"kind":"Field","value":"GradeCode"},
              {"kind":"Literal","value":"-"},
              {"kind":"Sequence","padWidth":3,"scopeFields":["OrgUnitCode","GradeCode","BudgetYear"]}
            ]
            """, ct);

        await SeedPolicyAsync(db, CodeEntityKind.AssetTag, "رقم العهدة", "AH",
            CodeSequenceReset.Yearly,
            """
            [
              {"kind":"Literal","value":"AH-"},
              {"kind":"Date","value":"YYYY"},
              {"kind":"Literal","value":"-"},
              {"kind":"Sequence","padWidth":6}
            ]
            """, ct);

        await SeedPolicyAsync(db, CodeEntityKind.ItAssetTag, "رقم أصل IT", "IT",
            CodeSequenceReset.Yearly,
            """
            [
              {"kind":"Literal","value":"IT-"},
              {"kind":"Date","value":"YYYY"},
              {"kind":"Literal","value":"-"},
              {"kind":"Sequence","padWidth":6}
            ]
            """, ct);

        await SeedPolicyAsync(db, CodeEntityKind.ArchiveReference, "مرجع الأرشيف", "ARC",
            CodeSequenceReset.Yearly,
            """
            [
              {"kind":"Literal","value":"ARC-"},
              {"kind":"Field","value":"EntityPrefix"},
              {"kind":"Literal","value":"-"},
              {"kind":"Date","value":"YYYY"},
              {"kind":"Literal","value":"-"},
              {"kind":"Sequence","padWidth":5,"scopeFields":["EntityPrefix"]}
            ]
            """, ct);

        await SeedPolicyAsync(db, CodeEntityKind.LegalCase, "رقم القضية", "LC",
            CodeSequenceReset.Yearly,
            """
            [
              {"kind":"Literal","value":"LC-"},
              {"kind":"Date","value":"YYYY"},
              {"kind":"Literal","value":"-"},
              {"kind":"Sequence","padWidth":5}
            ]
            """, ct);

        await SeedPolicyAsync(db, CodeEntityKind.ServiceTransaction, "مرجع معاملة خدمة", "SRV",
            CodeSequenceReset.Yearly,
            """
            [
              {"kind":"Literal","value":"SRV-"},
              {"kind":"Field","value":"TypeCode"},
              {"kind":"Literal","value":"-"},
              {"kind":"Date","value":"YYYY"},
              {"kind":"Literal","value":"-"},
              {"kind":"Sequence","padWidth":5,"scopeFields":["TypeCode"]}
            ]
            """, ct);

        await SeedPolicyAsync(db, CodeEntityKind.CitizenReference, "مرجع طلب مواطن", "CIT",
            CodeSequenceReset.Yearly,
            """
            [
              {"kind":"Literal","value":"CIT-"},
              {"kind":"Date","value":"YYYY"},
              {"kind":"Literal","value":"-"},
              {"kind":"Sequence","padWidth":6}
            ]
            """, ct);

        await SeedPolicyAsync(db, CodeEntityKind.IdCardSerial, "الرقم التسلسلي للبطاقة", "ID",
            CodeSequenceReset.Yearly,
            """
            [
              {"kind":"Literal","value":"ID-"},
              {"kind":"Date","value":"YYYY"},
              {"kind":"Literal","value":"-"},
              {"kind":"Sequence","padWidth":7}
            ]
            """, ct);

        await SeedPolicyAsync(db, CodeEntityKind.CostCenter, "مركز التكلفة", "CC",
            CodeSequenceReset.Never,
            """
            [
              {"kind":"Field","value":"OrgUnitCode"}
            ]
            """, ct);

        await SeedPolicyAsync(db, CodeEntityKind.InventorySku, "رمز المخزون", "SKU",
            CodeSequenceReset.Yearly,
            """
            [
              {"kind":"Literal","value":"SKU-"},
              {"kind":"Date","value":"YYYY"},
              {"kind":"Literal","value":"-"},
              {"kind":"Sequence","padWidth":6}
            ]
            """, ct);

        await SeedPolicyAsync(db, CodeEntityKind.RecruitmentRequisition, "ترميز طلبات الشاغر", "REQ",
            CodeSequenceReset.Yearly,
            """
            [
              {"kind":"Literal","value":"REQ-"},
              {"kind":"Date","value":"YYYY"},
              {"kind":"Literal","value":"-"},
              {"kind":"Sequence","padWidth":5,"scopeFields":["BudgetYear"]}
            ]
            """, ct);

        await SeedPolicyAsync(db, CodeEntityKind.OfferLetter, "ترميز خطابات العرض", "OFF",
            CodeSequenceReset.Yearly,
            """
            [
              {"kind":"Literal","value":"OFF-"},
              {"kind":"Date","value":"YYYY"},
              {"kind":"Literal","value":"-"},
              {"kind":"Sequence","padWidth":5}
            ]
            """, ct);

        await SeedPolicyAsync(db, CodeEntityKind.EstablishmentChangeRequest, "ترميز طلبات تغيير الملاك", "EST",
            CodeSequenceReset.Yearly,
            """
            [
              {"kind":"Literal","value":"EST-"},
              {"kind":"Date","value":"YYYY"},
              {"kind":"Literal","value":"-"},
              {"kind":"Sequence","padWidth":5}
            ]
            """, ct);

        await SeedPolicyAsync(db, CodeEntityKind.JobCatalogEntry, "ترميز دليل الوظائف", "JC",
            CodeSequenceReset.PerScope,
            """
            [
              {"kind":"Field","value":"GradeCode"},
              {"kind":"Literal","value":"-"},
              {"kind":"Field","value":"JobClassificationCode"},
              {"kind":"Literal","value":"-"},
              {"kind":"Sequence","padWidth":3,"scopeFields":["GradeCode","JobClassificationCode"]}
            ]
            """, ct);

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedPrivilegedAssetTagIfMissingAsync(HRCoreDbContext db, CancellationToken ct)
    {
        if (await db.CodePolicyTemplates.AnyAsync(t => t.PolicyKey == "ASSET-PRIVILEGED", ct))
            return;

        await SeedPolicyAsync(db, CodeEntityKind.AssetTag, "رقم عهدة مميزة", "VIP",
            CodeSequenceReset.Yearly,
            """
            [
              {"kind":"Literal","value":"VIP-"},
              {"kind":"Date","value":"YYYY"},
              {"kind":"Literal","value":"-"},
              {"kind":"Sequence","padWidth":6}
            ]
            """, ct, policyKey: "ASSET-PRIVILEGED");
    }

    private static async Task SeedJobCatalogEntryIfMissingAsync(HRCoreDbContext db, CancellationToken ct)
    {
        if (await db.CodePolicyTemplates.AnyAsync(t => t.EntityKind == CodeEntityKind.JobCatalogEntry, ct))
            return;

        await SeedPolicyAsync(db, CodeEntityKind.JobCatalogEntry, "ترميز دليل الوظائف", "JC",
            CodeSequenceReset.PerScope,
            """
            [
              {"kind":"Field","value":"GradeCode"},
              {"kind":"Literal","value":"-"},
              {"kind":"Field","value":"JobClassificationCode"},
              {"kind":"Literal","value":"-"},
              {"kind":"Sequence","padWidth":3,"scopeFields":["GradeCode","JobClassificationCode"]}
            ]
            """, ct);
    }

    /// <summary>FUN-029 (I3): DEC-{TypeCode}-YYYY-00001 for HR.Contracts administrative decisions.</summary>
    private static async Task SeedAdministrativeDecisionIfMissingAsync(HRCoreDbContext db, CancellationToken ct)
    {
        if (await db.CodePolicyTemplates.AnyAsync(t => t.EntityKind == CodeEntityKind.AdministrativeDecision, ct))
            return;

        await SeedPolicyAsync(db, CodeEntityKind.AdministrativeDecision, "ترميز القرارات الإدارية", "DEC",
            CodeSequenceReset.Yearly,
            """
            [
              {"kind":"Literal","value":"DEC-"},
              {"kind":"Field","value":"TypeCode"},
              {"kind":"Literal","value":"-"},
              {"kind":"Date","value":"YYYY"},
              {"kind":"Literal","value":"-"},
              {"kind":"Sequence","padWidth":5,"scopeFields":["TypeCode"]}
            ]
            """, ct);
    }

    private static async Task SeedInventorySkuIfMissingAsync(HRCoreDbContext db, CancellationToken ct)
    {
        if (await db.CodePolicyTemplates.AnyAsync(t => t.EntityKind == CodeEntityKind.InventorySku, ct))
            return;

        await SeedPolicyAsync(db, CodeEntityKind.InventorySku, "رمز المخزون", "SKU",
            CodeSequenceReset.Yearly,
            """
            [
              {"kind":"Literal","value":"SKU-"},
              {"kind":"Date","value":"YYYY"},
              {"kind":"Literal","value":"-"},
              {"kind":"Sequence","padWidth":6}
            ]
            """, ct);
    }

    private static async Task SeedPolicyAsync(
        HRCoreDbContext db,
        CodeEntityKind kind,
        string name,
        string fallbackPrefix,
        CodeSequenceReset reset,
        string segmentsJson,
        CancellationToken ct,
        string? policyKey = null)
    {
        var template = new CodePolicyTemplate
        {
            TenantId = TenantDefaults.DefaultTenantId,
            Name = name,
            EntityKind = kind,
            Scope = CodePolicyScope.National,
            PolicyKey = policyKey,
            Description = policyKey is null
                ? $"COD-001 national default for {kind}"
                : $"COD-001 policy key {policyKey} for {kind}"
        };

        var version = new CodePolicyVersion
        {
            TenantId = TenantDefaults.DefaultTenantId,
            VersionNumber = 1,
            Status = CodePolicyVersionStatus.Published,
            EffectiveFrom = DateTime.UtcNow.AddYears(-1),
            SegmentsJson = segmentsJson,
            SequenceReset = reset,
            FallbackPrefix = fallbackPrefix
        };

        template.Versions.Add(version);
        db.CodePolicyTemplates.Add(template);
        await db.SaveChangesAsync(ct);
    }
}
