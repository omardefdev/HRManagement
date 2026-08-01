namespace HRManagement.ServiceDefaults.SovVendor;

public sealed class SovVendorOptions
{
    public const string SectionName = "SovVendor";

    public bool EnforceOnStartup { get; set; }
    public bool LiveVendorClaim { get; set; }
    public string? MatrixPath { get; set; }
}
