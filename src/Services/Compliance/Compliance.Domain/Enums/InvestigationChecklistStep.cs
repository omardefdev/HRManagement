using System.ComponentModel.DataAnnotations;

namespace Compliance.Domain.Enums;

public enum InvestigationChecklistStep
{
    [Display(Name = "مُسند")]
    Assigned = 0,
    [Display(Name = "جمع أدلة")]
    EvidenceCollected = 1,
    [Display(Name = "قرار")]
    Decision = 2,
    [Display(Name = "مغلق")]
    Closed = 3
}
