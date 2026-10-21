using HRManagement.SharedKernel.Legal;

namespace HR.Core.Domain.Services;

/// <summary>SYR-003: maps offer legal source to assignable service regime.</summary>
public static class LegalSourceToRegimeMapper
{
    public static string? MapOfferToRegime(string? legalSourceId) =>
        legalSourceId switch
        {
            Syr001LegislationIds.Law50_2004 => "law50",
            Syr001LegislationIds.Law53_2021 => "law53",
            _ => null
        };
}
