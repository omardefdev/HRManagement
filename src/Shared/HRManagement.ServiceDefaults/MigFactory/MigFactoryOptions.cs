namespace HRManagement.ServiceDefaults.MigFactory;

public sealed class MigFactoryOptions
{
    public const string SectionName = "MigFactory";

    public bool EnforceOnStartup { get; set; }
    public bool LiveEtlWorkersClaim { get; set; }
    public string? MatrixPath { get; set; }
}
