using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HR.Core.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

/// <summary>HCM-006: selection committee session per requisition.</summary>
public class SelectionCommitteeSession : AuditableEntity
{
    [Display(Name = "طلب الشاغر")]
    public Guid RequisitionId { get; set; }

    public virtual RecruitmentRequisition? Requisition { get; set; }

    [Display(Name = "تاريخ اللجنة")]
    public DateTime CommitteeDate { get; set; }

    [Display(Name = "النتائج")]
    public string? Findings { get; set; }

    [Display(Name = "التوصية")]
    public string? Recommendation { get; set; }

    [Display(Name = "معرف سير العمل")]
    public Guid? WorkflowInstanceId { get; set; }

    [Display(Name = "تاريخ الاعتماد")]
    public DateTime? ApprovedAt { get; set; }

    /// <summary>HCM-028: only Ranking/Final sessions can gate ranking.</summary>
    [Display(Name = "غرض الجلسة")]
    public CommitteePurpose Purpose { get; set; } = CommitteePurpose.Ranking;

    /// <summary>HCM-028: set on submit for approval; cleared when the workflow is rejected.</summary>
    [Display(Name = "تاريخ التقديم")]
    public DateTime? SubmittedAt { get; set; }

    /// <summary>HCM-028: membership, conflicts, votes and decisions are read-only while set.</summary>
    [Display(Name = "تاريخ القفل")]
    public DateTime? LockedAt { get; set; }

    [Display(Name = "رئيس اللجنة")]
    public Guid? ChairEmployeeId { get; set; }

    [NotMapped]
    public bool IsLocked => LockedAt is not null;

    public virtual ICollection<SelectionCommitteeMember> Members { get; set; } = [];

    public virtual ICollection<SelectionCommitteeVote> Votes { get; set; } = [];

    public virtual ICollection<SelectionCommitteeDecision> Decisions { get; set; } = [];
}
