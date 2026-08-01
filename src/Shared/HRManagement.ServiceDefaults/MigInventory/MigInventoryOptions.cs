namespace HRManagement.ServiceDefaults.MigInventory;

public sealed class MigInventoryOptions
{
    public const string SectionName = "MigInventory";

    public bool EnforceOnStartup { get; set; }
    public bool LiveAgencyExtractClaim { get; set; }
    public string? MatrixPath { get; set; }
}
