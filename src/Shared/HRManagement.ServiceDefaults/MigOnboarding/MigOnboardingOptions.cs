namespace HRManagement.ServiceDefaults.MigOnboarding;

public sealed class MigOnboardingOptions
{
    public const string SectionName = "MigOnboarding";

    public bool EnforceOnStartup { get; set; }
    public bool LiveDualAgencyOnboardClaim { get; set; }
    public string? MatrixPath { get; set; }
}
