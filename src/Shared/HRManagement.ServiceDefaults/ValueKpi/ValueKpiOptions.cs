namespace HRManagement.ServiceDefaults.ValueKpi;

public sealed class ValueKpiOptions
{
    public const string SectionName = "ProgramValue";

    public bool EnforceOnStartup { get; set; }
    public bool LiveDashboardClaim { get; set; }
    public string? MatrixPath { get; set; }
}
