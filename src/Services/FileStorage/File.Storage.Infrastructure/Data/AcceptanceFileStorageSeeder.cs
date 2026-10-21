using System.Text;
using File.Storage.Domain.Entities;
using File.Storage.Domain.Enums;
using HRManagement.FileStorage;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Seeding;
using HRManagement.SharedKernel.Files;
using Microsoft.EntityFrameworkCore;

namespace File.Storage.Infrastructure.Data;

public static class AcceptanceFileStorageSeeder
{
    private static readonly string[] Modules =
    [
        "HR.Core", "HR.Contracts", "HR.Health", "IDCard", "Records.Archive", "Admin.Logistics"
    ];

    public static async Task SeedAsync(
        FileStorageDbContext db,
        IFileStorageService storage,
        CancellationToken ct = default)
    {
        await storage.EnsureBucketsAsync(ct);
        await AcceptanceSeedReceiptStore.EnsureTableAsync(db, ct);

        foreach (var agency in AcceptanceSeedCatalog.Agencies)
        {
            if (!await AcceptanceSeedReceiptStore.ShouldApplyAsync(db, agency.Id, nameof(FileStorageDbContext), ct))
                continue;
            using var frame = AcceptanceSeedExecution.Push(agency);
            var expected = agency.Volume.Files;
            var existing = await db.Files
                .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.SeedInitialization)
                .CountAsync(f => f.TenantId == agency.Id && f.ObjectKey.StartsWith("acceptance-v2/"), ct);
            if (existing == expected)
            {
                await AcceptanceSeedReceiptStore.WriteAppliedAsync(db, agency, nameof(FileStorageDbContext), ct);
                continue;
            }
            if (existing > 0)
            {
                throw new InvalidOperationException(
                    $"File.Storage contains an incomplete Acceptance v2 pack for {agency.Code}. " +
                    "Run scripts/acceptance-reset.ps1 before reseeding.");
            }

            var pending = new List<(FileRecord File, FileReferenceRecord Reference, FileScanResult Scan, FileAccessGrant Grant)>();
            for (var i = 0; i < expected; i++)
            {
                var employee = agency.EmployeeAt(i * 7);
                var fileId = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, $"file:{i:D4}");
                var objectKey = $"acceptance-v2/{agency.Code.ToLowerInvariant()}/{fileId:D}.txt";
                var bytes = Encoding.UTF8.GetBytes(
                    $"Acceptance v2 synthetic clean file\nAgency={agency.Code}\nEmployee={employee.EmployeeNumber}\nIndex={i:D4}\n");
                await using var content = new MemoryStream(bytes, writable: false);
                var stored = await storage.PutAsync(
                    FileStorageBuckets.FileStorage,
                    objectKey,
                    fileId,
                    content,
                    "text/plain",
                    bytes.Length,
                    ct);

                var uploadedAt = AcceptanceSeedCatalog.ReferenceDate.AddDays(-(i % 180));
                var file = new FileRecord
                {
                    Id = fileId,
                    TenantId = agency.Id,
                    OriginalName = $"acceptance-{agency.Code.ToLowerInvariant()}-{i + 1:D3}.txt",
                    Extension = ".txt",
                    MimeType = "text/plain",
                    DetectedContentType = "text/plain",
                    Size = stored.SizeBytes,
                    Sha256 = stored.Checksum,
                    Bucket = stored.Bucket,
                    ObjectKey = stored.ObjectKey,
                    UploadedBy = employee.Id,
                    UploadedAt = uploadedAt,
                    Status = FileRecordStatus.Active,
                    Visibility = i % 4 == 0
                        ? File.Storage.Domain.Enums.FileVisibility.Internal
                        : File.Storage.Domain.Enums.FileVisibility.Private,
                    IsDeleted = false,
                    ReferenceCount = 1,
                    LegalHold = i % 29 == 0,
                    RetainUntil = AcceptanceSeedCatalog.ReferenceDate.AddYears(5),
                    ScanStatus = FileScanStatus.Clean,
                    ScannedAt = uploadedAt.AddMinutes(2),
                    ScannerName = "AcceptanceCleanScanner",
                    ScannerVersion = "2.0",
                    CreatedBy = employee.Id,
                    CreatedAt = uploadedAt,
                    UpdatedAt = uploadedAt.AddMinutes(2)
                };
                var holderKey = $"{Modules[i % Modules.Length]}:Employee:{employee.Id:D}:Attachment";
                var reference = new FileReferenceRecord
                {
                    Id = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, $"file-reference:{i:D4}"),
                    TenantId = agency.Id,
                    FileId = fileId,
                    HolderKey = holderKey,
                    CreatedAt = uploadedAt.AddMinutes(3),
                    UpdatedAt = uploadedAt.AddMinutes(3)
                };
                var scan = new FileScanResult
                {
                    Id = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, $"file-scan:{i:D4}"),
                    TenantId = agency.Id,
                    FileId = fileId,
                    Outcome = FileScanStatus.Clean,
                    ScannerName = "AcceptanceCleanScanner",
                    ScannerVersion = "2.0",
                    CdrApplied = false,
                    ScannedAt = uploadedAt.AddMinutes(2),
                    Detail = "Synthetic acceptance fixture; content hash verified.",
                    CreatedAt = uploadedAt.AddMinutes(2),
                    UpdatedAt = uploadedAt.AddMinutes(2)
                };
                var revoked = i % 13 == 0;
                var expired = !revoked && i % 11 == 0;
                var grant = new FileAccessGrant
                {
                    Id = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, $"file-grant:{i:D4}"),
                    TenantId = agency.Id,
                    FileId = fileId,
                    Module = Modules[i % Modules.Length],
                    HolderType = "Employee",
                    HolderId = employee.Id.ToString("D"),
                    FileReference = stored.StorageKey,
                    Visibility = i % 4 == 0
                        ? HRManagement.SharedKernel.Files.FileVisibility.Module
                        : HRManagement.SharedKernel.Files.FileVisibility.Private,
                    GranteeId = employee.Id.ToString("D"),
                    PresignedUrlMaxLifetimeSeconds = 300,
                    IsRevoked = revoked,
                    CreatedAtUtc = uploadedAt.AddMinutes(3),
                    ExpiresAtUtc = expired
                        ? AcceptanceSeedCatalog.ReferenceDate.AddDays(-1)
                        : AcceptanceSeedCatalog.ReferenceDate.AddDays(90),
                    RevokedAtUtc = revoked ? AcceptanceSeedCatalog.ReferenceDate.AddDays(-2) : null
                };
                pending.Add((file, reference, scan, grant));
            }

            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(ct)
                : null;
            try
            {
                db.Files.AddRange(pending.Select(x => x.File));
                db.FileReferences.AddRange(pending.Select(x => x.Reference));
                db.FileScanResults.AddRange(pending.Select(x => x.Scan));
                db.FileAccessGrants.AddRange(pending.Select(x => x.Grant));
                await db.SaveChangesAsync(ct);
                await AcceptanceSeedReceiptStore.WriteAppliedAsync(db, agency, nameof(FileStorageDbContext), ct);
                if (transaction is not null)
                    await transaction.CommitAsync(ct);
                db.ChangeTracker.Clear();
            }
            catch
            {
                if (transaction is not null)
                    await transaction.RollbackAsync(ct);
                throw;
            }
        }
    }
}
