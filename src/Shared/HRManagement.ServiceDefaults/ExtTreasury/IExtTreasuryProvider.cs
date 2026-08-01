namespace HRManagement.ServiceDefaults.ExtTreasury;

public interface IExtTreasuryProvider
{
    ExtTreasuryOptions Options { get; }
    ExtTreasuryMatrixDocument Matrix { get; }
}

public interface ITreasuryPaymentClaimGuard
{
    string? EnsureLiveTreasuryAllowed();
    string? ValidateApprovedRun(string runStatus);
    string? ValidateIdempotency(bool duplicateSubmit, bool secondBusinessEffect);
    string? ValidateTotals(decimal orderTotal, decimal settledTotal, bool lineCountMatch);
    string? ValidateRejectCorrection(bool isReject, bool correctionQueued);
    string? ValidateManualFile(bool audited);
    string? ValidateCutoff(bool withinCutoff, bool contingencyHold = false);
    string? ValidateLivePkiClaim(bool livePki);
    string? ValidateAuditCompleteness(IEnumerable<string> presentFields);
    string? ValidateSigning(ExtTreasurySigning? meta);
}
