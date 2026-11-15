namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-67: one HR.Core host (<see cref="Hcm025ScopeAndPolicyTestHost"/>) for the classes that need one and keep their data apart by
/// their own ids. Each host hands EF its own interceptors, so each adds an internal service provider, and EF throws past twenty in
/// one test process: the suite sat at that edge. The classes of one collection run one after another.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SharedHrCoreHost : ICollectionFixture<Hcm025ScopeAndPolicyTestHost>
{
    public const string Name = "Shared HR.Core host";
}
