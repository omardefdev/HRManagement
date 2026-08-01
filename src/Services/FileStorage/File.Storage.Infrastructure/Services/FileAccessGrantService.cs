using File.Storage.Infrastructure.Data;
using HRManagement.SharedKernel.Files;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace File.Storage.Infrastructure.Services;

/// <summary>SEC-003: persist and resolve object-level file access grants.</summary>
public sealed class FileAccessGrantService(
    FileStorageDbContext db,
    ITenantContext tenantContext)
{
    public async Task<FileAccessGrant> CreateGrantAsync(
        Guid fileId,
        string fileReference,
        string module,
        string holderType,
        string holderId,
        FileVisibility visibility = FileVisibility.Private,
        string? granteeId = null,
        int? presignedUrlMaxLifetimeSeconds = null,
        CancellationToken cancellationToken = default)
    {
        var existing = await db.FileAccessGrants
            .FirstOrDefaultAsync(g => g.FileId == fileId && !g.IsRevoked, cancellationToken);
        if (existing is not null)
            return existing;

        var grant = new FileAccessGrant
        {
            TenantId = tenantContext.RequireTenantId(),
            FileId = fileId,
            FileReference = fileReference,
            Module = module,
            HolderType = holderType,
            HolderId = holderId,
            Visibility = visibility,
            GranteeId = granteeId,
            PresignedUrlMaxLifetimeSeconds = Math.Clamp(presignedUrlMaxLifetimeSeconds ?? 300, 1, 300)
        };

        db.FileAccessGrants.Add(grant);
        await db.SaveChangesAsync(cancellationToken);
        return grant;
    }

    public async Task<FileAccessGrant?> FindActiveGrantByFileAsync(
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        return await db.FileAccessGrants
            .AsNoTracking()
            .Where(g => g.FileId == fileId && !g.IsRevoked)
            .OrderByDescending(g => g.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<FileAccessGrant?> FindGrantByFileAsync(
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        return await db.FileAccessGrants
            .AsNoTracking()
            .Where(g => g.FileId == fileId)
            .OrderByDescending(g => g.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task RevokeGrantsForFileAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var grants = await db.FileAccessGrants
            .Where(g => g.FileId == fileId && !g.IsRevoked)
            .ToListAsync(cancellationToken);

        if (grants.Count == 0)
            return;

        var now = DateTime.UtcNow;
        foreach (var grant in grants)
        {
            grant.IsRevoked = true;
            grant.RevokedAtUtc = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Backfill a Module-visibility grant for an existing file that has none.
    /// Used so legacy rows are not left unauthorizable after SEC-003 wiring.
    /// </summary>
    public async Task<FileAccessGrant> EnsureDefaultGrantAsync(
        Guid fileId,
        string fileReference,
        Guid? uploadedBy,
        CancellationToken cancellationToken = default)
    {
        var existing = await FindGrantByFileAsync(fileId, cancellationToken);
        if (existing is not null && !existing.IsRevoked)
            return existing;

        return await CreateGrantAsync(
            fileId,
            fileReference,
            module: "File.Storage",
            holderType: "Uploader",
            holderId: uploadedBy?.ToString() ?? "system",
            visibility: FileVisibility.Module,
            granteeId: uploadedBy?.ToString(),
            cancellationToken: cancellationToken);
    }
}
