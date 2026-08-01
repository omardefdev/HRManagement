using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace IT.Infrastructure.Domain.Entities;

/// <summary>DEC-001: signed media/hardware destruction certificate.</summary>
[Display(Name = "Ø´Ù‡Ø§Ø¯Ø© Ø¥ØªÙ„Ø§Ù ÙˆØ³Ø§Ø¦Ø·")]
public class MediaDestructionCertificate : AuditableEntity
{
    public Guid LegacySystemId { get; set; }

    [Required]
    [MaxLength(200)]
    [Display(Name = "Ø§Ù„ÙˆØµÙŠ")]
    public string Custodian { get; set; } = string.Empty;

    [Display(Name = "ØªØ§Ø±ÙŠØ® Ø§Ù„Ø¥ØªÙ„Ø§Ù")]
    public DateTime DestroyedAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(64)]
    [Display(Name = "Ø¨ØµÙ…Ø© Ø§Ù„Ø´Ù‡Ø§Ø¯Ø©")]
    public string CertificateHash { get; set; } = string.Empty;

    [MaxLength(500)]
    [Display(Name = "Ø§Ù„ÙˆØµÙ")]
    public string Description { get; set; } = string.Empty;
}
