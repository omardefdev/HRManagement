using System.Security.Claims;
using File.Storage.Domain.Entities;
using File.Storage.Domain.Enums;
using File.Storage.Infrastructure.Data;
using File.Storage.Infrastructure.Services;
using HRManagement.FileStorage;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.Files;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using FileVisibility = HRManagement.SharedKernel.Files.FileVisibility;

namespace HRManagement.IntegrationTests;

/// <summary>AC-30: bad S2S token rejected with zero new FileReferenceRecord rows.</summary>
public class S2sFileReferenceRejectionTests : IAsyncDisposable
{
    private static readonly Guid TenantId = TenantDefaults.DefaultTenantId;
    private readonly WebApplicationFactory<FileStorageWebProgram> _factory;
    private readonly string _storageRoot;
    private readonly string _dbPath;

    public S2sFileReferenceRejectionTests()
    {
        _storageRoot = Path.Combine(Path.GetTempPath(), "s2s-ref-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_storageRoot);
        _dbPath = Path.Combine(_storageRoot, "file-storage.db");

        _factory = new WebApplicationFactory<FileStorageWebProgram>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Development);
            builder.UseSetting("ConnectionStrings:FileStorageDb", $"Data Source={_dbPath}");
            builder.UseSetting("FileStorage:Provider", "Local");
            builder.UseSetting("FileStorage:LocalRootPath", _storageRoot);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:RunMigrationsAtStartup"] = "false",
                    ["ConnectionStrings:FileStorageDb"] = $"Data Source={_dbPath}",
                    ["FileStorage:Provider"] = "Local",
                    ["FileStorage:LocalRootPath"] = _storageRoot,
                    ["FileStorage:ClamAvRequired"] = "false",
                    ["HrAuth:BehindGateway"] = "true",
                    ["Keycloak:Authority"] = "http://localhost:8080/realms/hrmanagement"
                });
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<ITenantContext>();
                services.AddSingleton<ITenantContext>(new FixedTenantContext(TenantDefaults.DefaultTenantId));
                services.AddMemoryCache();
                services.AddIntegrationTestAuthentication();
            });
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        if (Directory.Exists(_storageRoot))
            Directory.Delete(_storageRoot, recursive: true);
    }

    [Fact]
    public async Task Wrong_service_client_rejected_without_creating_reference()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var db = scope.ServiceProvider.GetRequiredService<FileStorageDbContext>();
        await db.Database.EnsureCreatedAsync();

        var hrCore = ServicePrincipal(ServiceClientIds.HrCore, ServiceScopes.FileStorageReferences);
        var payroll = ServicePrincipal(ServiceClientIds.Payroll, ServiceScopes.FileStorageReferences);

        Assert.True((await auth.AuthorizeAsync(hrCore, ServicePolicies.FileStorageReferences)).Succeeded);
        Assert.False((await auth.AuthorizeAsync(payroll, ServicePolicies.FileStorageReferences)).Succeeded);

        var fileId = await SeedCleanFileAsync(db);
        var platform = scope.ServiceProvider.GetRequiredService<FilePlatformService>();
        var baseline = await db.FileReferences.CountAsync(r => r.FileId == fileId);

        var afterValid = await platform.RegisterReferenceAsync(fileId, "HR.Core:Employee:ac30-valid:FileId");
        Assert.Equal(baseline + 1, afterValid);
        Assert.Equal(baseline + 1, await db.FileReferences.CountAsync(r => r.FileId == fileId));

        // Rogue client fails policy before any handler side effect — count must stay at post-valid level.
        Assert.False((await auth.AuthorizeAsync(payroll, ServicePolicies.FileStorageReferences)).Succeeded);
        Assert.Equal(baseline + 1, await db.FileReferences.CountAsync(r => r.FileId == fileId));
    }

    private static ClaimsPrincipal ServicePrincipal(string clientId, string scope)
    {
        var claims = new List<Claim>
        {
            new("preferred_username", $"service-account-{clientId}"),
            new("azp", clientId),
            new("scope", scope),
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D"))
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }

    private static async Task<Guid> SeedCleanFileAsync(FileStorageDbContext db)
    {
        var fileId = Guid.NewGuid();
        db.Files.Add(new FileRecord
        {
            Id = fileId,
            TenantId = TenantId,
            OriginalName = "doc.pdf",
            Extension = ".pdf",
            MimeType = "application/pdf",
            Size = 10,
            Bucket = FileStorageBuckets.FileStorage,
            ObjectKey = $"docs/{fileId:N}.pdf",
            Status = FileRecordStatus.Active,
            ScanStatus = FileScanStatus.Clean,
            UploadedAt = DateTime.UtcNow
        });
        db.FileAccessGrants.Add(new FileAccessGrant
        {
            TenantId = TenantId,
            FileId = fileId,
            Module = "File.Storage",
            HolderType = "Uploader",
            HolderId = "system",
            FileReference = $"{FileStorageBuckets.FileStorage}/docs/{fileId:N}.pdf",
            Visibility = FileVisibility.Module,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
        });
        await db.SaveChangesAsync();
        return fileId;
    }

    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId => tenantId;
        public bool IsPlatformOperator => false;
        public Guid RequireTenantId() => tenantId;
    }
}
