using HR.Core.Domain.Entities.CodeGeneration;
using HR.Core.Infrastructure.Services.CodeGeneration;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Data;

/// <summary>A national code policy as COD-001 seeds it (PLAN-48: one catalog for the seeder, the upgrade and the bootstrap).</summary>
public sealed record NationalCodePolicyDefinition(
    CodeEntityKind Kind,
    string Name,
    string FallbackPrefix,
    CodeSequenceReset Reset,
    string SegmentsJson,
    string? PolicyKey = null)
{
    public CodeFormat Format => CodeFormat.FromJson(SegmentsJson, Reset);
}

public static class CodeGenerationSeeder
{
    /// <summary>D-TEN-6: one counter per (unit, grade), without the budget year; a carried-over position keeps its code.</summary>
    public const string FundedPositionJson =
        """
        [
          {"kind":"Field","value":"OrgUnitCode"},
          {"kind":"Literal","value":"-"},
          {"kind":"Field","value":"GradeCode"},
          {"kind":"Literal","value":"-"},
          {"kind":"Sequence","padWidth":3,"scopeFields":["OrgUnitCode","GradeCode"]}
        ]
        """;

    /// <summary>D-TEN-5: REQ-{BudgetYear}-#####, one counter per budget year.</summary>
    public const string RecruitmentRequisitionJson =
        """
        [
          {"kind":"Literal","value":"REQ-"},
          {"kind":"Field","value":"BudgetYear"},
          {"kind":"Literal","value":"-"},
          {"kind":"Sequence","padWidth":5,"scopeFields":["BudgetYear"]}
        ]
        """;

