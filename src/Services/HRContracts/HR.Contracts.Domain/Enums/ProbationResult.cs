using System.ComponentModel.DataAnnotations;

namespace HR.Contracts.Domain.Enums;

public enum ProbationResult
{
    [Display(Name = "ناجح")]
    Passed,
    [Display(Name = "راسب")]
    Failed,
    [Display(Name = "ممدد")]
    Extended
}
