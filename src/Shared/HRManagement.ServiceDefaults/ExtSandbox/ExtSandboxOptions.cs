namespace HRManagement.ServiceDefaults.ExtSandbox;

public sealed class ExtSandboxOptions
{
    public const string SectionName = "ExtSandbox";

    public bool EnforceOnStartup { get; set; }
    public bool LiveSandboxPortalClaim { get; set; }
    public string? MatrixPath { get; set; }
}
