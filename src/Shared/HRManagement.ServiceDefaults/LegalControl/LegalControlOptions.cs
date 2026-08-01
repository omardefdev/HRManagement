namespace HRManagement.ServiceDefaults.LegalControl;

public sealed class LegalControlOptions
{
    public const string SectionName = "LegalControl";

    /// <summary>Interim (syria-interim style) or NationalClaim (requires no critical Blocked-Legal unless waived).</summary>
    public string ReleaseProfile { get; set; } = "Interim";

    /// <summary>When true, catalog rule violations fail host startup.</summary>
    public bool EnforceOnStartup { get; set; }

    /// <summary>Required to run NationalClaim while any critical-domain Blocked-Legal remains.</summary>
    public bool AllowBlockedLegalNationalClaim { get; set; }

    /// <summary>Optional absolute/relative path to control-catalog.json.</summary>
    public string? CatalogPath { get; set; }

    /// <summary>Optional path to legislative-watch-stamp.json.</summary>
    public string? LegislativeWatchStampPath { get; set; }

    /// <summary>When true (Production default), overdue legislative watch fails startup.</summary>
    public bool EnforceLegislativeWatch { get; set; } = true;
}
