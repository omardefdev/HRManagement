using System.Security.Cryptography;
using System.Text;
using Dev.Analytics.Application.Dtos;
using Dev.Analytics.Domain.Services;
using Dev.Analytics.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Dev.Analytics.Application.Services;

/// <summary>AC-19: detect drift in analytics read models, optionally correct, always document.</summary>
public sealed class ReadModelReconciliationService(
    AnalyticsDbContext db,
    IReadModelReconciliationStatusStore statusStore)
{
    public async Task<ReadModelReconciliationReport> ReconcileAsync(
        Guid readModelId,
        Guid? orgUnitId,
        bool dryRun,
        bool forceRebuild = false,
        CancellationToken ct = default)
    {
        var model = await db.ReadModels.FindAsync([readModelId], ct)
                    ?? throw new KeyNotFoundException($"ReadModel {readModelId:D} not found.");

        var beforeFingerprint = model.LastFingerprint ?? string.Empty;
        var beforeSchemaHash = HashSchema(model.Schema);

        var eventName = model.SourceEventName;
        var events = await db.AnalyticsEvents.AsNoTracking()
            .Where(e => eventName == null || e.EventName == eventName)
            .OrderBy(e => e.OccurredAt).ThenBy(e => e.Id)
            .ToListAsync(ct);

        var scopedEvents = orgUnitId.HasValue
            ? events.Where(e => e.OrgUnitId == orgUnitId).ToList()
            : events;

        var expectedProjection = AnalyticsProjectionRules.BuildHeadcountProjection(events, orgUnitId);
        var expectedFingerprint = AnalyticsProjectionRules.Fingerprint(scopedEvents);

        var driftDetected = forceRebuild
                            || !string.Equals(model.LastFingerprint, expectedFingerprint, StringComparison.Ordinal)
                            || !string.Equals(model.Schema, expectedProjection, StringComparison.Ordinal);

        var changes = new List<string>();
        if (!string.Equals(model.LastFingerprint, expectedFingerprint, StringComparison.Ordinal))
            changes.Add("LastFingerprint");
        if (!string.Equals(model.Schema, expectedProjection, StringComparison.Ordinal))
            changes.Add("Schema");

        var corrected = false;
        if (driftDetected && !dryRun)
        {
            model.Schema = expectedProjection;
            model.LastFingerprint = expectedFingerprint;
            model.LastRefreshed = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            corrected = true;
            if (!changes.Contains("LastRefreshed"))
                changes.Add("LastRefreshed");
        }

        var afterFingerprint = driftDetected ? expectedFingerprint : beforeFingerprint;
        var afterSchema = driftDetected ? expectedProjection : model.Schema;

        var report = new ReadModelReconciliationReport(
            readModelId,
            driftDetected,
            dryRun,
            corrected,
            beforeFingerprint,
            afterFingerprint,
            beforeSchemaHash,
            HashSchema(afterSchema),
            changes,
            DateTime.UtcNow);

        statusStore.Save(report);
        return report;
    }

    public static string? HashSchema(string? schema)
    {
        if (string.IsNullOrEmpty(schema))
            return null;

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(schema));
        return Convert.ToHexString(hash);
    }
}
