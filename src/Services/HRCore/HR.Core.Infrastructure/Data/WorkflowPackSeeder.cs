using System.Text.Json;
using HR.Core.Domain.Entities.Workflow;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Data;

/// <summary>SEED-001: national workflow definitions (replaces per-workflow seeders).</summary>
public static class WorkflowPackSeeder
{
    private record WorkflowSeed(
        Guid Id,
        string Code,
        string Name,
        string DomainEntityType,
        string Description,
        params (int Level, string Name, string Role)[] Steps);

    private static readonly WorkflowSeed[] Workflows =
    [
        new(Guid.Parse("f0050001-0001-4000-8000-000000000001"), "ASSET-ASSIGN-APPROVAL", "موافقة تسليم عهدة", "AssetAssignment",
            "FUN-005-EXT: approval for privileged/high-value asset assignments",
            (1, "موافقة لوجستية", "hr-core"), (2, "موافقة إدارية", "hr-management-approver")),
        new(Guid.Parse("f0050002-0002-4000-8000-000000000002"), "ASSET-LOSS-APPROVAL", "موافقة فقدان أصل", "AssetLossReport",
            "FUN-005: approval before marking assets as lost",
            (1, "موافقة فقدان أصل", "admin-logistics")),
        new(Guid.Parse("f0050003-0003-4000-8000-000000000003"), "HIRE-WIZARD", "معاملة توظيف", "ServiceTransaction",
            "Contracts hire wizard",
            (1, "مراجعة HR", "hr-core"), (2, "موافقة HR", "hr-core")),
        new(Guid.Parse("f0050004-0004-4000-8000-000000000004"), "SEPARATION-WIZARD", "معاملة انفصال", "SeparationRecord",
            "Contracts separation wizard",
            (1, "مراجعة HR", "hr-core"), (2, "مراجعة قانونية", "compliance")),
        new(Guid.Parse("f0050005-0005-4000-8000-000000000005"), "ID-CARD-ISSUANCE", "إصدار بطاقة تعريف", "CardRequest",
            "ID card issuance workflow",
            (1, "إصدار أمني", "security")),
        new(Guid.Parse("f0050006-0006-4000-8000-000000000006"), "TRANSFER-APPROVAL", "موافقة نقل", "TransferRequest",
            "Employee transfer approval",
            (1, "موافقة المدير", "hr-core"), (2, "موافقة HR", "hr-core")),
        new(Guid.Parse("f0050007-0007-4000-8000-000000000007"), "RECRUITMENT-HIRE", "توظيف مرشح", "RecruitmentApplication",
            "Recruitment hire committee",
            (1, "لجنة التوظيف", "hr-core")),
        new(Guid.Parse("f0050008-0008-4000-8000-000000000008"), "DISCIPLINARY-APPROVAL", "إجراء تأديبي", "DisciplinaryAction",
            "Compliance disciplinary review",
            (1, "مراجعة امتثال", "compliance")),
        new(Guid.Parse("f0050009-0009-4000-8000-000000000009"), "IT-TICKET", "تذكرة IT", "Ticket",
            "IT support escalation",
            (1, "دعم L1", "it-support"), (2, "دعم L2", "it-support")),
        new(Guid.Parse("f0050010-0010-4000-8000-000000000010"), "RECRUITMENT-REQUISITION-APPROVAL", "اعتماد طلب شاغر", "RecruitmentRequisition",
            "HCM-002: approval workflow for staffing requisitions",
            (1, "مراجعة HR", "hr-core"), (2, "اعتماد إداري", "hr-management-approver")),
        new(Guid.Parse("f0050011-0011-4000-8000-000000000011"), "RECRUITMENT-OFFER-APPROVAL", "اعتماد خطاب عرض", "OfferLetter",
            "HCM-004: approval workflow for offer letters",
            (1, "مراجعة HR", "hr-core"), (2, "اعتماد إداري", "hr-management-approver")),
        new(Guid.Parse("f0050012-0012-4000-8000-000000000012"), "RECRUITMENT-SELECTION-COMMITTEE", "لجنة اختيار", "SelectionCommitteeSession",
            "HCM-006: selection committee quorum and HR director sign-off",
            (1, "تشكيل اللجنة", "hr-core"), (2, "نصاب اللجنة", "hr-core"), (3, "اعتماد مدير HR", "hr-management-approver")),
        new(Guid.Parse("f0050013-0013-4000-8000-000000000013"), "RECRUITMENT-APPEAL-REVIEW", "مراجعة استئناف", "RecruitmentAppeal",
            "HCM-006: independent reviewer appeal decision",
            (1, "مراجع مستقل", "compliance")),
        new(Guid.Parse("f0050014-0014-4000-8000-000000000014"), "ESTABLISHMENT-CHANGE-APPROVAL", "اعتماد تغيير ملاك", "EstablishmentChangeRequest",
            "EST-002: approval workflow for establishment catalog and funded position changes",
            (1, "مراجعة HR", "hr-core"), (2, "اعتماد إداري", "hr-management-approver")),
        // PLAN-25: move step 3 to compliance
        new(Guid.Parse("f0050015-0015-4000-8000-000000000015"), "RECRUITMENT-OFFER-OVERRIDE-APPROVAL", "اعتماد عرض استثنائي (تجاوز الجدارة)", "OfferLetter",
            "HCM-027: offers that skip merit order — three distinct approvers",
            (1, "مراجعة HR", "hr-core"), (2, "اعتماد إداري", "hr-management-approver"), (3, "اعتماد استثناء الجدارة", "hr-management-approver")),
        new(Guid.Parse("f0050016-0016-4000-8000-000000000016"), "RECRUITMENT-COMPETITION-APPROVAL", "اعتماد مسابقة توظيف", "RecruitmentCompetition",
            "HCM-031: approval of a multi-vacancy recruitment competition (the creator never approves)",
            (1, "مراجعة HR", "hr-core"), (2, "اعتماد إداري", "hr-management-approver"))
    ];

    public static async Task SeedAsync(HRCoreDbContext db, CancellationToken ct = default)
    {
        foreach (var wf in Workflows)
        {
            if (await db.WorkflowDefinitions
                    .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.SeedInitialization)
                    .AnyAsync(d => d.TenantId == TenantDefaults.DefaultTenantId && d.Code == wf.Code, ct))
                continue;

            var steps = wf.Steps.Select(s => new WorkflowStepSnapshot
            {
                Level = s.Level,
                Name = s.Name,
                StepType = WorkflowStepType.Approval,
                RoleRequired = s.Role
            }).ToArray();

            db.WorkflowDefinitions.Add(new WorkflowDefinition
            {
                Id = wf.Id,
                TenantId = TenantDefaults.DefaultTenantId,
                Code = wf.Code,
                Name = wf.Name,
                Version = 1,
                IsPublished = true,
                DomainEntityType = wf.DomainEntityType,
                StepsJson = JsonSerializer.Serialize(steps),
                GuardrailRolesJson = "[]",
                Description = wf.Description
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
