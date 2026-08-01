namespace HRManagement.ServiceDefaults.ProdEss;

public sealed class ProdEssOptions
{
    public const string SectionName = "ProdEss";

    public bool EnforceOnStartup { get; set; }
    public bool LiveEmployeeSelfServiceClaim { get; set; }
    public string? MatrixPath { get; set; }
}
