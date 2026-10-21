using HRManagement.SharedKernel.Legal;
using HR.Movements.Domain.Entities;

namespace HR.Movements.Infrastructure.Services;

/// <summary>SYR-008: validates service regime fields on movement requests.</summary>
public static class MovementRegimeValidationService
{
    public const string TransferMovementCode = "transfer";

    public static string? ValidateTransferRegimeFields(TransferRequest transfer)
    {
        var preserveError = ServiceRegimeMovementRules.ValidateRegimeChangeFields(
            TransferMovementCode,
            transfer.TargetLegalRegimeId,
            transfer.TransitionDecisionRef);
        if (preserveError is not null)
            return preserveError;

        return ServiceRegimeMovementRules.ValidateCrossRegimeDecisionRequired(
            transfer.SourceLegalRegimeId,
            transfer.TargetLegalRegimeId,
            transfer.TransitionDecisionRef);
    }

    public static string? ValidatePreservingMovement(string movementCode, string? targetRegimeId) =>
        ServiceRegimeMovementRules.ValidateRegimeChangeFields(movementCode, targetRegimeId, null);

    public static bool IsCrossRegimeTransfer(TransferRequest transfer) =>
        !string.IsNullOrWhiteSpace(transfer.TargetLegalRegimeId)
        && !string.Equals(
            transfer.SourceLegalRegimeId,
            transfer.TargetLegalRegimeId,
            StringComparison.OrdinalIgnoreCase);
}
