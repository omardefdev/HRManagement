using Admin.Logistics.Domain.Entities;

namespace Admin.Logistics.Domain.Services;

public static class AssetOriginRules
{
    public const int MinLegacyReasonLength = 5;

    public static bool RequiresOriginReceipt(Asset asset) => !asset.IsLegacyEntry;

    public static bool IsValidOrigin(Asset asset)
    {
        if (asset.IsLegacyEntry)
            return !string.IsNullOrWhiteSpace(asset.LegacyEntryReason)
                   && asset.LegacyEntryReason.Trim().Length >= MinLegacyReasonLength
                   && !asset.OriginReceiptRecordId.HasValue;

        return asset.OriginReceiptRecordId.HasValue;
    }

    public static bool CanChangeOrigin(Asset existing, Asset updated)
    {
        if (existing.OriginReceiptRecordId == updated.OriginReceiptRecordId)
            return true;

        if (existing.OriginReceiptRecordId.HasValue && !updated.OriginReceiptRecordId.HasValue)
            return false;

        if (existing.IsLegacyEntry != updated.IsLegacyEntry)
            return false;

        if (existing.IsLegacyEntry && existing.LegacyEntryReason != updated.LegacyEntryReason)
            return false;

        return true;
    }

    public static bool IsGrandfatheredWithoutOrigin(Asset asset) =>
        !asset.IsLegacyEntry && !asset.OriginReceiptRecordId.HasValue;
}
