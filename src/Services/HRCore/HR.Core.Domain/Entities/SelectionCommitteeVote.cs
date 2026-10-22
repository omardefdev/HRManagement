using System.ComponentModel.DataAnnotations;
using HR.Core.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

/// <summary>HCM-028: a member's vote on the committee decision for one candidate (recorded by the HR secretary).</summary>
public class SelectionCommitteeVote : AuditableEntity
{
    [Display(Name = "جلسة اللجنة")]
    public Guid SessionId { get; set; }

    public virtual SelectionCommitteeSession? Session { get; set; }

    [Display(Name = "العضو")]
    public Guid MemberId { get; set; }

    public virtual SelectionCommitteeMember? Member { get; set; }

    [Display(Name = "طلب التوظيف")]
    public Guid ApplicationId { get; set; }

    public virtual RecruitmentApplication? Application { get; set; }

    [Display(Name = "التصويت")]
    public CommitteeVoteValue Vote { get; set; }

    [StringLength(1000)]
    [Display(Name = "تعليق")]
    public string? Comment { get; set; }

    [Display(Name = "سجّله")]
    public Guid? RecordedBy { get; set; }

    [Display(Name = "تاريخ التسجيل")]
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}
