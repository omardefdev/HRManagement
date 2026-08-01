using System.Text.Json;
using HRManagement.SharedKernel.MasterData;

namespace HRManagement.ServiceDefaults.MasterData;

/// <summary>MDM-002: merge/unmerge result with full provenance.</summary>
public sealed class MergeResult
{
    public bool Success { get; init; }
    public Guid SurvivorPersonId { get; init; }
    public Guid RetiredPersonId { get; init; }
    public string? Error { get; init; }
}

/// <summary>MDM-002: reversible person merge/unmerge operations.</summary>
public interface IPersonMergeService
{
    MergeResult Merge(GoldenPersonRecord survivor, GoldenPersonRecord duplicate, string actor);
    MergeResult Unmerge(GoldenPersonRecord survivor, Guid originalPersonId, string actor);
}

/// <summary>
/// MDM-002: domain service implementing reversible person merge.
/// Merge marks the duplicate as <see cref="GoldenPersonStatus.Merged"/> and
/// appends provenance; unmerge restores it to <see cref="GoldenPersonStatus.Active"/>.
/// </summary>
public sealed class PersonMergeService : IPersonMergeService
{
    public MergeResult Merge(GoldenPersonRecord survivor, GoldenPersonRecord duplicate, string actor)
    {
        ArgumentNullException.ThrowIfNull(survivor);
        ArgumentNullException.ThrowIfNull(duplicate);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        if (survivor.Id == duplicate.Id)
            return new MergeResult { Success = false, Error = "Cannot merge a record with itself." };

        if (duplicate.Status == GoldenPersonStatus.Merged)
            return new MergeResult { Success = false, Error = "Record is already merged." };

        duplicate.Status = GoldenPersonStatus.Merged;
        duplicate.SurvivorPersonId = survivor.PersonId;
        duplicate.UpdatedAtUtc = DateTime.UtcNow;

        var entry = new
        {
            action = "merge",
            actor,
            timestamp = DateTime.UtcNow,
            survivorPersonId = survivor.PersonId,
            retiredPersonId = duplicate.PersonId
        };
        AppendMergeHistory(survivor, entry);
        AppendMergeHistory(duplicate, entry);

        return new MergeResult
        {
            Success = true,
            SurvivorPersonId = survivor.PersonId,
            RetiredPersonId = duplicate.PersonId
        };
    }

    public MergeResult Unmerge(GoldenPersonRecord survivor, Guid originalPersonId, string actor)
    {
        ArgumentNullException.ThrowIfNull(survivor);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        var history = DeserializeHistory(survivor.MergeHistory);
        var mergeEntry = history
            .LastOrDefault(h => h.TryGetProperty("retiredPersonId", out var rp)
                             && rp.GetGuid() == originalPersonId
                             && h.TryGetProperty("action", out var a)
                             && a.GetString() == "merge");

        if (mergeEntry.ValueKind == JsonValueKind.Undefined)
            return new MergeResult { Success = false, Error = "No merge record found for unmerge." };

        var unmergeEntry = new
        {
            action = "unmerge",
            actor,
            timestamp = DateTime.UtcNow,
            restoredPersonId = originalPersonId,
            survivorPersonId = survivor.PersonId
        };
        AppendMergeHistory(survivor, unmergeEntry);

        return new MergeResult
        {
            Success = true,
            SurvivorPersonId = survivor.PersonId,
            RetiredPersonId = originalPersonId
        };
    }

    private static void AppendMergeHistory(GoldenPersonRecord record, object entry)
    {
        var history = string.IsNullOrWhiteSpace(record.MergeHistory)
            ? new List<object>()
            : JsonSerializer.Deserialize<List<object>>(record.MergeHistory) ?? new List<object>();

        history.Add(entry);
        record.MergeHistory = JsonSerializer.Serialize(history);
    }

    private static List<JsonElement> DeserializeHistory(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<JsonElement>();

        return JsonSerializer.Deserialize<List<JsonElement>>(json) ?? new List<JsonElement>();
    }
}
