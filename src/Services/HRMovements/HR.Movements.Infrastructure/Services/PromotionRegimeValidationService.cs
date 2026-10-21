using HRManagement.SharedKernel.Legal;
using HR.Movements.Domain.Entities;

namespace HR.Movements.Infrastructure.Services;

/// <summary>SYR-009: validates promotion against service regime rules.</summary>
public static class PromotionRegimeValidationService
{
    public static string? Validate(PromotionRequest request) =>
        ServiceRegimePromotionRules.ValidatePromotion(
            request.SourceLegalRegimeId,
            request.FromGrade,
            request.ToGrade,
            request.FromStep,
            request.ToStep,
            request.TransitionDecisionRef);
}
