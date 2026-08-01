namespace HRManagement.ServiceDefaults.MigOnboarding;

public interface IMigOnboardingProvider
{
    MigOnboardingOptions Options { get; }
    MigOnboardingMatrixDocument Matrix { get; }
}

public interface IAgencyOnboardingClaimGuard
{
    string? EnsureLiveDualAgencyOnboardAllowed();
    string? ValidateNofork(bool proposedFork);
    string? ValidateWaveSignOffs(bool waveStarted, bool signOffsComplete);
    string? ValidateOwners(IEnumerable<string> presentOwners);
    string? ValidateLeadTime(int days, int? targetOverride = null);
    string? ValidateDefects(int count, int? budgetOverride = null);
    string? ValidateLivePortalClaim(bool livePortal);
    string? ValidateSigning(MigOnboardingSigning? meta);
}