    /// <summary>
    /// COD-001's national code policies, one per kind (and one keyed AssetTag), as a new database gets them. PLAN-48 corrected
    /// three (G-COD-03): FundedPosition keeps one counter per (unit, grade) without the budget year (D-TEN-6); RecruitmentRequisition
    /// shows the budget year with one counter per budget year (D-TEN-5); CostCenter is derived from the unit, with no counter
    /// (D-TEN-7). Existing databases reach the same versions through <c>CodePolicyUpgrades</c>. Every entry passes
    /// <see cref="CodePolicyValidator"/>.
    /// </summary>
    public static readonly IReadOnlyList<NationalCodePolicyDefinition> NationalPolicies =
    [
        new(CodeEntityKind.OrgUnit, "ترميز الوحدات التنظيمية", "OU", CodeSequenceReset.PerScope,
            """
            [
              {"kind":"Field","value":"ParentCode"},
              {"kind":"Literal","value":"."},
              {"kind":"Sequence","padWidth":2,"scopeFields":["ParentCode"]}
            ]
            """),
        new(CodeEntityKind.FundedPosition, "ترميز المناصب الممولة", "POS", CodeSequenceReset.PerScope,
            FundedPositionJson),
        new(CodeEntityKind.AssetTag, "رقم العهدة", "AH", CodeSequenceReset.Yearly,
            """
            [
              {"kind":"Literal","value":"AH-"},
              {"kind":"Date","value":"YYYY"},
              {"kind":"Literal","value":"-"},
              {"kind":"Sequence","padWidth":6}
            ]
            """),
        new(CodeEntityKind.ItAssetTag, "رقم أصل IT", "IT", CodeSequenceReset.Yearly,
            """
            [
              {"kind":"Literal","value":"IT-"},
              {"kind":"Date","value":"YYYY"},
              {"kind":"Literal","value":"-"},
              {"kind":"Sequence","padWidth":6}
            ]
            """),
        new(CodeEntityKind.ArchiveReference, "مرجع الأرشيف", "ARC", CodeSequenceReset.Yearly,
            """
            [
              {"kind":"Literal","value":"ARC-"},
              {"kind":"Field","value":"EntityPrefix"},
              {"kind":"Literal","value":"-"},
              {"kind":"Date","value":"YYYY"},
              {"kind":"Literal","value":"-"},
              {"kind":"Sequence","padWidth":5,"scopeFields":["EntityPrefix"]}
            ]
            """),
        new(CodeEntityKind.LegalCase, "رقم القضية", "LC", CodeSequenceReset.Yearly,
            """
            [
              {"kind":"Literal","value":"LC-"},
              {"kind":"Date","value":"YYYY"},
              {"kind":"Literal","value":"-"},
              {"kind":"Sequence","padWidth":5}
            ]
            """),
        new(CodeEntityKind.ServiceTransaction, "مرجع معاملة خدمة", "SRV", CodeSequenceReset.Yearly,
            """
            [
              {"kind":"Literal","value":"SRV-"},
              {"kind":"Field","value":"TypeCode"},
              {"kind":"Literal","value":"-"},
              {"kind":"Date","value":"YYYY"},
              {"kind":"Literal","value":"-"},
              {"kind":"Sequence","padWidth":5,"scopeFields":["TypeCode"]}
            ]
            """),
        new(CodeEntityKind.CitizenReference, "مرجع طلب مواطن", "CIT", CodeSequenceReset.Yearly,
            """
            [
              {"kind":"Literal","value":"CIT-"},
              {"kind":"Date","value":"YYYY"},
              {"kind":"Literal","value":"-"},
              {"kind":"Sequence","padWidth":6}
            ]
            """),
        new(CodeEntityKind.IdCardSerial, "الرقم التسلسلي للبطاقة", "ID", CodeSequenceReset.Yearly,
            """
            [
              {"kind":"Literal","value":"ID-"},
              {"kind":"Date","value":"YYYY"},
              {"kind":"Literal","value":"-"},
              {"kind":"Sequence","padWidth":7}
            ]
            """),
        new(CodeEntityKind.CostCenter, "مركز التكلفة", "CC", CodeSequenceReset.Never,
            """
            [
              {"kind":"Field","value":"OrgUnitCode"}
            ]
            """),
        new(CodeEntityKind.InventorySku, "رمز المخزون", "SKU", CodeSequenceReset.Yearly,
            """
            [
              {"kind":"Literal","value":"SKU-"},
              {"kind":"Date","value":"YYYY"},
              {"kind":"Literal","value":"-"},
              {"kind":"Sequence","padWidth":6}
            ]
            """),
        new(CodeEntityKind.RecruitmentRequisition, "ترميز طلبات الشاغر", "REQ", CodeSequenceReset.PerScope,
            RecruitmentRequisitionJson),
        new(CodeEntityKind.OfferLetter, "ترميز خطابات العرض", "OFF", CodeSequenceReset.Yearly,
            """
            [
              {"kind":"Literal","value":"OFF-"},
              {"kind":"Date","value":"YYYY"},
              {"kind":"Literal","value":"-"},
              {"kind":"Sequence","padWidth":5}
            ]
            """),
        new(CodeEntityKind.EstablishmentChangeRequest, "ترميز طلبات تغيير الملاك", "EST", CodeSequenceReset.Yearly,
            """
            [
              {"kind":"Literal","value":"EST-"},
              {"kind":"Date","value":"YYYY"},
              {"kind":"Literal","value":"-"},
              {"kind":"Sequence","padWidth":5}
            ]
            """),
        new(CodeEntityKind.JobCatalogEntry, "ترميز دليل الوظائف", "JC", CodeSequenceReset.PerScope,
            """
            [
              {"kind":"Field","value":"GradeCode"},
              {"kind":"Literal","value":"-"},
              {"kind":"Field","value":"JobClassificationCode"},
              {"kind":"Literal","value":"-"},
              {"kind":"Sequence","padWidth":3,"scopeFields":["GradeCode","JobClassificationCode"]}
            ]
            """),
        // FUN-029 (I3): DEC-{TypeCode}-YYYY-00001 for HR.Contracts administrative decisions.
        new(CodeEntityKind.AdministrativeDecision, "ترميز القرارات الإدارية", "DEC", CodeSequenceReset.Yearly,
            """
            [
              {"kind":"Literal","value":"DEC-"},
              {"kind":"Field","value":"TypeCode"},
              {"kind":"Literal","value":"-"},
              {"kind":"Date","value":"YYYY"},
              {"kind":"Literal","value":"-"},
              {"kind":"Sequence","padWidth":5,"scopeFields":["TypeCode"]}
            ]
            """),
        // HCM-031 (PLAN-31): CMP-YYYY-00001 for recruitment competitions.
        new(CodeEntityKind.RecruitmentCompetition, "ترميز مسابقات التوظيف", "CMP", CodeSequenceReset.Yearly,
            """
            [
              {"kind":"Literal","value":"CMP-"},
              {"kind":"Date","value":"YYYY"},
              {"kind":"Literal","value":"-"},
              {"kind":"Sequence","padWidth":5}
            ]
            """),
        // The privileged asset tag: its own key, and since PLAN-48 its own counter (the key is part of the scope key).
        new(CodeEntityKind.AssetTag, "رقم عهدة مميزة", "VIP", CodeSequenceReset.Yearly,
            """
            [
              {"kind":"Literal","value":"VIP-"},
              {"kind":"Date","value":"YYYY"},
              {"kind":"Literal","value":"-"},
              {"kind":"Sequence","padWidth":6}
            ]
            """, PolicyKey: "ASSET-PRIVILEGED")
    ];

