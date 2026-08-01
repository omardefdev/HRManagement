using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace IDCard.Domain.Entities;

[Display(Name = "سجل الطباعة والتسليم")]
public class CardDeliveryRecord : BaseEntity
{
    [Required]
    [Display(Name = "البطاقة")]
    public Guid IssuedCardId { get; set; }

    [Display(Name = "طبع بواسطة")]
    public Guid? PrintedBy { get; set; }

    [Display(Name = "تاريخ الطباعة")]
    public DateTime? PrintedAt { get; set; }

    [Display(Name = "سُلمت إلى")]
    public Guid? DeliveredTo { get; set; }

    [Display(Name = "تاريخ التسليم")]
    public DateTime? DeliveredAt { get; set; }

    [Display(Name = "إيصال الاستلام")]
    public string? SignatureOrReceipt { get; set; }

    /// <summary>FUN-005: FILE-002 delivery receipt.</summary>
    public Guid? ReceiptFileId { get; set; }
}
