using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;

namespace HR.Core.Domain.Services;

/// <summary>HCM-001: validates recruitment requisition create/edit/delete rules.</summary>
public class RecruitmentRequisitionService(EstablishmentService establishmentService)
{
    public EstablishmentService Establishment { get; } = establishmentService;

    public string? ValidateCreate(FundedPosition position, bool hasActiveRequisitionOnPosition)
    {
        if (!RequisitionWorkflowRules.CanCreate(position))
        {
            return position.Status switch
            {
                FundedPositionStatus.Occupied => "لا يمكن إنشاء طلب شاغر على منصب مشغول.",
                FundedPositionStatus.Frozen => "لا يمكن إنشاء طلب شاغر على منصب مجمد.",
                _ => "المنصب الممول غير متاح لطلب شاغر."
            };
        }

        if (hasActiveRequisitionOnPosition)
            return "يوجد طلب شاغر نشط مسبقاً على هذا المنصب الممول.";

        if (!RequisitionWorkflowRules.ValidateVacancyCount(1))
            return "عدد الشواغر يجب أن يكون 1 في هذه المرحلة.";

        return null;
    }

    public string? ValidateEdit(RequisitionStatus status)
    {
        if (!RequisitionWorkflowRules.CanEdit(status))
            return "يمكن تعديل طلبات الشاغر في حالة مسودة فقط.";
        return null;
    }

    public string? ValidateDelete(RequisitionStatus status)
    {
        if (!RequisitionWorkflowRules.CanDelete(status))
            return "يمكن حذف طلبات الشاغر في حالة مسودة فقط.";
        return null;
    }

    public static string DefaultJobTitle(JobCatalogEntry? catalog) =>
        string.IsNullOrWhiteSpace(catalog?.TitleAr) ? string.Empty : catalog.TitleAr;

    public static PublicJobPosting MapToPublicJobPosting(
        RecruitmentRequisition requisition,
        FundedPosition position)
    {
        var requirements = position.JobCatalogEntry?.DescriptionAr;
        if (string.IsNullOrWhiteSpace(requirements))
            requirements = $"الدرجة: {position.GradeCode}";

        return new PublicJobPosting
        {
            TenantId = requisition.TenantId,
            Title = requisition.JobTitle,
            Entity = position.OrgUnit?.NameAr,
            Location = position.OrgUnit?.NameAr,
            Description = requisition.JustificationNotes ?? position.JobCatalogEntry?.DescriptionAr,
            Requirements = requirements,
            ClosingDate = null,
            IsPublished = true,
            RequisitionId = requisition.Id
        };
    }

    public static InternalVacancy MapToInternalVacancy(
        RecruitmentRequisition requisition,
        FundedPosition position)
    {
        var requirements = position.JobCatalogEntry?.DescriptionAr;
        if (string.IsNullOrWhiteSpace(requirements))
            requirements = $"الدرجة: {position.GradeCode}";

        return new InternalVacancy
        {
            TenantId = requisition.TenantId,
            Title = requisition.JobTitle,
            OrgUnitId = position.OrgUnitId,
            Description = requisition.JustificationNotes ?? position.JobCatalogEntry?.DescriptionAr,
            RequiredQualifications = requirements,
            ClosingDate = null,
            IsOpen = true,
            RequisitionId = requisition.Id,
            IsDerivedFromRequisition = true
        };
    }
}
