using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Payroll.Domain.Entities;

/// <summary>GOV-002-C: change notice emitted on successful Publish.</summary>
[Display(Name = "إشعار تغيير إعداد قانوني")]
public class PayrollConfigChangeNotice : BaseEntity
{
    public Guid PayrollConfigurationId { get; set; }

    public PayrollConfiguration? PayrollConfiguration { get; set; }

    public Guid? SupersedesConfigurationId { get; set; }

    [Required, MaxLength(64)]
    public string ContentHash { get; set; } = string.Empty;

    [Required, MaxLength(2000)]
    public string ImpactSummary { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? Channel { get; set; }

    public DateTime PublishedAt { get; set; }

    public Guid PublishedBy { get; set; }
}
