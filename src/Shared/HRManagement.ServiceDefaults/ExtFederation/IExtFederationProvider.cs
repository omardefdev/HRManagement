namespace HRManagement.ServiceDefaults.ExtFederation;

public interface IExtFederationProvider
{
    ExtFederationOptions Options { get; }
    ExtFederationMatrixDocument Matrix { get; }
}

public interface IFederationAssuranceClaimGuard
{
    string? EnsureLiveFederationAllowed();
    string? ValidatePersonaAssurance(string personaId, string aal);
    string? ValidateNameOnlyLink(string linkKey);
    string? ValidateRevocationSla(int elapsedMinutes, bool escalated);
    string? ValidateLiveIdpClaim(bool nationalLive, bool agencyLive);
    string? ValidateSmsOtpForSensitive(string role, bool usedSmsOtp);
    string? ValidateCitizenEmployeeMix(bool sharedPrincipal);
    string? ValidateSigning(ExtFederationSigning? meta);
}
