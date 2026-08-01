namespace HRManagement.ServiceDefaults.ProgramGovernance;

public interface IProgramGovernanceProvider
{
    ProgramGovernanceOptions Options { get; }
    GovernanceMatrixDocument Matrix { get; }
    DecisionLogDocument DecisionLog { get; }
    RiskRegisterDocument RiskRegister { get; }
    GoLiveEvidenceDocument? GoLiveEvidence { get; }
}

public interface IGoLiveSeatGuard
{
    string? ValidateEvidence(GoLiveEvidenceDocument? evidence);
    string? EnsureGoLiveAllowed();
}
