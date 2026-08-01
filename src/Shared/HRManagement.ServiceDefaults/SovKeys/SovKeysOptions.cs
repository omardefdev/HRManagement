namespace HRManagement.ServiceDefaults.SovKeys;

public sealed class SovKeysOptions
{
    public const string SectionName = "SovKeys";

    public bool EnforceOnStartup { get; set; }
    public bool LiveKeyCustodyClaim { get; set; }
    public string? MatrixPath { get; set; }
}
