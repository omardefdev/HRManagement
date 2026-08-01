using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HRManagement.SharedKernel.Recruitment;

/// <summary>HCM-001: an offer letter issued to a ranked candidate.</summary>
public class OfferLetter : BaseEntity
{
    public Guid ApplicationId { get; set; }

    public Guid PositionId { get; set; }

    [Required, MaxLength(50)]
    public string OfferNumber { get; set; } = string.Empty;

    public OfferStatus Status { get; set; } = OfferStatus.Draft;

    public DateTime IssuedDate { get; set; }

    public DateTime ExpiryDate { get; set; }

    public DateTime? AcceptedDate { get; set; }

    public decimal OfferedSalary { get; set; }

    [MaxLength(50)]
    public string? GradeCode { get; set; }

    public DateTime ProposedStartDate { get; set; }

    [MaxLength(100)]
    public string? LegalSourceId { get; set; }

    public bool IsConvertedToEmployee { get; set; }
}

public enum OfferStatus
{
    Draft = 0,
    PendingApproval = 1,
    Issued = 2,
    Accepted = 3,
    Declined = 4,
    Expired = 5,
    Revoked = 6
}
