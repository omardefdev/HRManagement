namespace HRManagement.ServiceDefaults.NoFork;

public interface INoForkPolicyProvider
{
    NoForkOptions Options { get; }
    NoForkMatrixDocument Matrix { get; }
    string ReleaseDigest { get; }
    IReadOnlyList<AgencyConfigPack> LoadedPacks { get; }
}

public interface IAgencyConfigResolver
{
    AgencyConfigPack? TryResolve(Guid? tenantId);
    AgencyConfigPack? TryResolve(string? tenantId);
    IReadOnlyList<AgencyConfigPack> AllPacks { get; }
}

public interface IAgencyBrandingAccessor
{
    OrganizationBrandingOptions GetBranding();
}
