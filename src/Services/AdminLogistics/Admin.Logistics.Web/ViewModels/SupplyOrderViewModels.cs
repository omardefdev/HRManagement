using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using HRManagement.SharedKernel.ViewModels;

namespace Admin.Logistics.Web.ViewModels;

public sealed class SupplyOrderIndexViewModel
{
    public PaginatedList<SupplyOrder> Items { get; init; } = PaginatedList<SupplyOrder>.Empty();
    public string? Q { get; init; }
    public SupplyOrderStatus? Status { get; init; }
    public Guid? RequestedBy { get; init; }
    public string? FilterRequesterName { get; init; }
    public Guid? OrgUnitId { get; init; }
    public DateTime? OrderedFrom { get; init; }
    public DateTime? OrderedTo { get; init; }
    public DateTime? FulfilledFrom { get; init; }
    public DateTime? FulfilledTo { get; init; }
    public bool ActiveOnly { get; init; }
    public bool AwaitingReceiptOnly { get; init; }
}
