namespace HRManagement.ServiceDefaults.SovZt;

public sealed class SovZtOptions
{
    public const string SectionName = "SovZt";

    public bool EnforceOnStartup { get; set; }
    public bool LivePamClaim { get; set; }
    public string? MatrixPath { get; set; }
}
