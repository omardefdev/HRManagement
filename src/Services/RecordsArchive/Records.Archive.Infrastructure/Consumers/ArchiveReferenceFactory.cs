using System.Diagnostics.Metrics;
using HRManagement.ServiceDefaults.CodeGeneration;
using HRManagement.SharedKernel.CodeGeneration;
using Microsoft.EntityFrameworkCore;
using Records.Archive.Domain.Entities;
using Records.Archive.Domain.Enums;
using Records.Archive.Infrastructure.Data;

namespace Records.Archive.Infrastructure.Consumers;

/// <summary>PLAN-37 (§5): counts archive events that could not get a reference number (they are retried, not dropped).</summary>
public static class ArchiveReferenceMetrics
{
    public const string MeterName = "HRManagement.RecordsArchive";
    public const string FailuresCounterName = "archive_reference_failures_total";

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> Failures = Meter.CreateCounter<long>(
        FailuresCounterName,
        unit: "{event}",
        description: "Archive events whose reference number HR.Core could not issue; the message is retried, then dead-lettered.");
}

internal static class ArchiveReferenceFactory
{
    /// <summary>
    /// PLAN-37 (§5, G-COD-06): the archive reference is required. When HR.Core cannot issue it the call throws
    /// <see cref="CodeGenerationUnavailableException"/>, so the consumer retries and then moves the message to
    /// <c>_error</c>; it never acknowledges a message without an <see cref="ArchiveItem"/>.
    /// </summary>
    // The event type is a string (nameof), not a type argument: CodeQL reads a call named after a medical event type as
    // private data and then flags every log line that writes the reference (cs/exposure-of-sensitive-information).
    public static async Task<string> CreateReferenceAsync(
        ICodeGenerationClient codeClient,
        string entityPrefix,
        string eventType,
        CancellationToken ct)
    {
        try
        {
            var result = await codeClient.GenerateRequiredAsync(CodeGenerationRequests.ArchiveReference(entityPrefix), ct);
            return result.Code;
        }
        catch (CodeGenerationUnavailableException)
        {
            ArchiveReferenceMetrics.Failures.Add(1, new KeyValuePair<string, object?>("event_type", eventType));
            throw;
        }
    }

    public static async Task<bool> ExistsByTitleAsync(
        ArchiveDbContext db,
        string title,
        CancellationToken ct) =>
        await db.ArchiveItems.AnyAsync(x => x.Title == title, ct);

    public static ArchiveItem CreateItem(string title, string reference, string storageLocation, int retentionYears = 10) =>
        new()
        {
            Title = title,
            ReferenceNumber = reference,
            Format = ArchiveFormat.Digital,
            StorageLocation = storageLocation,
            RetentionPeriodYears = retentionYears
        };
}
