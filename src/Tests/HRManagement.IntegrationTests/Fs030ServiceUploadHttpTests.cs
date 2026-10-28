using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using File.Storage.Infrastructure.Data;
using File.Storage.Infrastructure.Services;
using HRManagement.FileStorage;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-30 (FS-030): HTTP contract of <c>api/files/service-upload</c> on the real File.Storage host (SQLite, local
/// storage, Development so caller proof is off). Service principals carry no tenant_id claim, as real service
/// tokens do not, so the tenant comes only from X-Tenant-ID.
/// </summary>
public class Fs030ServiceUploadHttpTests : IAsyncDisposable
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-4000-8000-0000000f0301");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-0000-4000-8000-0000000f0301");

    private readonly WebApplicationFactory<FileStorageWebProgram> _factory;
    private readonly string _storageRoot;

    public Fs030ServiceUploadHttpTests()
    {
        _storageRoot = Path.Combine(Path.GetTempPath(), "fs030-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_storageRoot);
        var dbPath = Path.Combine(_storageRoot, "file-storage.db");

        _factory = new WebApplicationFactory<FileStorageWebProgram>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Development);
            builder.UseSetting("ConnectionStrings:FileStorageDb", $"Data Source={dbPath}");
            builder.UseSetting("FileStorage:Provider", "Local");
            builder.UseSetting("FileStorage:LocalRootPath", _storageRoot);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:RunMigrationsAtStartup"] = "false",
                    ["ConnectionStrings:FileStorageDb"] = $"Data Source={dbPath}",
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
                services.AddAuthentication(options =>
                    {
                        options.DefaultScheme = ServiceTestAuthHandler.SchemeName;
                        options.DefaultAuthenticateScheme = ServiceTestAuthHandler.SchemeName;
                        options.DefaultChallengeScheme = ServiceTestAuthHandler.SchemeName;
                        options.DefaultForbidScheme = ServiceTestAuthHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, ServiceTestAuthHandler>(ServiceTestAuthHandler.SchemeName, _ => { });
            });
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        try
        {
            if (Directory.Exists(_storageRoot))
                Directory.Delete(_storageRoot, recursive: true);
        }
        catch (IOException)
        {
            // SQLite may still hold the file briefly on Windows; the temp folder is disposable.
        }
    }

    [Fact]
    public async Task Upload_returns_201_for_svc_hr_core_and_403_or_401_for_everyone_else()
    {
        using var client = await CreateClientAsync();

        using var ok = await client.SendAsync(Upload(MinimalPdf(), "cv.pdf", "application/pdf", TenantA, ServiceClientIds.HrCore, ServiceScopes.FileStorageServiceUpload));
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        using var json = JsonDocument.Parse(await ok.Content.ReadAsStringAsync());
        var fileId = json.RootElement.GetProperty("fileId").GetGuid();
        Assert.Equal("cv.pdf", json.RootElement.GetProperty("originalName").GetString());
        Assert.Equal((long)MinimalPdf().Length, json.RootElement.GetProperty("size").GetInt64());
        Assert.Equal("application/pdf", json.RootElement.GetProperty("mimeType").GetString());
        Assert.EndsWith($"/api/files/service-upload/{fileId}/content", ok.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);

        using var otherClient = await client.SendAsync(Upload(MinimalPdf(), "cv.pdf", "application/pdf", TenantA, ServiceClientIds.Payroll, ServiceScopes.FileStorageServiceUpload));
        using var missingScope = await client.SendAsync(Upload(MinimalPdf(), "cv.pdf", "application/pdf", TenantA, ServiceClientIds.HrCore, ServiceScopes.FileStorageReferences));
        using var user = await client.SendAsync(Upload(MinimalPdf(), "cv.pdf", "application/pdf", TenantA, client: null, scope: null, userRole: HrManagementRoles.Admin));
        using var anonymous = await client.SendAsync(Upload(MinimalPdf(), "cv.pdf", "application/pdf", TenantA, client: null, scope: null));

        Assert.Equal(HttpStatusCode.Forbidden, otherClient.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, missingScope.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, user.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task Validation_problems_return_400_with_the_Arabic_message()
    {
        using var client = await CreateClientAsync();

        using var noTenant = await client.SendAsync(Upload(MinimalPdf(), "cv.pdf", "application/pdf", tenant: null, ServiceClientIds.HrCore, ServiceScopes.FileStorageServiceUpload));
        using var wrongProfile = await client.SendAsync(Upload(MinimalPdf(), "cv.pdf", "application/pdf", TenantA, ServiceClientIds.HrCore, ServiceScopes.FileStorageServiceUpload, profile: "Document"));
        using var exe = await client.SendAsync(Upload([0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00], "setup.exe", "application/x-msdownload", TenantA, ServiceClientIds.HrCore, ServiceScopes.FileStorageServiceUpload));
        using var spoofed = await client.SendAsync(Upload([0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00], "cv.pdf", "application/pdf", TenantA, ServiceClientIds.HrCore, ServiceScopes.FileStorageServiceUpload));

        await AssertBadRequestAsync(noTenant, FileServiceUploadService.MissingTenantMessage);
        await AssertBadRequestAsync(wrongProfile, FileServiceUploadService.UnsupportedProfileMessage);
        await AssertBadRequestAsync(exe, "نوع الملف غير مدعوم.");
        await AssertBadRequestAsync(spoofed, "تعارض بين امتداد الملف ومحتواه (magic bytes).");
    }

    [Fact]
    public async Task Metadata_and_content_serve_only_the_callers_Clean_uploads_in_the_request_tenant()
    {
        using var client = await CreateClientAsync();
        using var uploaded = await client.SendAsync(Upload(MinimalPdf(), "cv.pdf", "application/pdf", TenantA, ServiceClientIds.HrCore, ServiceScopes.FileStorageServiceUpload));
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        using var json = JsonDocument.Parse(await uploaded.Content.ReadAsStringAsync());
        var fileId = json.RootElement.GetProperty("fileId").GetGuid();

        using var pending = await client.SendAsync(Get(fileId, TenantA, ServiceClientIds.HrCore));
        Assert.Equal(HttpStatusCode.NotFound, pending.StatusCode);
        Assert.Equal("PendingScan", await ScanStatusAsync(client, fileId, TenantA));

        await using (var scope = _factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<FilePlatformService>().ProcessScanQueueAsync();

        using var clean = await client.SendAsync(Get(fileId, TenantA, ServiceClientIds.HrCore));
        Assert.Equal(HttpStatusCode.OK, clean.StatusCode);
        Assert.Equal("application/pdf", clean.Content.Headers.ContentType!.MediaType);
        Assert.Equal(MinimalPdf(), await clean.Content.ReadAsByteArrayAsync());
        Assert.Equal("Clean", await ScanStatusAsync(client, fileId, TenantA));

        using var otherTenant = await client.SendAsync(Get(fileId, TenantB, ServiceClientIds.HrCore));
        using var noTenant = await client.SendAsync(Get(fileId, tenant: null, ServiceClientIds.HrCore));
        using var unknown = await client.SendAsync(Get(Guid.NewGuid(), TenantA, ServiceClientIds.HrCore));
        Assert.Equal(HttpStatusCode.NotFound, otherTenant.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noTenant.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Null(await ScanStatusAsync(client, fileId, TenantB));
    }

    private async Task<HttpClient> CreateClientAsync()
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<FileStorageDbContext>().Database.EnsureCreatedAsync();
            await scope.ServiceProvider.GetRequiredService<IFileStorageService>().EnsureBucketsAsync();
        }

        return _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    private static async Task<string?> ScanStatusAsync(HttpClient client, Guid fileId, Guid tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/files/service-upload/metadata")
        {
            Content = JsonContent.Create(new { fileIds = new[] { fileId } })
        };
        Authenticate(request, tenant, ServiceClientIds.HrCore, ServiceScopes.FileStorageServiceUpload);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.EnumerateArray()
            .Where(e => e.GetProperty("fileId").GetGuid() == fileId)
            .Select(e => e.GetProperty("scanStatus").GetString())
            .SingleOrDefault();
    }

    private static async Task AssertBadRequestAsync(HttpResponseMessage response, string arabicMessage)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(arabicMessage, await response.Content.ReadAsStringAsync());
    }

    private static HttpRequestMessage Upload(
        byte[] bytes,
        string fileName,
        string contentType,
        Guid? tenant,
        string? client,
        string? scope,
        string? userRole = null,
        string profile = FileServiceUploadService.CandidateDocumentProfile)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        var form = new MultipartFormDataContent
        {
            { file, "file", fileName },
            { new StringContent(profile), "constraintProfile" }
        };
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/files/service-upload") { Content = form };
        Authenticate(request, tenant, client, scope, userRole);
        return request;
    }

    private static HttpRequestMessage Get(Guid fileId, Guid? tenant, string client)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/files/service-upload/{fileId:D}/content");
        Authenticate(request, tenant, client, ServiceScopes.FileStorageServiceUpload);
        return request;
    }

    private static void Authenticate(HttpRequestMessage request, Guid? tenant, string? client, string? scope, string? userRole = null)
    {
        if (tenant is not null)
            request.Headers.Add(TenantIdMiddleware.HeaderName, tenant.Value.ToString("D"));
        if (client is not null)
            request.Headers.Add(ServiceTestAuthHandler.ClientHeader, client);
        if (scope is not null)
            request.Headers.Add(ServiceTestAuthHandler.ScopeHeader, scope);
        if (userRole is not null)
            request.Headers.Add(ServiceTestAuthHandler.RoleHeader, userRole);
    }

    private static byte[] MinimalPdf() =>
        Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n");

    /// <summary>Service principals shaped like Keycloak client_credentials tokens (no tenant_id claim).</summary>
    private sealed class ServiceTestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Fs030Service";
        public const string ClientHeader = "X-Fs030-Client";
        public const string ScopeHeader = "X-Fs030-Scope";
        public const string RoleHeader = "X-Fs030-Role";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var clientId = Request.Headers[ClientHeader].ToString();
            var role = Request.Headers[RoleHeader].ToString();
            List<Claim> claims;
            if (!string.IsNullOrWhiteSpace(clientId))
            {
                claims =
                [
                    new("preferred_username", $"service-account-{clientId}"),
                    new("azp", clientId),
                    new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D"))
                ];
                var scope = Request.Headers[ScopeHeader].ToString();
                if (!string.IsNullOrWhiteSpace(scope))
                    claims.Add(new Claim("scope", scope));
            }
            else if (!string.IsNullOrWhiteSpace(role))
            {
                claims =
                [
                    new("preferred_username", "alice"),
                    new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
                    new(ClaimTypes.Role, role)
                ];
            }
            else
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(claims, SchemeName, "preferred_username", ClaimTypes.Role);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
