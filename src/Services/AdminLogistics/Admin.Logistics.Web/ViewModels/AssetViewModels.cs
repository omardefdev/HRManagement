using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using HRManagement.SharedKernel.ViewModels;

namespace Admin.Logistics.Web.ViewModels;

public sealed class AssetListItemViewModel
{
    public Guid Id { get; init; }
    public string TagNumber { get; init; } = default!;
    public string Name { get; init; } = default!;
    public string? Category { get; init; }
    public AssetStatus Status { get; init; }
    public bool IsPrivileged { get; init; }
    public bool IsCancelled { get; init; }
    public Guid? OpenHolderEmployeeId { get; init; }
    public string? OpenHolderEmployeeName { get; init; }
    public Guid? OriginReceiptRecordId { get; init; }
    public bool IsLegacyEntry { get; init; }
    public string OriginSummary { get; init; } = "—";
}

public sealed class AssetIndexViewModel
{
    public PaginatedList<AssetListItemViewModel> Items { get; init; } = PaginatedList<AssetListItemViewModel>.Empty();
    public string? Q { get; init; }
    public AssetStatus? Status { get; init; }
    public string? Category { get; init; }
    public string? Brand { get; init; }
    public Guid? HolderEmployeeId { get; init; }
    public string? FilterHolderEmployeeName { get; init; }
    public bool UnassignedOnly { get; init; }
    public bool AssignedOnly { get; init; }
    public bool PrivilegedOnly { get; init; }
    public string? OriginKind { get; init; }
    public DateTime? PurchaseFrom { get; init; }
    public DateTime? PurchaseTo { get; init; }
    public decimal? ValueMin { get; init; }
    public decimal? ValueMax { get; init; }
    public bool ActiveOnly { get; init; }
    public bool ShowCancelled { get; init; }
}

public sealed class AssetAssignmentIndexViewModel
{
    public PaginatedList<AssetAssignment> Items { get; init; } = PaginatedList<AssetAssignment>.Empty();
    public string? AssetQ { get; init; }
    public Guid? EmployeeId { get; init; }
    public string? FilterEmployeeName { get; init; }
    public AssetAssignmentStatus? Status { get; init; }
    public bool OpenOnly { get; init; }
    public bool OverdueOnly { get; init; }
    public DateTime? AssignedFrom { get; init; }
    public DateTime? AssignedTo { get; init; }
}

public sealed class AssignmentEvidenceViewModel
{
    public required AssetAssignment Assignment { get; init; }
    public Asset? Asset { get; init; }
}
