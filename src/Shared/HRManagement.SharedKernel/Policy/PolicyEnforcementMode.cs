using System.ComponentModel.DataAnnotations;

namespace HRManagement.SharedKernel.Policy;

public enum PolicyEnforcementMode
{
    [Display(Name = "متوقف")]
    Off,
    [Display(Name = "ظل (يسجّل ولا يغيّر)")]
    Shadow,
    [Display(Name = "مفروض")]
    Enforce
}
