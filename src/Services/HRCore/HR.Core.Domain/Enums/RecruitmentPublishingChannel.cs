using System.ComponentModel.DataAnnotations;

namespace HR.Core.Domain.Enums;

/// <summary>HCM-007: which intake channels a requisition supports.</summary>
public enum RecruitmentPublishingChannel
{
    [Display(Name = "عام فقط")]
    PublicOnly = 0,

    [Display(Name = "داخلي فقط")]
    InternalOnly = 1,

    [Display(Name = "عام وداخلي")]
    Both = 2
}
