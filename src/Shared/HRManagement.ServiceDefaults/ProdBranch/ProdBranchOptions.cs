namespace HRManagement.ServiceDefaults.ProdBranch;

public sealed class ProdBranchOptions
{
    public const string SectionName = "ProdBranch";

    public bool EnforceOnStartup { get; set; }
    public bool LiveRemoteBranchClaim { get; set; }
    public string? MatrixPath { get; set; }
}
