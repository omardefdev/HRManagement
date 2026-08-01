namespace HRManagement.ServiceDefaults.ArcCriticality;

public sealed class ArcCriticalityOptions
{
    public const string SectionName = "ArcCriticality";

    public bool EnforceOnStartup { get; set; }
    public bool LiveCriticalityClaim { get; set; }
    public string? MatrixPath { get; set; }
}
