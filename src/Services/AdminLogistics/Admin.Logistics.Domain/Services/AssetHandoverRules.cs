namespace Admin.Logistics.Domain.Services;

public static class AssetHandoverRules
{
    public static bool HasProof(string? evidenceRef, Guid? fileId)
    {
        if (fileId.HasValue) return true;
        if (string.IsNullOrWhiteSpace(evidenceRef)) return false;
        var s = evidenceRef.Trim();
        if (string.Equals(s, "موقّع", StringComparison.Ordinal) ||
            string.Equals(s, "موقع", StringComparison.OrdinalIgnoreCase))
            return false;
        return s.Length >= 2;
    }
}
