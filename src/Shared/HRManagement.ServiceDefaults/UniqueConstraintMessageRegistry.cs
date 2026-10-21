using Microsoft.EntityFrameworkCore;

namespace HRManagement.ServiceDefaults;

public sealed record UniqueConstraintResolution(string Message, string? ModelStateKey);

/// <summary>Maps PostgreSQL unique constraint names to Arabic user messages (UX-004).</summary>
public static class UniqueConstraintMessageRegistry
{
    private static readonly Dictionary<string, UniqueConstraintResolution> ByConstraint = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IX_Assets_TenantId_TagNumber"] = new("رقم العهدة مستخدم مسبقاً في هذه الجهة", "TagNumber"),
        ["IX_Assets_TagNumber"] = new("رقم العهدة مستخدم مسبقاً", "TagNumber"),
        ["IX_InventoryItems_Sku"] = new("رمز SKU مستخدم مسبقاً", "Sku"),
        ["IX_InventoryItems_TenantId_Sku"] = new("رمز SKU مستخدم مسبقاً في هذه الجهة", "Sku"),
    };

    public static UniqueConstraintResolution Resolve(DbUpdateException? exception = null, string? constraintName = null)
    {
        constraintName ??= exception is not null && ConcurrencyExtensions.TryGetUniqueConstraintName(exception, out var name)
            ? name
            : null;

        if (!string.IsNullOrWhiteSpace(constraintName)
            && ByConstraint.TryGetValue(constraintName, out var mapped))
            return mapped;

        foreach (var entry in ByConstraint)
        {
            if (!string.IsNullOrWhiteSpace(constraintName)
                && constraintName.Contains(entry.Key, StringComparison.OrdinalIgnoreCase))
                return entry.Value;
        }

        return new(ConcurrencyConflictMessages.UniqueViolation, null);
    }

    public static string MessageForTagNumber => ByConstraint["IX_Assets_TenantId_TagNumber"].Message;

    public static string MessageForSku => ByConstraint["IX_InventoryItems_TenantId_Sku"].Message;
}
