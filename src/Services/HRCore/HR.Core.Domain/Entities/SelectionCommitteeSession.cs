using System.ComponentModel.DataAnnotations;
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

    public virtual ICollection<SelectionCommitteeMember> Members { get; set; } = [];
}
