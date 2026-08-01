namespace HRManagement.ServiceDefaults.ProdPolicy;

public sealed class ProdPolicyOptions
{
    public const string SectionName = "ProdPolicy";

    public bool EnforceOnStartup { get; set; }
    public bool LivePolicyEngineClaim { get; set; }
    public string? MatrixPath { get; set; }
}
