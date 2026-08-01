using System.ComponentModel.DataAnnotations;
using HR.Contracts.Domain.Enums;

namespace HR.Contracts.Web.ViewModels;

public class ContractSignatureCreateViewModel
{
    public Guid ContractId { get; set; }

    [Display(Name = "الموقّع")]
    [Required]
    public Guid SignedBy { get; set; }

    [Display(Name = "تاريخ التوقيع")]
    [Required]
    public DateTime SignedAt { get; set; } = DateTime.UtcNow;

    [Display(Name = "نوع التوقيع")]
    public SignatureType SignatureType { get; set; }

    [Display(Name = "بيانات التوقيع")]
    public string? SignatureData { get; set; }
}
