using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HRManagement.SharedKernel.Recruitment;

/// <summary>HCM-001: a staffing requisition linked to a funded position.</summary>
public class RecruitmentRequisition : BaseEntity
{
    [Required, MaxLength(50)]
    public string RequisitionNumber { get; set; } = string.Empty;

    public Guid PositionId { get; set; }

    [Required, MaxLength(200)]
    public string JobTitle { get; set; } = string.Empty;

    public RequisitionStatus Status { get; set; } = RequisitionStatus.Draft;

    public DateTime RequestedDate { get; set; } = DateTime.UtcNow;

    public DateTime? ApprovedDate { get; set; }

    public DateTime? ClosedDate { get; set; }

    [MaxLength(500)]
    public string? JustificationNotes { get; set; }

    public int VacancyCount { get; set; } = 1;

    [MaxLength(100)]
    public string? LegalSourceId { get; set; }
}

public enum RequisitionStatus
{
    Draft = 0,
    PendingApproval = 1,
    Approved = 2,
    Published = 3,
    Closed = 4,
    Cancelled = 5
}
