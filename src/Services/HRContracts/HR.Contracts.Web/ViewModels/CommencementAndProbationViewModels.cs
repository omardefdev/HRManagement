using System.ComponentModel.DataAnnotations;
using HR.Contracts.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HR.Contracts.Web.ViewModels;

public class CommencementRecordViewModel
{
    public Guid? Id { get; set; }

    [Display(Name = "معرّف الموظف")]
    [Required]
    public Guid EmployeeId { get; set; }

    [Display(Name = "العقد")]
    public Guid? ContractId { get; set; }

    [Display(Name = "تاريخ البداية")]
    [Required]
    [DataType(DataType.Date)]
    public DateOnly StartDate { get; set; }

    [Display(Name = "المُبلغ")]
    public Guid? NotifiedBy { get; set; }

    [Display(Name = "تاريخ الإبلاغ")]
    public DateTime? NotificationDate { get; set; }

    public SelectList? Contracts { get; set; }
}

public class ProbationRecordViewModel
{
    public Guid? Id { get; set; }

    [Display(Name = "العقد")]
    [Required]
    public Guid ContractId { get; set; }

    [Display(Name = "معرّف الموظف")]
    [Required]
    public Guid EmployeeId { get; set; }

    [Display(Name = "تاريخ البداية")]
    [Required]
    [DataType(DataType.Date)]
    public DateOnly StartDate { get; set; }

    [Display(Name = "تاريخ النهاية")]
    [Required]
    [DataType(DataType.Date)]
    public DateOnly EndDate { get; set; }

    [Display(Name = "النتيجة")]
    public ProbationResult Result { get; set; }

    [Display(Name = "صاحب القرار")]
    public Guid? DecisionBy { get; set; }

    [Display(Name = "تاريخ القرار")]
    [DataType(DataType.Date)]
    public DateOnly? DecisionDate { get; set; }

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    public SelectList? Contracts { get; set; }
}
