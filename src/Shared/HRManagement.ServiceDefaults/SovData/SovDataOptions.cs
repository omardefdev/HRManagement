namespace HRManagement.ServiceDefaults.SovData;

public sealed class SovDataOptions
{
    public const string SectionName = "SovData";

    public bool EnforceOnStartup { get; set; }
    public bool LiveSovereigntyClaim { get; set; }
    public string? MatrixPath { get; set; }
}
