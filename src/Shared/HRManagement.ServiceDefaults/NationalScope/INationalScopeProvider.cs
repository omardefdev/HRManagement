namespace HRManagement.ServiceDefaults.NationalScope;

public interface INationalScopeProvider
{
    NationalScopeOptions Options { get; }
    EnvelopeMatrixDocument Matrix { get; }
    string ActiveEnvelope { get; }

    EnvelopeIndicator? GetIndicator(string indicatorId);
    decimal? GetNumericCeiling(params string[] preferredIndicatorIds);

    /// <summary>Platform tenant ceiling (national.tenants).</summary>
    decimal? TenantCeiling { get; }

    /// <summary>Active+history persons ceiling for active envelope.</summary>
    decimal? PersonsCeiling { get; }

    /// <summary>Payroll window persons ceiling for active envelope.</summary>
    decimal? PayrollWindowPersonsCeiling { get; }

    string ResolvedCurrencyCode { get; }
}
