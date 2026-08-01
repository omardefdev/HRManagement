namespace HRManagement.ServiceDefaults.ExtFederation;

public sealed class ExtFederationOptions
{
    public const string SectionName = "ExtFederation";

    public bool EnforceOnStartup { get; set; }
    public bool LiveFederationClaim { get; set; }
    public string? MatrixPath { get; set; }
}
