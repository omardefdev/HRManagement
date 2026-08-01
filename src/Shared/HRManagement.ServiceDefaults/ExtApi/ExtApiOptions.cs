namespace HRManagement.ServiceDefaults.ExtApi;

public sealed class ExtApiOptions
{
    public const string SectionName = "ExtApi";

    public bool EnforceOnStartup { get; set; }
    public bool LiveExchangeClaim { get; set; }
    public string? MatrixPath { get; set; }
}
