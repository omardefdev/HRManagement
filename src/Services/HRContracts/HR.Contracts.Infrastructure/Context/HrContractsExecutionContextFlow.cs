namespace HR.Contracts.Infrastructure.Context;

public static class HrContractsExecutionContextFlow
{
    private static readonly AsyncLocal<string?> CorrelationCurrent = new();
    private static readonly AsyncLocal<string?> SourceSystemCurrent = new();

    public static void Set(string? correlationId, string? sourceSystem)
    {
        CorrelationCurrent.Value = correlationId;
        SourceSystemCurrent.Value = sourceSystem;
    }

    public static string? GetCorrelationId() => CorrelationCurrent.Value;
    public static string? GetSourceSystem() => SourceSystemCurrent.Value;
}
