namespace HRManagement.ServiceDefaults.MigMatch;

public sealed class MigMatchOptions
{
    public const string SectionName = "MigMatch";

    public bool EnforceOnStartup { get; set; }
    public bool LiveIdentityMatchClaim { get; set; }
    public string? MatrixPath { get; set; }
}
