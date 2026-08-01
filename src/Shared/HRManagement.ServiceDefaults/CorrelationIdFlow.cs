namespace HRManagement.ServiceDefaults;

/// <summary>نقل CorrelationId من طلب HTTP إلى نشر MassTransit في نفس السياق غير المتزامن.</summary>
public static class CorrelationIdFlow
{
    private static readonly AsyncLocal<string?> Current = new();

    public static void Set(string? correlationId) => Current.Value = correlationId;

    public static string? Get() => Current.Value;
}
