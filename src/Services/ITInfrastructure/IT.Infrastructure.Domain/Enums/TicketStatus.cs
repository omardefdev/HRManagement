using System.ComponentModel.DataAnnotations;

namespace IT.Infrastructure.Domain.Enums;

public enum TicketStatus
{
    [Display(Name = "مفتوح")]
    Open,
    [Display(Name = "مُسند")]
    Assigned,
    [Display(Name = "محلول")]
    Resolved,
    [Display(Name = "مغلق")]
    Closed,
}