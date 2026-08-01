namespace HRManagement.ServiceDefaults.ExtCivil;

public sealed class ExtCivilOptions
{
    public const string SectionName = "ExtCivil";

    public bool EnforceOnStartup { get; set; }
    public bool LiveMoiClaim { get; set; }
    public string? MatrixPath { get; set; }
}
