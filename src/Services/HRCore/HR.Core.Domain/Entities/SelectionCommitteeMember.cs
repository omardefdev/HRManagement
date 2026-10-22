using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

public class SelectionCommitteeMember : AuditableEntity
{
    /// <summary>HCM-028: committee roles (legacy rows may hold free text, read as Member).</summary>
    public const string ChairRole = "Chair";
    public const string MemberRole = "Member";

    [Display(Name = "جلسة اللجنة")]
    public Guid SessionId { get; set; }

    public virtual SelectionCommitteeSession? Session { get; set; }

    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    public virtual Employee? Employee { get; set; }

    [Required, StringLength(50)]
    [Display(Name = "الدور")]
    public string Role { get; set; } = MemberRole;

    [Display(Name = "تعارض مصالح")]
    public bool HasConflictOfInterest { get; set; }

    [Display(Name = "انسحاب")]
    public bool Recused { get; set; }

    // legacy — PLAN-28: replaced by SelectionCommitteeVote; kept for existing rows, no longer read or written.
    [StringLength(20)]
    [Display(Name = "التصويت")]
    public string? Vote { get; set; }

    public virtual ICollection<SelectionCommitteeMemberCoi> CoiDeclarations { get; set; } = [];
}
