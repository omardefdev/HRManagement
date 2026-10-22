using System.ComponentModel.DataAnnotations;
using HR.Core.Domain.Entities;
using HRManagement.SharedKernel.Enums;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HR.Core.Web.ViewModels;

public enum RecruitmentHireMode
{
    [Display(Name = "إنشاء جديد")]
    CreateNew,
    [Display(Name = "ربط موجود")]
    LinkExisting
}

public class RecruitmentHireViewModel
{
    public static IReadOnlyList<(string Code, string Label)> EmploymentCategories { get; } =
    [
        ("PERMANENT", "توظيف دائم"),
        ("TEMPORARY", "توظيف مؤقت"),
        ("CONTRACT", "عقد"),
        ("INTERNSHIP", "تدريب عملي")
    ];

    public Guid ApplicationId { get; set; }

    public Guid? RequisitionId { get; set; }

    [Display(Name = "رقم طلب الشاغر")]
    public string? RequisitionNumber { get; set; }

    [Display(Name = "اسم المتقدم")]
    public string ApplicantFullName { get; set; } = string.Empty;

    [Display(Name = "الوظيفة")]
    public string AppliedPosition { get; set; } = string.Empty;

    [Display(Name = "الوحدة التنظيمية")]
    public string? OrgUnitName { get; set; }

    [Display(Name = "طريقة التعيين")]
    public RecruitmentHireMode Mode { get; set; } = RecruitmentHireMode.CreateNew;

    [Display(Name = "الرقم الوطني")]
    [StringLength(20)]
    public string NationalId { get; set; } = string.Empty;

    [Display(Name = "الاسم الأول")]
    [StringLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Display(Name = "الاسم الأوسط")]
    [StringLength(100)]
    public string? MiddleName { get; set; }

    [Display(Name = "اسم العائلة")]
    [StringLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Display(Name = "تاريخ الميلاد")]
    [DataType(DataType.Date)]
    public DateTime BirthDate { get; set; } = DateTime.UtcNow.Date.AddYears(-25);

    [Display(Name = "الجنس")]
    public Gender Gender { get; set; }

    [Display(Name = "الوحدة التنظيمية")]
    public Guid? PrimaryOrgUnitId { get; set; }

    [Display(Name = "موظف موجود")]
    public Guid? ExistingEmployeeId { get; set; }

    [Display(Name = "تاريخ المباشرة المتوقع")]
    [DataType(DataType.Date)]
    public DateOnly? ExpectedStartDate { get; set; }

    [Display(Name = "فئة التوظيف")]
    public string EmploymentCategory { get; set; } = "PERMANENT";

    /// <summary>HCM-029 (A3): required when the national id differs from the application's.</summary>
    [Display(Name = "مبرر اختلاف الرقم الوطني")]
    [StringLength(1000)]
    public string? NationalIdMismatchReason { get; set; }

    /// <summary>HCM-029 (A2): the application carries a national id (the entered/linked one is compared with it).</summary>
    public bool ApplicationHasNationalId { get; set; }

    /// <summary>HCM-029 (A2): employee already holding the application's (or the entered) national id.</summary>
    public Guid? MatchedEmployeeId { get; set; }

    public string? MatchedEmployeeName { get; set; }

    /// <summary>HCM-029 (A1): same normalized name and birth date — a warning only.</summary>
    public string? PossibleMatchEmployeeName { get; set; }

    public SelectList? OrgUnits { get; set; }
    public SelectList? Employees { get; set; }

    public static RecruitmentHireViewModel FromApplication(
        RecruitmentApplication app,
        RecruitmentRequisition? requisition = null,
        FundedPosition? fundedPosition = null)
    {
        var (first, middle, last) = SplitApplicantName(app.ApplicantFullName);
        var orgUnitId = fundedPosition?.OrgUnitId ?? requisition?.FundedPosition?.OrgUnitId;
        return new RecruitmentHireViewModel
        {
            ApplicationId = app.Id,
            RequisitionId = app.RequisitionId,
            RequisitionNumber = requisition?.RequisitionNumber,
            ApplicantFullName = app.ApplicantFullName,
            AppliedPosition = requisition?.JobTitle ?? app.AppliedPosition,
            OrgUnitName = fundedPosition?.OrgUnit?.NameAr ?? requisition?.FundedPosition?.OrgUnit?.NameAr,
            NationalId = app.NationalId ?? string.Empty,
            ApplicationHasNationalId = !string.IsNullOrWhiteSpace(app.NationalId) || !string.IsNullOrWhiteSpace(app.NationalIdHash),
            FirstName = first,
            MiddleName = middle,
            LastName = last,
            PrimaryOrgUnitId = orgUnitId,
            ExpectedStartDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };
    }

    [Obsolete("Use FromApplication(RecruitmentApplication, ...)")]
    public static RecruitmentHireViewModel FromApplication(
        Guid applicationId,
        string applicantFullName,
        string appliedPosition,
        string? nationalId)
    {
        var (first, middle, last) = SplitApplicantName(applicantFullName);
        return new RecruitmentHireViewModel
        {
            ApplicationId = applicationId,
            ApplicantFullName = applicantFullName,
            AppliedPosition = appliedPosition,
            NationalId = nationalId ?? string.Empty,
            FirstName = first,
            MiddleName = middle,
            LastName = last,
            ExpectedStartDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };
    }

    public static (string First, string? Middle, string Last) SplitApplicantName(string fullName)
    {
        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return ("—", null, "—");
        if (parts.Length == 1)
            return (parts[0], null, parts[0]);

        var first = parts[0];
        var last = parts[^1];
        var middle = parts.Length > 2 ? string.Join(' ', parts[1..^1]) : null;
        return (first, middle, last);
    }
}
