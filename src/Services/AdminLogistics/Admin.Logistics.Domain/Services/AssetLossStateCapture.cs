using System.Text.Json;
using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;

namespace Admin.Logistics.Domain.Services;

public static class AssetLossStateCapture
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void CaptureBefore(
        Asset asset,
        IEnumerable<AssetAssignment> openAssignments,
        AssetLossReport report)
    {
        report.BeforeAssetStatus = asset.Status;
        report.BeforeStateJson = SerializeState(asset, openAssignments);
    }

    public static void CaptureAfter(
        Asset asset,
        IEnumerable<AssetAssignment> openAssignments,
        AssetLossReport report)
    {
        report.AfterAssetStatus = asset.Status;
        report.AfterStateJson = SerializeState(asset, openAssignments);
    }

    public static string SerializeState(Asset asset, IEnumerable<AssetAssignment> openAssignments) =>
        JsonSerializer.Serialize(new
        {
            assetStatus = asset.Status.ToString(),
            openAssignments = openAssignments
                .Where(a => a.ReturnedAt == null)
                .Select(a => new
                {
                    id = a.Id,
                    employeeId = a.EmployeeId,
                    status = a.Status.ToString()
                })
                .ToList()
        }, JsonOptions);
}
