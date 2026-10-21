using System.ComponentModel.DataAnnotations;

namespace HRManagement.SharedKernel.Enums;

public enum DocumentType
{
    [Display(Name = "شهادة")]
    Certificate,
    [Display(Name = "صورة")]
    Photo,
    [Display(Name = "بصمة")]
    Fingerprint,
    [Display(Name = "هوية وطنية")]
    NationalId,
    [Display(Name = "جواز سفر")]
    Passport,
    [Display(Name = "أخرى")]
    Other
}
