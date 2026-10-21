using System.ComponentModel.DataAnnotations;

namespace HR.Contracts.Domain.Enums;

public enum SignatureType
{
    [Display(Name = "توقيع رقمي")]
    Digital,
    [Display(Name = "توقيع يدوي")]
    Physical
}
