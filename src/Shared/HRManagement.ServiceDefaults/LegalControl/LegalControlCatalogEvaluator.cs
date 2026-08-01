using System.Text;

namespace HRManagement.ServiceDefaults.LegalControl;

/// <summary>Shared catalog release rules used by startup validator and tests.</summary>
public static class LegalControlCatalogEvaluator
{
    /// <summary>Marker split so source scanners do not treat this assembly as an unresolved placeholder.</summary>
    public static readonly string UnresolvedLegalMarker = "LEGAL" + "_TBD";

    public static readonly string[] CriticalNationalDomains =
    [
        "payroll",
        "tax",
        "privacy",
        "signature_evidence",
        "sovereignty"
    ];

    public static readonly string[] PayrollCriticalRequirementIds =
    [
        "CTRL-PAY-NO-LEGAL-TBD",
        "CTRL-PAY-LD-REGISTER"
    ];

    public static List<string> Evaluate(
        ControlCatalogDocument catalog,
        string releaseProfile,
        bool allowBlockedLegalNationalClaim,
        LegislativeWatchStamp? watchStamp = null,
        DateTime? utcNow = null,
        bool evaluateLegislativeWatch = false)
    {
        var failures = new List<string>();
        var activeExceptions = catalog.Exceptions
            .Where(e => string.Equals(e.Status, "Active", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.ExceptionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var c in catalog.Controls)
        {
            if (string.Equals(c.Status, "Implemented", StringComparison.OrdinalIgnoreCase)
                && ControlBlobContainsUnresolvedLegalMarker(c))
            {
                failures.Add($"{c.RequirementId}: Implemented must not reference {UnresolvedLegalMarker}");
            }

            if (string.Equals(c.Status, "Partial", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(c.ExceptionId)
                    || !activeExceptions.Contains(c.ExceptionId))
                {
                    failures.Add($"{c.RequirementId}: Partial requires an Active exceptionId");
                }
            }

            if (string.Equals(c.Confidence, "D", StringComparison.OrdinalIgnoreCase))
            {
                var okD = string.Equals(c.Status, "Blocked-Legal", StringComparison.OrdinalIgnoreCase)
                          || (!string.IsNullOrWhiteSpace(c.ExceptionId)
                              && activeExceptions.Contains(c.ExceptionId));
                if (!okD)
                    failures.Add($"{c.RequirementId}: confidence D requires Blocked-Legal or Active exception");
            }

            if (!string.IsNullOrWhiteSpace(c.ExceptionId)
                && catalog.Exceptions.All(e =>
                    !string.Equals(e.ExceptionId, c.ExceptionId, StringComparison.OrdinalIgnoreCase)))
            {
                failures.Add($"{c.RequirementId}: unknown exceptionId {c.ExceptionId}");
            }
        }

        var isNational = string.Equals(releaseProfile, "NationalClaim", StringComparison.OrdinalIgnoreCase);
        if (isNational && !allowBlockedLegalNationalClaim)
        {
            foreach (var c in catalog.Controls)
            {
                if (!string.Equals(c.Status, "Blocked-Legal", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!CriticalNationalDomains.Contains(c.Domain, StringComparer.OrdinalIgnoreCase))
                    continue;
                failures.Add(
                    $"{c.RequirementId}: NationalClaim forbidden while Blocked-Legal remains in critical domain '{c.Domain}' (set AllowBlockedLegalNationalClaim)");
            }
        }

        if (evaluateLegislativeWatch)
        {
            if (watchStamp is null)
            {
                failures.Add("legislative-watch-stamp.json missing");
            }
            else if (!DateTime.TryParse(watchStamp.NextDue, out var nextDue))
            {
                failures.Add($"legislative watch nextDue unparseable: '{watchStamp.NextDue}'");
            }
            else
            {
                var now = utcNow ?? DateTime.UtcNow;
                if (nextDue.ToUniversalTime().Date < now.Date)
                    failures.Add($"legislative watch overdue: nextDue={watchStamp.NextDue}");
            }
        }

        return failures;
    }

    public static bool ControlBlobContainsUnresolvedLegalMarker(CatalogControl c)
    {
        // Align with pack gate: evidence + test refs only (titles may describe the prohibition).
        var sb = new StringBuilder();
        sb.Append(c.EvidencePath).Append(' ').Append(c.TestRef);
        return sb.ToString().Contains(UnresolvedLegalMarker, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsCapabilityAllowed(
        ControlCatalogDocument catalog,
        string requirementId,
        Func<string?, bool> isExceptionActive)
    {
        var c = catalog.Controls.FirstOrDefault(x =>
            string.Equals(x.RequirementId, requirementId, StringComparison.OrdinalIgnoreCase));
        if (c is null) return false;
        if (string.Equals(c.Status, "Blocked-Legal", StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.Equals(c.Status, "Implemented", StringComparison.OrdinalIgnoreCase))
            return !ControlBlobContainsUnresolvedLegalMarker(c);
        if (string.Equals(c.Status, "Partial", StringComparison.OrdinalIgnoreCase))
            return isExceptionActive(c.ExceptionId);
        return false;
    }

    public static string? ValidatePayrollCriticalControls(
        ControlCatalogDocument catalog,
        Func<string?, bool> isExceptionActive)
    {
        foreach (var id in PayrollCriticalRequirementIds)
        {
            var c = catalog.Controls.FirstOrDefault(x =>
                string.Equals(x.RequirementId, id, StringComparison.OrdinalIgnoreCase));
            if (c is null)
                return $"STR-002: missing critical payroll control {id}";
            if (string.Equals(c.Status, "Implemented", StringComparison.OrdinalIgnoreCase))
            {
                if (ControlBlobContainsUnresolvedLegalMarker(c))
                    return $"STR-002: {id} Implemented references {UnresolvedLegalMarker}";
                continue;
            }

            if (string.Equals(c.Status, "Partial", StringComparison.OrdinalIgnoreCase)
                && isExceptionActive(c.ExceptionId))
                continue;

            return $"STR-002: payroll blocked — {id} status={c.Status} (need Implemented or Partial with Active exception)";
        }

        var mof = catalog.Controls.FirstOrDefault(x =>
            string.Equals(x.RequirementId, "CTRL-TAX-SYD004-MOF", StringComparison.OrdinalIgnoreCase));
        if (mof is not null
            && string.Equals(mof.Status, "Blocked-Legal", StringComparison.OrdinalIgnoreCase)
            && IsCapabilityAllowed(catalog, "CTRL-TAX-SYD004-MOF", isExceptionActive))
        {
            return "STR-002: CTRL-TAX-SYD004-MOF must remain blocked until MoF gazette";
        }

        return null;
    }
}
