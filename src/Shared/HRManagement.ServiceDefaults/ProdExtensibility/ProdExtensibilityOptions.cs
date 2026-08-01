namespace HRManagement.ServiceDefaults.ProdExtensibility;

public sealed class ProdExtensibilityOptions
{
    public const string SectionName = "ProdExtensibility";

    public bool EnforceOnStartup { get; set; }
    public bool LiveExtensibilityClaim { get; set; }
    public string? MatrixPath { get; set; }
}
