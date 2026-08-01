using IT.Infrastructure.Domain.Entities;

namespace IT.Infrastructure.Domain.Services;

/// <summary>DEC-001: enforces DC-01..DC-08 ordering from decommission-checklist.json.</summary>
public static class DecommissionWorkflow
{
    public static readonly string[] Phases =
    [
        "DC-01", // Write-Freeze
        "DC-02", // Read-Only
        "DC-03", // Archive
        "DC-04", // Shadow Audit
        "DC-05", // Credential Revocation
        "DC-06", // License Termination
        "DC-07", // Media Destruction
        "DC-08"  // Rollback Window
    ];

    public static string MapSystemPhase(string checklistPhase) => checklistPhase switch
    {
        "DC-01" => "WriteFrozen",
        "DC-02" => "ReadOnly",
        "DC-03" => "Archived",
        "DC-07" or "DC-08" => "Decommissioned",
        _ => "ReadOnly"
    };

    public static void ApplyChecklistPhase(LegacySystemRecord system, string checklistPhaseId)
    {
        if (!Phases.Contains(checklistPhaseId))
            throw new InvalidOperationException($"Ù…Ø±Ø­Ù„Ø© ØºÙŠØ± Ù…Ø¹Ø±ÙˆÙØ©: {checklistPhaseId}");

        system.AdvanceTo(MapSystemPhase(checklistPhaseId));
    }

    public static void RejectWriteAfterCutoff(LegacySystemRecord system, DateTime nowUtc)
    {
        if (nowUtc >= system.CutoffDate.ToUniversalTime() || !system.WriteAllowed)
            system.EnsureWritableOrThrow();
    }
}
