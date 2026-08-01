namespace HRManagement.ServiceDefaults.ProdWorkbox;

public sealed class ProdWorkboxOptions
{
    public const string SectionName = "ProdWorkbox";

    public bool EnforceOnStartup { get; set; }
    public bool LiveWorkboxClaim { get; set; }
    public string? MatrixPath { get; set; }
}
