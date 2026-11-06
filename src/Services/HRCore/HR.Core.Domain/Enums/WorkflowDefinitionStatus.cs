using System.ComponentModel.DataAnnotations;

namespace HR.Core.Domain.Enums;

/// <summary>PLAN-43 (G-WF-04): the life of one definition version. Only <see cref="Published"/> starts instances.</summary>
public enum WorkflowDefinitionStatus
{
    [Display(Name = "مسودة")]
    Draft,
    [Display(Name = "بانتظار النشر")]
    PendingPublish,
    [Display(Name = "منشور")]
    Published,
    [Display(Name = "مسحوب")]
    Retired
}
