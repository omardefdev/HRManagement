namespace HRManagement.ServiceDefaults.ExtPki;

public sealed class ExtPkiOptions
{
    public const string SectionName = "ExtPki";

    public bool EnforceOnStartup { get; set; }
    public bool LivePkiClaim { get; set; }
    public string? MatrixPath { get; set; }
}
