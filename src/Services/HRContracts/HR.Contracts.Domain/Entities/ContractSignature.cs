using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HR.Contracts.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Contracts.Domain.Entities;

[Display(Name = "توقيع العقد")]
public class ContractSignature : AuditableEntity
{
    [Display(Name = "العقد")]
    [Required]
    [ForeignKey(nameof(Contract))]
    public Guid ContractId { get; set; }

    [Display(Name = "الموقّع")]
    [Required]
    public Guid SignedBy { get; set; }

    [Display(Name = "تاريخ التوقيع")]
    [Required]
    public DateTime SignedAt { get; set; }

    [Display(Name = "نوع التوقيع")]
    public SignatureType SignatureType { get; set; }

    [Display(Name = "بيانات التوقيع")]
    public string? SignatureData { get; set; }

    public Contract Contract { get; set; } = null!;
}
