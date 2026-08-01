namespace HRManagement.ServiceDefaults.ProdSemantic;

public sealed class ProdSemanticOptions
{
    public const string SectionName = "ProdSemantic";

    public bool EnforceOnStartup { get; set; }
    public bool LiveSemanticLayerClaim { get; set; }
    public string? MatrixPath { get; set; }
}
