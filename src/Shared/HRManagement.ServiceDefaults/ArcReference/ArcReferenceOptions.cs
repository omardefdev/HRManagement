namespace HRManagement.ServiceDefaults.ArcReference;

public sealed class ArcReferenceOptions
{
    public const string SectionName = "ArcReference";

    public bool EnforceOnStartup { get; set; }
    public bool LiveReferenceClaim { get; set; }
    public string? MatrixPath { get; set; }
}
