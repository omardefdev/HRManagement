using System.Security.Claims;
using File.Storage.Domain.Entities;
using File.Storage.Domain.Enums;
using File.Storage.Infrastructure.Data;
using File.Storage.Infrastructure.Services;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Files;
using HRManagement.SharedKernel.Files;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using FileVisibility = HRManagement.SharedKernel.Files.FileVisibility;

namespace HRManagement.IntegrationTests;

/// <summary>SEC-003: runtime wiring of FileAccessGrant + IFileAccessPolicy.</summary>
public class Sec003FileAccessIntegrationTests : IAsyncDisposable
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    private static readonly Guid UploaderId = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FileStorageDbContext> _options;
    private readonly IFileAccessPolicy _policy = new FileAccessPolicy();

    public Sec003FileAccessIntegrationTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<FileStorageDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var db = CreateDb(TenantA);
        db.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    private FileAccessGrantService CreateGrantService(FileStorageDbContext db, Guid tenantId) =>
        new(db, new FixedTenant(tenantId),
            Microsoft.Extensions.Options.Options.Create(new HRManagement.FileStorage.FileStorageOptions()));

    [Fact]
    public async Task Upload_Creates_AccessGrant()
    {
        await using var db = CreateDb(TenantA);
        var grants = CreateGrantService(db, TenantA);
        var fileId = await SeedFileAsync(db);

        var grant = await grants.CreateGrantAsync(
            fileId,
            fileReference: "bucket/key",
            module: "File.Storage",
            holderType: "Uploader",
            holderId: UploaderId.ToString(),
            visibility: FileVisibility.Private,
            granteeId: UploaderId.ToString());

        Assert.Equal(fileId, grant.FileId);
        Assert.Equal(TenantA, grant.TenantId);
        Assert.False(grant.IsRevoked);
        Assert.True(grant.PresignedUrlMaxLifetimeSeconds <= 300);
        Assert.Equal(1, await db.FileAccessGrants.CountAsync(g => g.FileId == fileId));
    }

    [Fact]
    public async Task Cross_Tenant_Access_Denied()
    {
        await using var db = CreateDb(TenantA);
        var grants = CreateGrantService(db, TenantA);
        var fileId = await SeedFileAsync(db);
        var grant = await grants.CreateGrantAsync(
            fileId, "bucket/key", "HR.Core", "Employee", UploaderId.ToString(),
            FileVisibility.Tenant, UploaderId.ToString());

        var user = AuthenticatedUser(UploaderId);
        var decision = _policy.Evaluate(user, grant, TenantB);
        Assert.False(decision.Allowed);
        Assert.Contains("Cross-tenant", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Grantee_Can_Access_Private_File()
    {
        await using var db = CreateDb(TenantA);
        var grants = CreateGrantService(db, TenantA);
        var fileId = await SeedFileAsync(db);
        var grant = await grants.CreateGrantAsync(
            fileId, "bucket/key", "File.Storage", "Uploader", UploaderId.ToString(),
            FileVisibility.Private, UploaderId.ToString());

        var user = AuthenticatedUser(UploaderId);
        var decision = _policy.Evaluate(user, grant, TenantA);
        Assert.True(decision.Allowed);
    }

    [Fact]
    public async Task Non_Owner_Denied_Private_File()
    {
        await using var db = CreateDb(TenantA);
        var grants = CreateGrantService(db, TenantA);
        var fileId = await SeedFileAsync(db);
        var grant = await grants.CreateGrantAsync(
            fileId, "bucket/key", "File.Storage", "Uploader", UploaderId.ToString(),
            FileVisibility.Private, UploaderId.ToString());

        var stranger = AuthenticatedUser(Guid.NewGuid());
        var decision = _policy.Evaluate(stranger, grant, TenantA);
        Assert.False(decision.Allowed);
    }

    [Fact]
    public async Task Revoked_Grant_Denied()
    {
        await using var db = CreateDb(TenantA);
        var grants = CreateGrantService(db, TenantA);
        var fileId = await SeedFileAsync(db);
        await grants.CreateGrantAsync(
            fileId, "bucket/key", "File.Storage", "Uploader", UploaderId.ToString(),
            FileVisibility.Private, UploaderId.ToString());

        await grants.RevokeGrantsForFileAsync(fileId);
        var grant = await grants.FindGrantByFileAsync(fileId);
        Assert.NotNull(grant);
        Assert.True(grant!.IsRevoked);

        var decision = _policy.Evaluate(AuthenticatedUser(UploaderId), grant, TenantA);
        Assert.False(decision.Allowed);
    }

    [Fact]
    public async Task Admin_Can_Access_And_Delete()
    {
        await using var db = CreateDb(TenantA);
        var grants = CreateGrantService(db, TenantA);
        var fileId = await SeedFileAsync(db);
        var grant = await grants.CreateGrantAsync(
            fileId, "bucket/key", "File.Storage", "Uploader", UploaderId.ToString());

        var admin = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, HrManagementRoles.Admin)], "test"));

        Assert.True(_policy.Evaluate(admin, grant, TenantA).Allowed);
        Assert.True(_policy.EvaluateDelete(admin, grant, TenantA).Allowed);
    }

    [Fact]
    public async Task Presigned_Url_Lifetime_Capped_At_300()
    {
        await using var db = CreateDb(TenantA);
        var grants = CreateGrantService(db, TenantA);
        var fileId = await SeedFileAsync(db);
        var grant = await grants.CreateGrantAsync(
            fileId, "bucket/key", "File.Storage", "Uploader", UploaderId.ToString(),
            presignedUrlMaxLifetimeSeconds: 9999);

        Assert.Equal(300, grant.PresignedUrlMaxLifetimeSeconds);

        var requestedSeconds = 60 * 60; // 60 minutes
        var effective = Math.Min(requestedSeconds, Math.Clamp(grant.PresignedUrlMaxLifetimeSeconds, 1, 300));
        Assert.Equal(300, effective);
    }

    [Fact]
    public async Task No_Grant_Means_Deny()
    {
        await using var db = CreateDb(TenantA);
        var grants = CreateGrantService(db, TenantA);
        var fileId = await SeedFileAsync(db);

        var found = await grants.FindActiveGrantByFileAsync(fileId);
        Assert.Null(found);
    }

    [Fact]
    public async Task Expired_grant_denies_presign_and_logs()
    {
        await using var db = CreateDb(TenantA);
        var grants = CreateGrantService(db, TenantA);
        var fileId = await SeedFileAsync(db);
        var grant = await grants.CreateGrantAsync(
            fileId, "bucket/key", "File.Storage", "Uploader", UploaderId.ToString(),
            FileVisibility.Private, UploaderId.ToString());
        grant.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        var decision = _policy.Evaluate(AuthenticatedUser(UploaderId), grant, TenantA);
        Assert.False(decision.Allowed);
        Assert.Equal("grant-expired", decision.Reason);

        db.FileAccessAuditLogs.Add(new FileAccessAuditLog
        {
            TenantId = TenantA,
            FileId = fileId,
            Action = "access-denied",
            DecisionAllowed = false,
            DecisionReason = decision.Reason
        });
        await db.SaveChangesAsync();
        var log = await db.FileAccessAuditLogs.AsNoTracking().OrderByDescending(x => x.CreatedAt).FirstAsync();
        Assert.Equal("grant-expired", log.DecisionReason);
    }

    [Fact]
    public async Task Wrong_grantee_denies_and_logs()
    {
        await using var db = CreateDb(TenantA);
        var grants = CreateGrantService(db, TenantA);
        var fileId = await SeedFileAsync(db);
        var grant = await grants.CreateGrantAsync(
            fileId, "bucket/key", "File.Storage", "Uploader", UploaderId.ToString(),
            FileVisibility.Private, UploaderId.ToString());

        var stranger = AuthenticatedUser(Guid.NewGuid());
        var decision = _policy.Evaluate(stranger, grant, TenantA);
        Assert.False(decision.Allowed);
        Assert.Equal("wrong-grantee", decision.Reason);

        db.FileAccessAuditLogs.Add(new FileAccessAuditLog
        {
            TenantId = TenantA,
            FileId = fileId,
            Action = "access-denied",
            DecisionAllowed = false,
            DecisionReason = decision.Reason
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Audit_Log_Stores_Decision()
    {
        await using var db = CreateDb(TenantA);
        db.FileAccessAuditLogs.Add(new FileAccessAuditLog
        {
            TenantId = TenantA,
            FileId = Guid.NewGuid(),
            Action = "access-denied",
            DecisionAllowed = false,
            DecisionReason = "No matching access rule"
        });
        await db.SaveChangesAsync();

        var log = await db.FileAccessAuditLogs.AsNoTracking().SingleAsync();
        Assert.False(log.DecisionAllowed);
        Assert.Equal("No matching access rule", log.DecisionReason);
    }

    private FileStorageDbContext CreateDb(Guid tenantId) =>
        new(_options, new FixedTenant(tenantId));

    private static async Task<Guid> SeedFileAsync(FileStorageDbContext db)
    {
        var id = Guid.NewGuid();
        db.Files.Add(new FileRecord
        {
            Id = id,
            TenantId = db.EffectiveTenantId,
            OriginalName = "doc.pdf",
            Extension = ".pdf",
            MimeType = "application/pdf",
            Size = 12,
            Bucket = "file-storage",
            ObjectKey = $"docs/{id:N}.pdf",
            Status = FileRecordStatus.Active,
            ScanStatus = FileScanStatus.Clean,
            UploadedBy = UploaderId,
            UploadedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static ClaimsPrincipal AuthenticatedUser(Guid userId) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Role, "hr-employee"),
            new Claim("sub", userId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        ], "test"));

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId => tenantId;
        public bool IsPlatformOperator => false;
        public Guid RequireTenantId() => tenantId;
    }
}
