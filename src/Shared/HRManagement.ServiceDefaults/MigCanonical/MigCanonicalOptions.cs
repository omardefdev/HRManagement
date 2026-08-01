namespace HRManagement.ServiceDefaults.MigCanonical;

public sealed class MigCanonicalOptions
{
    public const string SectionName = "MigCanonical";

    public bool EnforceOnStartup { get; set; }
    public bool LiveCodeMapEngineClaim { get; set; }
    public string? MatrixPath { get; set; }
}
