namespace HRManagement.ServiceDefaults.NoFork;

public sealed class NoForkOptions
{
    public const string SectionName = "NoFork";

    public bool EnforceOnStartup { get; set; }
    public bool ResolveAgencyPacks { get; set; }
    public string? MatrixPath { get; set; }
    public string? AgencyPacksDirectory { get; set; }

    /// <summary>Optional override; must match matrix releaseDigest when set.</summary>
    public string? ActiveReleaseDigest { get; set; }
}
