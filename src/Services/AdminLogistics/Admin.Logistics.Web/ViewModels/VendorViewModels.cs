using Admin.Logistics.Domain.Entities;
using HRManagement.SharedKernel.ViewModels;

namespace Admin.Logistics.Web.ViewModels;

public sealed class VendorIndexViewModel
{
    public PaginatedList<Vendor> Items { get; init; } = PaginatedList<Vendor>.Empty();
    public string? Q { get; init; }
    public string? Country { get; init; }
    public bool HasContactOnly { get; init; }
    public bool MissingContactOnly { get; init; }
}
