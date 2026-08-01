using System.ComponentModel.DataAnnotations;
using HR.Contracts.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HR.Contracts.Web.ViewModels;

public class SeparationRecordCreateViewModel
{
    [Display(Name = "معرّف الموظف")]
    [Required]
    public Guid EmployeeId { get; set; }

    [Display(Name = "العقد (اختياري)")]
    public Guid? ContractId { get; set; }

    [Display(Name = "نوع الإنهاء")]
    public SeparationType SeparationType { get; set; }

    [Display(Name = "تاريخ النفاذ")]
    [Required]
    [DataType(DataType.Date)]
    public DateOnly EffectiveDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

    [Display(Name = "السبب")]
    public string? Reason { get; set; }

    [Display(Name = "المستندات")]
    public string? Documents { get; set; }

    public SelectList? Contracts { get; set; }
}

public class SeparationRecordEditViewModel : SeparationRecordCreateViewModel
{
    public Guid Id { get; set; }
    public bool SeparationEventPublished { get; set; }
}
