using System.ComponentModel.DataAnnotations;
using HR.Core.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

/// <summary>HCM-028: the committee's recommendation and proposed rank for one candidate.</summary>
public class SelectionCommitteeDecision : AuditableEntity
{
    [Display(Name = "جلسة اللجنة")]
    public Guid SessionId { get; set; }

    public virtual SelectionCommitteeSession? Session { get; set; }

    [Display(Name = "طلب التوظيف")]
    public Guid ApplicationId { get; set; }

    public virtual RecruitmentApplication? Application { get; set; }

    [Display(Name = "التوصية")]
    public CommitteeRecommendation Recommendation { get; set; }

    [Display(Name = "الترتيب المقترح")]
    public int? ProposedRank { get; set; }

    [StringLength(2000)]
    [Display(Name = "التعليل")]
    public string? Rationale { get; set; }
}
