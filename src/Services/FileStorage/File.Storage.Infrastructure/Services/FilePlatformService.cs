using System.Security.Cryptography;
using File.Storage.Domain.Entities;
using File.Storage.Domain.Enums;
using File.Storage.Infrastructure.Data;
using HRManagement.FileStorage;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace File.Storage.Infrastructure.Services;

public sealed class FilePlatformService(
    FileStorageDbContext db,
    IFileStorageService storage,
    IMalwareScanner malwareScanner,
    IContentDisarmService contentDisarm)
{
    private static int MaxPurgeAttempts =>
        int.TryParse(Environment.GetEnvironmentVariable("FileStorage__GarbageCollection__MaxPurgeAttempts"), out var n) ? n : 12;

    private static int MaxScanAttempts =>
        int.TryParse(Environment.GetEnvironmentVariable("FileStorage__Scan__MaxAttempts"), out var n) ? n : 8;

    public async Task<FileRecord> UploadAsync(
        IFormFile file,
        FileUploadConstraints constraints,
        Guid? uploadedBy,
        CancellationToken cancellationToken = default)
    {
        var validation = FileUploadHelper.Validate(file, constraints);
        if (!validation.Success)
            throw new InvalidOperationException(validation.Error);

        await using var buffer = new MemoryStream();
        await using (var src = file.OpenReadStream())
            await src.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        var probe = await FileContentProbe.ProbeAsync(buffer, file.FileName, constraints, cancellationToken);
        if (!probe.Success)
            throw new InvalidOperationException(probe.Error ?? "رفض المحتوى.");

        buffer.Position = 0;
        var prefix = $"{DateTime.UtcNow:yyyy/MM}";
        var reference = await storage.UploadAsync(
            FileStorageBuckets.Quarantine,
            prefix,
            buffer,
            file.FileName,
            probe.DetectedContentType ?? file.ContentType,
            buffer.Length,
            cancellationToken);

        var extension = Path.GetExtension(file.FileName);
        if (extension.Length > 32)
            extension = extension[..32];

        var record = new FileRecord
        {
            Id = reference.FileId,
            TenantId = db.EffectiveTenantId,
            OriginalName = file.FileName,
            Extension = extension,
            MimeType = probe.DetectedContentType ?? reference.ContentType,
            DetectedContentType = probe.DetectedContentType,
            Size = reference.SizeBytes,
            Sha256 = reference.Checksum,
            Bucket = reference.Bucket,
            ObjectKey = reference.ObjectKey,
            UploadedBy = uploadedBy,
            UploadedAt = DateTime.UtcNow,
            Status = FileRecordStatus.Pending,
            ScanStatus = FileScanStatus.PendingScan,
            Visibility = FileVisibility.Private,
            ReferenceCount = 0,
            CreatedBy = uploadedBy
        };

        db.Files.Add(record);
        db.FileScanWorkItems.Add(new FileScanWorkItem
        {
            FileId = record.Id,
            Attempt = 0,
            NextAttemptAt = DateTime.UtcNow,
            Status = FileScanWorkItemStatus.Pending,
            TenantId = record.TenantId
        });
        await db.SaveChangesAsync(cancellationToken);
        return record;
    }

    public void EnsureDownloadAllowed(FileRecord record)
    {
        if (record.ScanStatus != FileScanStatus.Clean)
            throw new InvalidOperationException("الملف غير متاح للتحميل حتى يكتمل الفحص بنجاح (Clean).");
    }

    public async Task<FileRecord?> GetAsync(Guid fileId, CancellationToken cancellationToken = default) =>
        await db.Files.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == fileId && !x.IsDeleted, cancellationToken);

    public async Task<FileRecord?> GetIncludingDeletedAsync(Guid fileId, CancellationToken cancellationToken = default) =>
        await db.Files.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == fileId, cancellationToken);

    public async Task<IReadOnlyList<FileRecord>> GetBatchAsync(
        IEnumerable<Guid> fileIds,
        CancellationToken cancellationToken = default)
    {
        var ids = fileIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];

        return await db.Files.AsNoTracking()
            .Where(x => ids.Contains(x.Id) && !x.IsDeleted)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> RegisterReferenceAsync(
        Guid fileId,
        string holderKey,
        CancellationToken cancellationToken = default)
    {
        holderKey = holderKey.Trim();
        if (string.IsNullOrWhiteSpace(holderKey))
            throw new ArgumentException("holderKey is required.", nameof(holderKey));

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var exists = await db.FileReferences
            .AsNoTracking()
            .AnyAsync(x => x.FileId == fileId && x.HolderKey == holderKey, cancellationToken);
        if (exists)
        {
            var current = await db.Files.AsNoTracking()
                .Where(x => x.Id == fileId && !x.IsDeleted)
                .Select(x => x.ReferenceCount)
                .FirstOrDefaultAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return current;
        }

        var fileExists = await db.Files.AnyAsync(x => x.Id == fileId && !x.IsDeleted, cancellationToken);
        if (!fileExists)
            throw new KeyNotFoundException($"File {fileId} not found.");

        db.FileReferences.Add(new FileReferenceRecord
        {
            FileId = fileId,
            HolderKey = holderKey,
            CreatedAt = DateTime.UtcNow,
            TenantId = await db.Files.AsNoTracking()
                .Where(x => x.Id == fileId)
                .Select(x => x.TenantId)
                .FirstAsync(cancellationToken)
        });
        await db.SaveChangesAsync(cancellationToken);

        // Atomic increment (DATA-001) — avoids lost updates under concurrency.
        await db.Files
            .Where(x => x.Id == fileId && !x.IsDeleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.ReferenceCount, f => f.ReferenceCount + 1)
                .SetProperty(f => f.Status, f => f.Status == FileRecordStatus.Pending ? FileRecordStatus.Active : f.Status)
                .SetProperty(f => f.UpdatedAt, _ => DateTime.UtcNow), cancellationToken);

        var count = await db.Files.AsNoTracking()
            .Where(x => x.Id == fileId)
            .Select(x => x.ReferenceCount)
            .FirstAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);
        return count;
    }

    public async Task<int> ReleaseReferenceAsync(
        Guid fileId,
        string holderKey,
        CancellationToken cancellationToken = default)
    {
        holderKey = holderKey.Trim();
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var reference = await db.FileReferences
            .FirstOrDefaultAsync(x => x.FileId == fileId && x.HolderKey == holderKey, cancellationToken);

        if (reference is null)
        {
            var file = await db.Files.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == fileId, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return file?.ReferenceCount ?? 0;
        }

        db.FileReferences.Remove(reference);
        await db.SaveChangesAsync(cancellationToken);

        await db.Files
            .Where(x => x.Id == fileId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.ReferenceCount, f => f.ReferenceCount > 0 ? f.ReferenceCount - 1 : 0)
                .SetProperty(f => f.UpdatedAt, _ => DateTime.UtcNow), cancellationToken);

        var count = await db.Files.AsNoTracking()
            .Where(x => x.Id == fileId)
            .Select(x => x.ReferenceCount)
            .FirstAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);
        return count;
    }

    /// <summary>
    /// Phase 1 of two-phase delete: mark PendingPurge and enqueue purge work. Does not delete MinIO object.
    /// </summary>
    public async Task<bool> TryDeleteWhenUnreferencedAsync(
        Guid fileId,
        Guid? deletedBy,
        CancellationToken cancellationToken = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var record = await db.Files.FirstOrDefaultAsync(x => x.Id == fileId, cancellationToken);
        if (record is null || record.IsDeleted)
        {
            await tx.CommitAsync(cancellationToken);
            return record is not null;
        }

        var liveRefs = await db.FileReferences.CountAsync(x => x.FileId == fileId, cancellationToken);
        if (liveRefs > 0 || record.ReferenceCount > 0)
        {
            await tx.CommitAsync(cancellationToken);
            return false;
        }

        if (IsRetentionBlocked(record))
        {
            await tx.CommitAsync(cancellationToken);
            return false;
        }

        await EnqueuePendingPurgeAsync(record, deletedBy, forceReason: null, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Admin force-delete: requires reason; rejects legal hold and live refs unless overrideLiveRefs.
    /// Still uses two-phase purge (no immediate object delete).
    /// </summary>
    public async Task ForceDeleteAsync(
        Guid fileId,
        Guid? deletedBy,
        string reason,
        bool overrideLiveRefs = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Force delete requires an audit reason.", nameof(reason));

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var record = await db.Files.FirstOrDefaultAsync(x => x.Id == fileId, cancellationToken);
        if (record is null || record.IsDeleted)
        {
            await tx.CommitAsync(cancellationToken);
            return;
        }

        if (record.LegalHold)
            throw new InvalidOperationException("Cannot force-delete a file under legal hold.");

        var liveRefs = await db.FileReferences.CountAsync(x => x.FileId == fileId, cancellationToken);
        if ((liveRefs > 0 || record.ReferenceCount > 0) && !overrideLiveRefs)
            throw new InvalidOperationException("Cannot force-delete a file that still has live references.");

        if (overrideLiveRefs && (liveRefs > 0 || record.ReferenceCount > 0))
        {
            var refs = await db.FileReferences.Where(x => x.FileId == fileId).ToListAsync(cancellationToken);
            db.FileReferences.RemoveRange(refs);
            record.ReferenceCount = 0;
        }

        await EnqueuePendingPurgeAsync(record, deletedBy, reason.Trim(), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<int> CollectGarbageAsync(CancellationToken cancellationToken = default)
    {
        var orphanCutoff = DateTime.UtcNow.AddHours(-GetOrphanTtlHours());
        var graceCutoff = DateTime.UtcNow.AddHours(-GetReleaseGraceHours());

        var candidateIds = await db.Files
            .Where(x => !x.IsDeleted && x.ReferenceCount == 0 && !x.LegalHold)
            .Where(x => x.RetainUntil == null || x.RetainUntil <= DateTime.UtcNow)
            .Where(x =>
                (x.Status == FileRecordStatus.Pending && x.UploadedAt < orphanCutoff) ||
                (x.Status == FileRecordStatus.Active && x.UpdatedAt < graceCutoff))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var marked = 0;
        foreach (var id in candidateIds)
        {
            if (await TryDeleteWhenUnreferencedAsync(id, deletedBy: null, cancellationToken))
                marked++;
        }

        return marked;
    }

    /// <summary>Phase 2: process due purge work items with exponential backoff.</summary>
    public async Task<int> ProcessPurgeQueueAsync(CancellationToken cancellationToken = default, int batchSize = 50)
    {
        var now = DateTime.UtcNow;
        var items = await db.FilePurgeWorkItems
            .Where(x => x.Status == FilePurgeWorkItemStatus.Pending && x.NextAttemptAt <= now)
            .OrderBy(x => x.NextAttemptAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var succeeded = 0;
        foreach (var item in items)
        {
            var file = await db.Files.FirstOrDefaultAsync(x => x.Id == item.FileId, cancellationToken);
            if (file is null)
            {
                item.Status = FilePurgeWorkItemStatus.Succeeded;
                item.CompletedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                succeeded++;
                continue;
            }

            if (IsRetentionBlocked(file))
            {
                item.LastError = "Blocked by legal hold or RetainUntil.";
                item.NextAttemptAt = DateTime.UtcNow.AddHours(6);
                await db.SaveChangesAsync(cancellationToken);
                continue;
            }

            var liveRefs = await db.FileReferences.CountAsync(x => x.FileId == item.FileId, cancellationToken);
            if (liveRefs > 0 || file.ReferenceCount > 0)
            {
                // Safety: never delete object while live refs exist.
                item.LastError = "Aborted: live references present.";
                item.Status = FilePurgeWorkItemStatus.Dead;
                item.CompletedAt = DateTime.UtcNow;
                if (file.Status == FileRecordStatus.PendingPurge)
                {
                    file.IsDeleted = false;
                    file.DeletedAt = null;
                    file.Status = FileRecordStatus.Active;
                    file.ForceDeleteReason = null;
                }

                await db.SaveChangesAsync(cancellationToken);
                continue;
            }

            try
            {
                await storage.DeleteAsync(
                    new FileReference(file.Id, item.Bucket, item.ObjectKey, file.MimeType, file.Size, file.Sha256),
                    cancellationToken);

                item.Status = FilePurgeWorkItemStatus.Succeeded;
                item.CompletedAt = DateTime.UtcNow;
                item.LastError = null;
                file.Status = FileRecordStatus.Deleted;
                file.ObjectPurgedAt = DateTime.UtcNow;
                file.UpdatedAt = DateTime.UtcNow;
                succeeded++;
            }
            catch (Exception ex)
            {
                item.Attempt++;
                item.LastError = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                if (item.Attempt >= MaxPurgeAttempts)
                {
                    item.Status = FilePurgeWorkItemStatus.Dead;
                    item.CompletedAt = DateTime.UtcNow;
                }
                else
                {
                    var delaySeconds = Math.Min(3600, Math.Pow(2, Math.Min(item.Attempt, 10)));
                    item.NextAttemptAt = DateTime.UtcNow.AddSeconds(delaySeconds);
                }
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return succeeded;
    }

    public async Task<int> ReconcileReferenceCountsAsync(CancellationToken cancellationToken = default)
    {
        var drifts = await db.Files
            .Select(f => new
            {
                f.Id,
                f.ReferenceCount,
                Actual = db.FileReferences.Count(r => r.FileId == f.Id)
            })
            .Where(x => x.ReferenceCount != x.Actual)
            .ToListAsync(cancellationToken);

        var repaired = 0;
        foreach (var drift in drifts)
        {
            await db.Files
                .Where(x => x.Id == drift.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(f => f.ReferenceCount, drift.Actual)
                    .SetProperty(f => f.UpdatedAt, _ => DateTime.UtcNow), cancellationToken);
            repaired++;
        }

        return repaired;
    }

    public async Task<FileOrphanScanResult> ScanOrphansAsync(
        bool persistExceptions = true,
        CancellationToken cancellationToken = default)
    {
        var result = new FileOrphanScanResult();
        var approvedKeys = await db.FileReconcileExceptions.AsNoTracking()
            .Where(x => x.Approved)
            .Select(x => x.Kind + "|" + (x.FileId ?? Guid.Empty) + "|" + (x.Bucket ?? "") + "|" + (x.ObjectKey ?? ""))
            .ToListAsync(cancellationToken);
        var approved = approvedKeys.ToHashSet(StringComparer.Ordinal);

        var dbRows = await db.Files.AsNoTracking()
            .Select(x => new { x.Id, x.Bucket, x.ObjectKey, x.Status, x.IsDeleted, x.ObjectPurgedAt })
            .ToListAsync(cancellationToken);

        var buckets = dbRows.Select(x => x.Bucket).Distinct().ToList();
        if (!buckets.Contains(FileStorageBuckets.FileStorage))
            buckets.Add(FileStorageBuckets.FileStorage);

        var storageKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var bucket in buckets)
        {
            var keys = await storage.ListObjectKeysAsync(bucket, cancellationToken);
            foreach (var key in keys)
                storageKeys.Add($"{bucket}/{key}");
        }

        foreach (var row in dbRows)
        {
            var storageKey = $"{row.Bucket}/{row.ObjectKey}";
            var exists = storageKeys.Contains(storageKey);
            if (!exists && row.Status is FileRecordStatus.Pending or FileRecordStatus.Active)
            {
                result.DbWithoutObject++;
                MaybeAddException(result, approved, persistExceptions, "DbWithoutObject", row.Id, row.Bucket, row.ObjectKey,
                    "Active/Pending DB row has no storage object.");
            }
            else if (exists
                     && row.Status is FileRecordStatus.Deleted or FileRecordStatus.PendingPurge
                     && row.ObjectPurgedAt is not null)
            {
                // Object still present after successful purge mark — report as leftover.
                result.DeletedWithObject++;
                MaybeAddException(result, approved, persistExceptions, "DeletedWithObject", row.Id, row.Bucket, row.ObjectKey,
                    "Deleted/purged DB row still has storage object.");
            }
        }

        var dbKeySet = dbRows.Select(x => $"{x.Bucket}/{x.ObjectKey}").ToHashSet(StringComparer.Ordinal);
        foreach (var storageKey in storageKeys)
        {
            if (dbKeySet.Contains(storageKey))
                continue;

            var slash = storageKey.IndexOf('/');
            var bucket = slash > 0 ? storageKey[..slash] : storageKey;
            var objectKey = slash > 0 ? storageKey[(slash + 1)..] : storageKey;
            result.ObjectWithoutDb++;
            MaybeAddException(result, approved, persistExceptions, "ObjectWithoutDb", null, bucket, objectKey,
                "Storage object has no DB row.");
        }

        var countDrifts = await ReconcileReferenceCountsAsync(cancellationToken);
        result.RefCountDriftRepaired = countDrifts;

        if (persistExceptions)
            await db.SaveChangesAsync(cancellationToken);

        result.UnresolvedExceptions = result.Exceptions.Count(x => !x.Approved);
        return result;
    }

    public async Task ApproveReconcileExceptionAsync(
        Guid exceptionId,
        Guid? approvedBy,
        CancellationToken cancellationToken = default)
    {
        var row = await db.FileReconcileExceptions.FirstOrDefaultAsync(x => x.Id == exceptionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Reconcile exception {exceptionId} not found.");
        row.Approved = true;
        row.ApprovedBy = approvedBy;
        row.ApprovedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Verifies SHA256 of stored content. Fail-closed for Active files by default.
    /// </summary>
    public async Task VerifyChecksumAsync(
        FileRecord record,
        Stream content,
        bool failClosed = true,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(record.Sha256))
        {
            if (failClosed && record.Status == FileRecordStatus.Active)
                throw new InvalidOperationException("Active file has no stored checksum.");
            return;
        }

        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        var hash = Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
        if (!string.Equals(hash, record.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            if (failClosed || record.Status == FileRecordStatus.Active)
                throw new InvalidOperationException("Checksum mismatch for file content.");
        }

        buffer.Position = 0;
    }

    public async Task<(Stream Stream, string ContentType, string FileName)> OpenDownloadVerifiedAsync(
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        var record = await GetAsync(fileId, cancellationToken)
            ?? throw new KeyNotFoundException($"File {fileId} not found.");

        EnsureDownloadAllowed(record);

        var reference = new FileReference(record.Id, record.Bucket, record.ObjectKey, record.MimeType, record.Size, record.Sha256);
        await using var raw = await storage.OpenReadAsync(reference, cancellationToken);
        await using var buffer = new MemoryStream();
        await raw.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        var failClosed = !string.Equals(
            Environment.GetEnvironmentVariable("FileStorage__Checksum__FailClosed"),
            "false",
            StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(record.Sha256))
        {
            var hash = Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
            if (!string.Equals(hash, record.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                if (failClosed || record.Status == FileRecordStatus.Active)
                    throw new InvalidOperationException("Checksum mismatch for file content.");
            }
        }
        else if (failClosed && record.Status == FileRecordStatus.Active)
        {
            throw new InvalidOperationException("Active file has no stored checksum.");
        }

        buffer.Position = 0;
        var copy = new MemoryStream(buffer.ToArray());
        return (copy, record.MimeType, record.OriginalName);
    }

    /// <summary>Process malware scan queue: AV → CDR → promote to clean bucket (FILE-002).</summary>
    public async Task<int> ProcessScanQueueAsync(CancellationToken cancellationToken = default, int batchSize = 25)
    {
        var now = DateTime.UtcNow;
        var items = await db.FileScanWorkItems
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.BackgroundQueueProcessing)
            .Where(x => x.Status == FileScanWorkItemStatus.Pending && x.NextAttemptAt <= now)
            .OrderBy(x => x.NextAttemptAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var completed = 0;
        foreach (var item in items)
        {
            FileReference? obsoleteReference = null;
            var file = await db.Files
                .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.BackgroundQueueProcessing)
                .FirstOrDefaultAsync(x => x.Id == item.FileId, cancellationToken);
            if (file is null || file.IsDeleted)
            {
                item.Status = FileScanWorkItemStatus.Succeeded;
                item.CompletedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                completed++;
                continue;
            }

            try
            {
                var reference = new FileReference(file.Id, file.Bucket, file.ObjectKey, file.MimeType, file.Size, file.Sha256);
                await using var raw = await storage.OpenReadAsync(reference, cancellationToken);
                await using var buffer = new MemoryStream();
                await raw.CopyToAsync(buffer, cancellationToken);
                buffer.Position = 0;

                var outcome = await malwareScanner.ScanAsync(buffer, file.OriginalName, cancellationToken);
                file.ScannerName = outcome.ScannerName;
                file.ScannerVersion = outcome.ScannerVersion;
                file.ScannedAt = DateTime.UtcNow;

                if (outcome.Verdict == MalwareScanVerdict.Infected)
                {
                    file.ScanStatus = FileScanStatus.Infected;
                    file.ThreatName = outcome.ThreatName;
                    AppendScanResult(file, FileScanStatus.Infected, outcome, cdrApplied: false, cdrProfile: null, outcome.Detail);
                    item.Status = FileScanWorkItemStatus.Succeeded;
                    item.CompletedAt = DateTime.UtcNow;
                    completed++;
                    await db.SaveChangesAsync(cancellationToken);
                    continue;
                }

                if (outcome.Verdict == MalwareScanVerdict.Error)
                {
                    throw new InvalidOperationException(outcome.Detail ?? "Scanner error (fail-closed).");
                }

                // Clean → CDR → promote
                buffer.Position = 0;
                var disarm = await contentDisarm.DisarmAsync(
                    buffer, file.OriginalName, file.DetectedContentType ?? file.MimeType, cancellationToken);

                if (!disarm.Success || disarm.Content is null)
                {
                    file.ScanStatus = FileScanStatus.Quarantined;
                    file.ThreatName = disarm.Profile ?? "CDR-Policy";
                    file.CdrApplied = false;
                    file.CdrProfile = disarm.Profile;
                    AppendScanResult(file, FileScanStatus.Quarantined, outcome, false, disarm.Profile, disarm.Error);
                    item.Status = FileScanWorkItemStatus.Succeeded;
                    item.CompletedAt = DateTime.UtcNow;
                    completed++;
                    await db.SaveChangesAsync(cancellationToken);
                    continue;
                }

                await using (disarm.Content)
                {
                    var cleanKey = file.ObjectKey;
                    if (cleanKey.StartsWith("quarantine/", StringComparison.OrdinalIgnoreCase))
                        cleanKey = cleanKey["quarantine/".Length..];

                    disarm.Content.Position = 0;
                    var promoted = await storage.PutAsync(
                        FileStorageBuckets.FileStorage,
                        cleanKey,
                        file.Id,
                        disarm.Content,
                        disarm.ContentType,
                        disarm.SizeBytes,
                        cancellationToken);

                    var oldRef = reference;
                    file.Bucket = promoted.Bucket;
                    file.ObjectKey = promoted.ObjectKey;
                    file.Sha256 = promoted.Checksum ?? disarm.Sha256;
                    file.Size = promoted.SizeBytes;
                    file.MimeType = disarm.ContentType;
                    file.ScanStatus = FileScanStatus.Clean;
                    file.ThreatName = null;
                    file.CdrApplied = disarm.Applied;
                    file.CdrProfile = disarm.Profile;
                    file.UpdatedAt = DateTime.UtcNow;

                    AppendScanResult(file, FileScanStatus.Clean, outcome, disarm.Applied, disarm.Profile, null);

                    if (!string.Equals(oldRef.Bucket, promoted.Bucket, StringComparison.Ordinal)
                        || !string.Equals(oldRef.ObjectKey, promoted.ObjectKey, StringComparison.Ordinal))
                    {
                        obsoleteReference = oldRef;
                    }
                }

                item.Status = FileScanWorkItemStatus.Succeeded;
                item.CompletedAt = DateTime.UtcNow;
                item.LastError = null;
                completed++;
            }
            catch (Exception ex)
            {
                obsoleteReference = null;
                item.Attempt++;
                item.LastError = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                if (item.Attempt >= MaxScanAttempts)
                {
                    item.Status = FileScanWorkItemStatus.Dead;
                    item.CompletedAt = DateTime.UtcNow;
                    if (file is not null)
                    {
                        file.ScanStatus = FileScanStatus.ScanError;
                        file.ScannedAt = DateTime.UtcNow;
                        AppendScanResult(
                            file,
                            FileScanStatus.ScanError,
                            new MalwareScanOutcome(MalwareScanVerdict.Error, null, file.ScannerName ?? "unknown", file.ScannerVersion, ex.Message),
                            false,
                            null,
                            ex.Message);
                    }
                }
                else
                {
                    var delaySeconds = Math.Min(3600, Math.Pow(2, Math.Min(item.Attempt, 10)));
                    item.NextAttemptAt = DateTime.UtcNow.AddSeconds(delaySeconds);
                }
            }

            await db.SaveChangesAsync(cancellationToken);

            // The database is the source of truth for the object's current location. Delete the
            // obsolete quarantine copy only after that location has been committed successfully.
            if (obsoleteReference is not null)
            {
                try { await storage.DeleteAsync(obsoleteReference, cancellationToken); }
                catch { /* best-effort quarantine cleanup */ }
            }
        }

        return completed;
    }

    public async Task<int> EnqueueRescanAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var file = await db.Files.FirstOrDefaultAsync(x => x.Id == fileId && !x.IsDeleted, cancellationToken)
            ?? throw new KeyNotFoundException($"File {fileId} not found.");

        file.ScanStatus = FileScanStatus.PendingScan;
        file.ThreatName = null;
        db.FileScanWorkItems.Add(new FileScanWorkItem
        {
            FileId = file.Id,
            Attempt = 0,
            NextAttemptAt = DateTime.UtcNow,
            Status = FileScanWorkItemStatus.Pending,
            TenantId = file.TenantId
        });
        await db.SaveChangesAsync(cancellationToken);
        return 1;
    }

    public Task<List<FileRecord>> ListByScanStatusAsync(
        string scanStatus,
        CancellationToken cancellationToken = default) =>
        db.Files.AsNoTracking()
            .Where(x => !x.IsDeleted && x.ScanStatus == scanStatus)
            .OrderByDescending(x => x.UploadedAt)
            .Take(200)
            .ToListAsync(cancellationToken);

    private void AppendScanResult(
        FileRecord file,
        string outcome,
        MalwareScanOutcome scan,
        bool cdrApplied,
        string? cdrProfile,
        string? detail)
    {
        db.FileScanResults.Add(new FileScanResult
        {
            FileId = file.Id,
            Outcome = outcome,
            ThreatName = scan.ThreatName ?? file.ThreatName,
            ScannerName = scan.ScannerName,
            ScannerVersion = scan.ScannerVersion,
            CdrApplied = cdrApplied,
            CdrProfile = cdrProfile,
            ScannedAt = DateTime.UtcNow,
            Detail = detail,
            TenantId = file.TenantId
        });
    }

    public FileStorageMetricsSnapshot GetMetricsSnapshot()
    {
        var files = db.Files.AsNoTracking();
        return new FileStorageMetricsSnapshot
        {
            TotalFiles = files.Count(x => !x.IsDeleted),
            PendingOrphans = files.Count(x => !x.IsDeleted && x.ReferenceCount == 0 && x.Status == FileRecordStatus.Pending),
            UnreferencedActive = files.Count(x => !x.IsDeleted && x.ReferenceCount == 0 && x.Status == FileRecordStatus.Active),
            DeletedFiles = files.Count(x => x.IsDeleted),
            PendingPurge = files.Count(x => x.Status == FileRecordStatus.PendingPurge),
            DeadPurgeWorkItems = db.FilePurgeWorkItems.AsNoTracking().Count(x => x.Status == FilePurgeWorkItemStatus.Dead),
            PendingScan = files.Count(x => !x.IsDeleted && x.ScanStatus == FileScanStatus.PendingScan),
            InfectedCount = files.Count(x => !x.IsDeleted && x.ScanStatus == FileScanStatus.Infected),
            QuarantinedCount = files.Count(x => !x.IsDeleted && x.ScanStatus == FileScanStatus.Quarantined),
            ScanErrorCount = files.Count(x => !x.IsDeleted && x.ScanStatus == FileScanStatus.ScanError),
            DeadScanWorkItems = db.FileScanWorkItems.AsNoTracking().Count(x => x.Status == FileScanWorkItemStatus.Dead)
        };
    }

    public Task<List<FilePurgeWorkItem>> GetPurgeQueueAsync(
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        var q = db.FilePurgeWorkItems.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(x => x.Status == status);
        return q.OrderByDescending(x => x.CreatedAt).Take(200).ToListAsync(cancellationToken);
    }

    private async Task EnqueuePendingPurgeAsync(
        FileRecord record,
        Guid? deletedBy,
        string? forceReason,
        CancellationToken cancellationToken)
    {
        record.IsDeleted = true;
        record.DeletedAt = DateTime.UtcNow;
        record.Status = FileRecordStatus.PendingPurge;
        record.UpdatedBy = deletedBy;
        record.UpdatedAt = DateTime.UtcNow;
        record.ForceDeleteReason = forceReason;

        var alreadyQueued = db.FilePurgeWorkItems.Local
            .Any(x => x.FileId == record.Id && x.Status == FilePurgeWorkItemStatus.Pending)
            || await db.FilePurgeWorkItems
                .AnyAsync(x => x.FileId == record.Id && x.Status == FilePurgeWorkItemStatus.Pending, cancellationToken);

        if (!alreadyQueued)
        {
            db.FilePurgeWorkItems.Add(new FilePurgeWorkItem
            {
                FileId = record.Id,
                Bucket = record.Bucket,
                ObjectKey = record.ObjectKey,
                Attempt = 0,
                NextAttemptAt = DateTime.UtcNow,
                Status = FilePurgeWorkItemStatus.Pending,
                TenantId = record.TenantId
            });
        }
    }

    private static bool IsRetentionBlocked(FileRecord record) =>
        record.LegalHold || (record.RetainUntil is { } until && until > DateTime.UtcNow);

    private void MaybeAddException(
        FileOrphanScanResult result,
        HashSet<string> approved,
        bool persist,
        string kind,
        Guid? fileId,
        string? bucket,
        string? objectKey,
        string detail)
    {
        var key = kind + "|" + (fileId ?? Guid.Empty) + "|" + (bucket ?? "") + "|" + (objectKey ?? "");
        var isApproved = approved.Contains(key);
        var ex = new FileReconcileException
        {
            Kind = kind,
            FileId = fileId,
            Bucket = bucket,
            ObjectKey = objectKey,
            Detail = detail,
            Approved = isApproved,
            DetectedAt = DateTime.UtcNow,
            TenantId = db.EffectiveTenantId
        };
        result.Exceptions.Add(ex);
        if (persist && !isApproved)
            db.FileReconcileExceptions.Add(ex);
    }

    private static int GetOrphanTtlHours() =>
        int.TryParse(Environment.GetEnvironmentVariable("FileStorage__GarbageCollection__OrphanUploadTtlHours"), out var h) ? h : 24;

    private static int GetReleaseGraceHours() =>
        int.TryParse(Environment.GetEnvironmentVariable("FileStorage__GarbageCollection__ReleaseGraceHours"), out var h) ? h : 24;
}

public sealed class FileStorageMetricsSnapshot
{
    public int TotalFiles { get; init; }
    public int PendingOrphans { get; init; }
    public int UnreferencedActive { get; init; }
    public int DeletedFiles { get; init; }
    public int PendingPurge { get; init; }
    public int DeadPurgeWorkItems { get; init; }
    public int PendingScan { get; init; }
    public int InfectedCount { get; init; }
    public int QuarantinedCount { get; init; }
    public int ScanErrorCount { get; init; }
    public int DeadScanWorkItems { get; init; }
}

public sealed class FileOrphanScanResult
{
    public int DbWithoutObject { get; set; }
    public int ObjectWithoutDb { get; set; }
    public int DeletedWithObject { get; set; }
    public int RefCountDriftRepaired { get; set; }
    public int UnresolvedExceptions { get; set; }
    public List<FileReconcileException> Exceptions { get; } = [];
}
