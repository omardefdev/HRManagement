namespace HRManagement.ServiceDefaults.ExtTreasury;

public sealed class ExtTreasuryOptions
{
    public const string SectionName = "ExtTreasury";

    public bool EnforceOnStartup { get; set; }
    public bool LiveTreasuryClaim { get; set; }
    public string? MatrixPath { get; set; }
}
