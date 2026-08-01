using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Enums;

namespace HR.Core.Web.ViewModels;

public class ContactCreateViewModel
{
    public Guid EmployeeId { get; set; }

    [Display(Name = "نوع الاتصال")]
    public ContactType Type { get; set; }

    [Display(Name = "القيمة")]
    [Required(ErrorMessage = "قيمة الاتصال مطلوبة")]
    public string Value { get; set; } = string.Empty;

    [Display(Name = "أساسي")]
    public bool IsPrimary { get; set; }

    public string? EmployeeName { get; set; }

    public string ReturnTo { get; set; } = "index";
}

public class ContactEditViewModel : ContactCreateViewModel
{
    public Guid Id { get; set; }
}
