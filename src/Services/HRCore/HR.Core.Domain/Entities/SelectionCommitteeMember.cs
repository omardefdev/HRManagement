using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

public class SelectionCommitteeMember : AuditableEntity
{
    [Display(Name = "جلسة اللجنة")]
    public Guid SessionId { get; set; }

    public virtual SelectionCommitteeSession? Session { get; set; }

    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    public virtual Employee? Employee { get; set; }

    [Required, StringLength(50)]
    [Display(Name = "الدور")]
    public string Role { get; set; } = "Member";

    [Display(Name = "تعارض مصالح")]
    public bool HasConflictOfInterest { get; set; }

    [Display(Name = "انسحاب")]
    public bool Recused { get; set; }

    [StringLength(20)]
    [Display(Name = "التصويت")]
    public string? Vote { get; set; }
}
