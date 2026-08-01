using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Compliance.Domain.Entities;

[Display(Name = "سجل إحالة قانونية")]
public class LegalReferral : BaseEntity
{
    [Display(Name = "الوحدة المصدرة")]
    public Guid? OriginatingUnit { get; set; }

    [Display(Name = "محال إلى")]
    public string? ReferredTo { get; set; }

    [Display(Name = "تاريخ الإحالة")]
    [DataType(DataType.Date)]
    public DateTime? ReferralDate { get; set; }

    [Display(Name = "المستندات")]
    public string? Documents { get; set; }

    [Display(Name = "الشكوى")]
    public Guid? ComplaintId { get; set; }

    public virtual Complaint? Complaint { get; set; }
}