    /// <summary>
    /// Seeds every national policy of <see cref="NationalPolicies"/> that has no national template of the same kind and key yet
    /// (a new database gets all of them; an existing one only what was added since). Runs in the national session.
    /// </summary>
    public static async Task SeedAsync(HRCoreDbContext db, CancellationToken ct = default)
    {
        var existing = await db.CodePolicyTemplates.NationalOnly().AsNoTracking()
            .Where(t => t.Scope == CodePolicyScope.National)
            .Select(t => new { t.EntityKind, t.PolicyKey })
            .ToListAsync(ct);

        var added = false;
        foreach (var definition in NationalPolicies)
        {
            if (existing.Any(e => e.EntityKind == definition.Kind && e.PolicyKey == definition.PolicyKey))
                continue;

            AddNationalPolicy(db, definition);
            added = true;
        }

        if (added)
        {
            await db.SaveChangesAsync(ct);
            CodeGeneratorService.InvalidateSnapshotCache();
        }
    }

    private static void AddNationalPolicy(HRCoreDbContext db, NationalCodePolicyDefinition definition)
    {
        var issues = CodePolicyValidator.ValidateVersion(definition.Kind, definition.Format);
        if (issues.Count > 0)
            throw new InvalidOperationException(
                $"PLAN-48: the national code policy {definition.Kind} {definition.PolicyKey} fails the validator: " +
                string.Join(", ", issues.Select(i => i.Code)));

        var template = new CodePolicyTemplate
        {
            TenantId = NationalTenant.Id,
            Name = definition.Name,
            EntityKind = definition.Kind,
            Scope = CodePolicyScope.National,
            PolicyKey = definition.PolicyKey,
            Description = definition.PolicyKey is null
                ? $"COD-001 national default for {definition.Kind}"
                : $"COD-001 policy key {definition.PolicyKey} for {definition.Kind}"
        };

        template.Versions.Add(new CodePolicyVersion
        {
            TenantId = NationalTenant.Id,
            VersionNumber = 1,
            Status = CodePolicyVersionStatus.Published,
            EffectiveFrom = DateTime.UtcNow.AddYears(-1),
            SegmentsJson = definition.SegmentsJson,
            SequenceReset = definition.Reset,
            FallbackPrefix = definition.FallbackPrefix
        });
        db.CodePolicyTemplates.Add(template);
    }
}
