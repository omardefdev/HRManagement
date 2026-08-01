using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Enums;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HR.Core.Web.ViewModels;

public enum RecruitmentHireMode
{
    CreateNew,
    LinkExisting
}

public class RecruitmentHireViewModel
{
    public Guid ApplicationId { get; set; }

    [Display(Name = "اسم المتقدم")]
    public string ApplicantFullName { get; set; } = string.Empty;

    [Display(Name = "الوظيفة")]
    public string AppliedPosition { get; set; } = string.Empty;

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
    public string EmploymentCategory { get; set; } = "DEFAULT";

    public SelectList? OrgUnits { get; set; }
    public SelectList? Employees { get; set; }

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
