namespace HRManagement.ServiceDefaults.MigCutover;

public sealed class MigCutoverOptions
{
    public const string SectionName = "MigCutover";

    public bool EnforceOnStartup { get; set; }
    public bool LiveAgencyCutoverClaim { get; set; }
    public string? MatrixPath { get; set; }
}
