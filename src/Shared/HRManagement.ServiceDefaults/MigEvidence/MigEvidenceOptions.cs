namespace HRManagement.ServiceDefaults.MigEvidence;

public sealed class MigEvidenceOptions
{
    public const string SectionName = "MigEvidence";

    public bool EnforceOnStartup { get; set; }
    public bool LiveAgencyReconClaim { get; set; }
    public string? MatrixPath { get; set; }
}
