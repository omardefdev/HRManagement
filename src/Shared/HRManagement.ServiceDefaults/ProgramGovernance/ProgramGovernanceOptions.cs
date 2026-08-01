namespace HRManagement.ServiceDefaults.ProgramGovernance;

public sealed class ProgramGovernanceOptions
{
    public const string SectionName = "ProgramGovernance";

    public bool EnforceOnStartup { get; set; }
    public bool GoLiveDeclared { get; set; }
    public string? MatrixPath { get; set; }
    public string? DecisionLogPath { get; set; }
    public string? RiskRegisterPath { get; set; }
    public string? GoLiveEvidencePath { get; set; }
}
