using System.ComponentModel.DataAnnotations;
using HR.Contracts.Domain.Enums;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace HR.Contracts.Web.ViewModels;

public class ContractSignatureCreateViewModel
{
    public Guid ContractId { get; set; }

    [Display(Name = "الموقّع")]
    [BindNever]
    [ValidateNever]
    public Guid SignedBy { get; set; }

    [Display(Name = "تاريخ التوقيع")]
    [Required]
    public DateTime SignedAt { get; set; } = DateTime.UtcNow;

    [Display(Name = "نوع التوقيع")]
    public SignatureType SignatureType { get; set; }

    [Display(Name = "بيانات التوقيع")]
    public string? SignatureData { get; set; }
}
