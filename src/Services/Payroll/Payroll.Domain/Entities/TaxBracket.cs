using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HRManagement.SharedKernel.Entities;

namespace Payroll.Domain.Entities;

[Display(Name = "شريحة ضريبة")]
public class TaxBracket : BaseEntity
{
    [Required]
    [ForeignKey(nameof(PayrollConfiguration))]
    public Guid PayrollConfigurationId { get; set; }

    [Display(Name = "الحد الأعلى")]
    public decimal UpperLimit { get; set; }

    [Display(Name = "النسبة %")]
    public decimal RatePercent { get; set; }

    [Display(Name = "الترتيب")]
    public int SortOrder { get; set; }

    public PayrollConfiguration PayrollConfiguration { get; set; } = null!;
}
