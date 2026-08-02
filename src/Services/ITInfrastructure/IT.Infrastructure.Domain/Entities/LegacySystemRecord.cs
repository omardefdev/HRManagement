using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace IT.Infrastructure.Domain.Entities;

/// <summary>DEC-001: tracked legacy system decommission lifecycle.</summary>
[Display(Name = "Ù†Ø¸Ø§Ù… Ù‚Ø¯ÙŠÙ…")]
public class LegacySystemRecord : AuditableEntity
{
    [Required]
    [MaxLength(200)]
    [Display(Name = "Ø§Ø³Ù… Ø§Ù„Ù†Ø¸Ø§Ù…")]
    public string SystemName { get; set; } = string.Empty;

    [Display(Name = "ØªØ§Ø±ÙŠØ® Ø§Ù„Ù‚Ø·Ø¹")]
    public DateTime CutoffDate { get; set; }

    [MaxLength(32)]
    [Display(Name = "Ø§Ù„Ù…Ø±Ø­Ù„Ø©")]
    public string Phase { get; set; } = "Active";
    // Active / WriteFrozen / ReadOnly / Archived / Decommissioned

    [MaxLength(64)]
    [Display(Name = "Ø¨ØµÙ…Ø© Ø§Ù„Ø£Ø±Ø´ÙŠÙ")]
    public string? ArchiveManifestHash { get; set; }

    [MaxLength(500)]
    [Display(Name = "Ù…Ø³Ø§Ø± Ø§Ù„Ø£Ø±Ø´ÙŠÙ")]
    public string? ArchiveUri { get; set; }

    public bool WriteAllowed =>
        Phase is "Active";

    public void AdvanceTo(string nextPhase)
    {
        var order = new[] { "Active", "WriteFrozen", "ReadOnly", "Archived", "Decommissioned" };
        var current = Array.IndexOf(order, Phase);
        var next = Array.IndexOf(order, nextPhase);
        if (next < 0)
            throw new InvalidOperationException($"Ù…Ø±Ø­Ù„Ø© ØºÙŠØ± Ù…Ø¹Ø±ÙˆÙØ©: {nextPhase}");
        if (next < current)
            throw new InvalidOperationException("Ù„Ø§ ÙŠÙ…ÙƒÙ† Ø§Ù„Ø±Ø¬ÙˆØ¹ ÙÙŠ Ù…Ø±Ø§Ø­Ù„ Ø§Ù„Ø¥Ø®Ø±Ø§Ø¬ Ø¨Ø¯ÙˆÙ† Ù†Ø§ÙØ°Ø© Ø±Ø¬ÙˆØ¹ Ù…ØµØ±Ù‘Ø­ Ø¨Ù‡Ø§.");
        Phase = nextPhase;
        UpdatedAt = DateTime.UtcNow;
    }

    public void EnsureWritableOrThrow()
    {
        if (!WriteAllowed)
            throw new InvalidOperationException(
                $"Ø§Ù„Ù†Ø¸Ø§Ù… '{SystemName}' Ù„Ø§ ÙŠÙ‚Ø¨Ù„ Ø§Ù„ÙƒØªØ§Ø¨Ø© Ø¨Ø¹Ø¯ ØªØ§Ø±ÙŠØ® Ø§Ù„Ù‚Ø·Ø¹ (Ù…Ø±Ø­Ù„Ø© {Phase}).");
    }
}
