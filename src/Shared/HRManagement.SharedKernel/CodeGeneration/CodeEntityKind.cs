using System.ComponentModel.DataAnnotations;

namespace HRManagement.SharedKernel.CodeGeneration;

public enum CodeEntityKind
{
    [Display(Name = "وحدة تنظيمية")]
    OrgUnit,
    [Display(Name = "منصب ممول")]
    FundedPosition,
    [Display(Name = "رقم أصل")]
    AssetTag,
    [Display(Name = "رقم أصل تقني")]
    ItAssetTag,
    [Display(Name = "مرجع أرشيف")]
    ArchiveReference,
    [Display(Name = "قضية قانونية")]
    LegalCase,
    [Display(Name = "معاملة خدمة")]
    ServiceTransaction,
    [Display(Name = "رقم بطاقة تعريف")]
    IdCardSerial,
    [Display(Name = "مرجع مواطن")]
    CitizenReference,
    [Display(Name = "قضية طب شرعي")]
    ForensicCase,
    [Display(Name = "مركز تكلفة")]
    CostCenter,
    [Display(Name = "رمز صنف مخزون (SKU)")]
    InventorySku,
    [Display(Name = "طلب شاغر")]
    RecruitmentRequisition,
    [Display(Name = "عرض تعيين")]
    OfferLetter,
    [Display(Name = "طلب تغيير ملاك")]
    EstablishmentChangeRequest,
    [Display(Name = "وظيفة في الدليل")]
    JobCatalogEntry,
    /// <summary>FUN-029 (I3): administrative decision numbers (appointment decisions first).</summary>
    [Display(Name = "قرار إداري")]
    AdministrativeDecision,
    /// <summary>HCM-031 (PLAN-31): recruitment competition numbers (CMP-YYYY-00001).</summary>
    [Display(Name = "مسابقة توظيف")]
    RecruitmentCompetition
}
