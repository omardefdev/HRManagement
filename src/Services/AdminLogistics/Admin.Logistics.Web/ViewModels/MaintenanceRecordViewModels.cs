using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using HRManagement.SharedKernel.ViewModels;

namespace Admin.Logistics.Web.ViewModels;

public sealed class MaintenanceRecordIndexViewModel
{
    public PaginatedList<MaintenanceRecord> Items { get; init; } = PaginatedList<MaintenanceRecord>.Empty();
    public string? Q { get; init; }
    public Guid? AssetId { get; init; }
    public string? FilterAssetLabel { get; init; }
    public MaintenanceType? MaintenanceType { get; init; }
    public DateTime? PerformedFrom { get; init; }
    public DateTime? PerformedTo { get; init; }
}
