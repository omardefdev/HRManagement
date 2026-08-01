namespace HRManagement.ServiceDefaults;

/// <summary>نقل TenantId من طلب HTTP أو رسالة MassTransit إلى نفس السياق غير المتزامن.</summary>
public static class TenantIdFlow
{
    private static readonly AsyncLocal<Guid?> Current = new();

    public static void Set(Guid? tenantId) => Current.Value = tenantId;

    public static Guid? Get() => Current.Value;
}
