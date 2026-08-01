using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dev.Analytics.Domain.Entities;

namespace Dev.Analytics.Domain.Services;

public static class AnalyticsProjectionRules
{
    public static string IdempotencyKey(string eventName, string referenceKey) =>
        $"{eventName}:{referenceKey}";

    public static bool AcceptLateEvent(DateTime existingOccurredAt, DateTime incomingOccurredAt) =>
        incomingOccurredAt <= existingOccurredAt.AddDays(30) || incomingOccurredAt > existingOccurredAt;

    public static string Fingerprint(IEnumerable<AnalyticsEvent> events)
    {
        var ordered = events
            .OrderBy(e => e.OccurredAt)
            .ThenBy(e => e.Id)
            .Select(e => $"{e.Id:N}|{e.EventName}|{e.OrgUnitId?.ToString("N")}|{e.OccurredAt:O}");
        var raw = string.Join("\n", ordered);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }

    public static string BuildHeadcountProjection(IEnumerable<AnalyticsEvent> events, Guid? orgUnitId)
    {
        var q = events.AsEnumerable();
        if (orgUnitId.HasValue)
            q = q.Where(e => e.OrgUnitId == orgUnitId);
        var created = q.Count(e => e.EventName == "EmployeeCreated");
        var separated = q.Count(e => e.EventName == "SeparationCompleted");
        var active = Math.Max(0, created - separated);
        return JsonSerializer.Serialize(new
        {
            orgUnitId,
            created,
            separated,
            active,
            fingerprint = Fingerprint(q)
        });
    }
}

public static class BranchReportScopeRules
{
    /// <summary>Branch-scoped callers must supply orgUnitId matching their root; never get tenant-wide totals.</summary>
    public static bool AllowsTenantWide(bool isBranchScoped) => !isBranchScoped;

    public static bool OrgMatchesBranch(Guid? requestedOrg, Guid branchRoot) =>
        requestedOrg.HasValue && requestedOrg.Value == branchRoot;
}
