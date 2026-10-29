using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Dev.Analytics.Domain.Entities;

/// <summary>
/// HCM-032 (PLAN-32, P6): one closed or cancelled recruitment requisition, from HR.Core's
/// <c>IRecruitmentRequisitionClosed</c> — aggregate counts only (no person-level data). One row per requisition
/// (unique per tenant): a redelivered message updates the same row.
/// </summary>
[Display(Name = "حقيقة طلب شاغر")]
public class RecruitmentRequisitionFact : BaseEntity
{
    public Guid RequisitionId { get; set; }

    public Guid OrgUnitId { get; set; }

    /// <summary><c>Closed</c> or <c>Cancelled</c>.</summary>
    [MaxLength(16)]
    public string Outcome { get; set; } = string.Empty;

    public DateTime ClosedAt { get; set; }

    public int Applicants { get; set; }
    public int HiredCount { get; set; }
    public int? TimeToFillDays { get; set; }

    public int ApplicantsPublic { get; set; }
    public int ApplicantsInternal { get; set; }
    public int ApplicantsHrEntered { get; set; }
    public int ApplicantsLegacy { get; set; }

    public int OffersAccepted { get; set; }
    public int OffersDeclined { get; set; }
    public int OffersExpired { get; set; }
    public int OffersRevoked { get; set; }

    public int SchemaVersion { get; set; } = 1;
}
