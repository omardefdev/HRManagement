using System.ComponentModel.DataAnnotations;

namespace HRManagement.SharedKernel.Entities;

/// <summary>INT-002: prevents processing the same integration message twice per consumer and tenant.</summary>
public class IntegrationMessageReceipt : BaseEntity
{
    [Required]
    [MaxLength(128)]
    public string MessageId { get; set; } = "";

    [Required]
    [MaxLength(200)]
    public string ConsumerType { get; set; } = "";

    public DateTime ReceivedAt { get; set; }

    public DateTime? ProcessedAt { get; set; }

    [MaxLength(50)]
    public string? Outcome { get; set; }
}
