namespace HRManagement.ServiceDefaults.ProdAgency;

public sealed class ProdAgencyOptions
{
    public const string SectionName = "ProdAgency";

    public bool EnforceOnStartup { get; set; }
    public bool LiveAgencySelfAdminClaim { get; set; }
    public string? MatrixPath { get; set; }
}
