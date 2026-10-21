using System.ComponentModel.DataAnnotations;

namespace HRManagement.SharedKernel.Enums;

public enum ContactType
{
    [Display(Name = "هاتف")]
    Phone,
    [Display(Name = "بريد إلكتروني")]
    Email,
    [Display(Name = "عنوان")]
    Address
}
