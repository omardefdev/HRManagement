namespace HRManagement.ServiceDefaults.SovThreat;

public sealed class SovThreatOptions
{
    public const string SectionName = "SovThreat";

    public bool EnforceOnStartup { get; set; }
    public bool LiveThreatModelClaim { get; set; }
    public string? MatrixPath { get; set; }
}
