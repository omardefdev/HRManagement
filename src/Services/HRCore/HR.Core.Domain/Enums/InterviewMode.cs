using System.ComponentModel.DataAnnotations;

namespace HR.Core.Domain.Enums;

/// <summary>HCM-026: how an interview is conducted.</summary>
public enum InterviewMode
{
    [Display(Name = "حضوري")]
    InPerson = 0,
    [Display(Name = "عن بُعد")]
    Remote = 1
}
