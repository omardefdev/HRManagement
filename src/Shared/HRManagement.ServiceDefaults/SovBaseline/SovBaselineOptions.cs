namespace HRManagement.ServiceDefaults.SovBaseline;

public sealed class SovBaselineOptions
{
    public const string SectionName = "SovBaseline";

    public bool EnforceOnStartup { get; set; }
    public bool LiveAtoClaim { get; set; }
    public string? MatrixPath { get; set; }
}
