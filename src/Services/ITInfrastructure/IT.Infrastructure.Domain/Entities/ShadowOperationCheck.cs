using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace IT.Infrastructure.Domain.Entities;

/// <summary>DEC-001: field audit for shadow/parallel operations after cutover.</summary>
[Display(Name = "ÙØ­Øµ Ø¹Ù…Ù„ÙŠØ© Ø¸Ù„")]
public class ShadowOperationCheck : AuditableEntity
{
    public Guid LegacySystemId { get; set; }

    [Display(Name = "ØªØ§Ø±ÙŠØ® Ø§Ù„ØªØ¯Ù‚ÙŠÙ‚")]
    public DateTime AuditDate { get; set; } = DateTime.UtcNow;

    [Display(Name = "Ø¹Ø¯Ø¯ Ø§Ù„Ù†ØªØ§Ø¦Ø¬")]
    public int FindingsCount { get; set; }

    [MaxLength(2000)]
    [Display(Name = "Ø§Ù„Ø§Ø³ØªØ«Ù†Ø§Ø¡Ø§Øª")]
    public string? Exceptions { get; set; }

    [MaxLength(200)]
    [Display(Name = "Ø§Ù„Ù…Ø¯Ù‚Ù‚")]
    public string Auditor { get; set; } = string.Empty;

    public bool IsClean => FindingsCount == 0 || !string.IsNullOrWhiteSpace(Exceptions);
}
