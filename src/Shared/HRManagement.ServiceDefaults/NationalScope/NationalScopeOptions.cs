namespace HRManagement.ServiceDefaults.NationalScope;

public sealed class NationalScopeOptions
{
    public const string SectionName = "NationalScope";

    /// <summary>Active envelope id: syria-interim | national-competition.</summary>
    public string ActiveEnvelope { get; set; } = "syria-interim";

    public string CountryCode { get; set; } = "SY";
    public string CurrencyCode { get; set; } = "SYP";
    public string PrimaryCulture { get; set; } = "ar-SY";
    public int GovernorateCount { get; set; } = 14;

    /// <summary>Required to run Production with ActiveEnvelope=national-competition (after CAP-001).</summary>
    public bool AllowNationalCompetitionClaim { get; set; }

    /// <summary>Lab/dev scale marker; never treat as national proof.</summary>
    public bool IsLabScale { get; set; }

    /// <summary>Optional absolute/relative path to envelope-matrix.json.</summary>
    public string? MatrixPath { get; set; }

    /// <summary>When true, exceeding tenant/payroll ceilings fails hard; otherwise warn.</summary>
    public bool HardEnforceCeilings { get; set; }
}
