namespace HRManagement.ServiceDefaults.ExtSector;

public sealed class ExtSectorOptions
{
    public const string SectionName = "ExtSector";

    public bool EnforceOnStartup { get; set; }
    public bool LiveSectorCatalogClaim { get; set; }
    public string? MatrixPath { get; set; }
}
