namespace HRManagement.ServiceDefaults.Ownership;

public sealed class OwnershipOptions
{
    public const string SectionName = "ProgramOwnership";

    public bool EnforceOnStartup { get; set; }
    public bool National24x7Claim { get; set; }
    public string? MatrixPath { get; set; }
}
