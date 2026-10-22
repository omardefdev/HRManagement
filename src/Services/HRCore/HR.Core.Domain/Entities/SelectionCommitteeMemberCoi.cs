using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

/// <summary>HCM-028: a member's conflict-of-interest declaration for one candidate.</summary>
public class SelectionCommitteeMemberCoi : AuditableEntity
{
    [Display(Name = "العضو")]
    public Guid MemberId { get; set; }

    public virtual SelectionCommitteeMember? Member { get; set; }

    [Display(Name = "طلب التوظيف")]
    public Guid ApplicationId { get; set; }

    public virtual RecruitmentApplication? Application { get; set; }

    [Display(Name = "تعارض مصالح")]
    public bool HasConflict { get; set; }

    [Display(Name = "تاريخ الإقرار")]
    public DateTime DeclaredAt { get; set; } = DateTime.UtcNow;

    [Display(Name = "سجّله")]
    public Guid? RecordedBy { get; set; }
}
