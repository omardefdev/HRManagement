using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

[Display(Name = "خطة التعاقب")]
public class SuccessionPlan : AuditableEntity
{
    [Display(Name = "المنصب الحرج")]
    public Guid KeyPositionId { get; set; }

    [Display(Name = "السنة")]
    [Range(2000, 2100)]
    public int Year { get; set; } = DateTime.UtcNow.Year;

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    public virtual KeyPosition? KeyPosition { get; set; }
    public virtual ICollection<SuccessorCandidate> Candidates { get; set; } = new List<SuccessorCandidate>();
}
