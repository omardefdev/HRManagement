using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using File.Storage.Domain.Entities;
using File.Storage.Domain.Enums;
using File.Storage.Infrastructure.Data;
using HRManagement.FileStorage;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace HRManagement.IntegrationTests;

/// <summary>AC-28: clean vs EICAR scan path via File.Storage API (local provider).</summary>
public class FileScanLiveE2ETests : IAsyncDisposable
{
    private static readonly Guid TenantId = AcceptanceSeedIds.AgencyAId;
    private readonly WebApplicationFactory<FileStorageWebProgram> _factory;
    private readonly string _storageRoot;
    private readonly string _dbPath;

    public FileScanLiveE2ETests()
    {
        _storageRoot = Path.Combine(Path.GetTempPath(), "file-scan-e2e-" + Guid.NewGuid().ToString("N"));
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
    public async Task Clean_pdf_scans_clean_and_downloads()
    {
        await SeedDbAsync();
        using var client = CreateClient();
        var fileId = await UploadAsync(client, MinimalPdf(), "clean.pdf", "application/pdf");

        await PollScanStatusAsync(client, fileId, FileScanStatus.Clean);
        var download = await client.GetAsync($"/api/files/{fileId:D}/download");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
    }

    [Fact]
    public async Task Eicar_embedded_pdf_quarantined_and_download_denied()
    {
        await SeedDbAsync();
        using var client = CreateClient();
        var payload = MinimalPdf().Concat(Encoding.ASCII.GetBytes(EicarAwareScanner.EicarSignature)).ToArray();
        var fileId = await UploadAsync(client, payload, "eicar.pdf", "application/pdf");

        await PollScanStatusAsync(client, fileId, FileScanStatus.Infected, FileScanStatus.Quarantined);
        var download = await client.GetAsync($"/api/files/{fileId:D}/download");
        Assert.True(download.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.Conflict);
    }

    [SkippableFact]
    public async Task Composite_scanner_uses_clamav_when_configured()
    {
        Skip.IfNot(RealInfra.IsEnabled, "Set RUN_TESTCONTAINERS=true for ClamAV container path.");
        var clamHost = Environment.GetEnvironmentVariable("CLAMAV_HOST");
        Skip.If(string.IsNullOrWhiteSpace(clamHost), "CLAMAV_HOST not set.");

        var scanner = new CompositeMalwareScanner(
            new EicarAwareScanner(),
            new ClamAvMalwareScanner(Microsoft.Extensions.Options.Options.Create(new FileStorageOptions
            {
                ClamAvHost = clamHost,
                ClamAvPort = int.TryParse(Environment.GetEnvironmentVariable("CLAMAV_PORT"), out var p) ? p : 3310
            })),
            Microsoft.Extensions.Options.Options.Create(new FileStorageOptions { ClamAvHost = clamHost }),
            new TestHostEnvironment());

        await using var ms = new MemoryStream(Encoding.ASCII.GetBytes(EicarAwareScanner.EicarSignature));
        var outcome = await scanner.ScanAsync(ms, "eicar.com");
        Assert.Equal(MalwareScanVerdict.Infected, outcome.Verdict);
    }

    private HttpClient CreateClient()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, TenantId.ToString("D"));
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, HrManagementRoles.Admin);
        return client;
    }

    private async Task SeedDbAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FileStorageDbContext>();
        await db.Database.EnsureCreatedAsync();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
        await storage.EnsureBucketsAsync();
    }

    private static async Task<Guid> UploadAsync(HttpClient client, byte[] bytes, string name, string contentType)
    {
        using var form = new MultipartFormDataContent();
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(content, "file", name);
        var resp = await client.PostAsync("/api/files", form);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("fileId").GetGuid();
    }

    private static async Task PollScanStatusAsync(
        HttpClient client,
        Guid fileId,
        params string[] acceptable)
    {
        for (var i = 0; i < 30; i++)
        {
            await client.PostAsync("/api/files/admin/process-scan", null);
            var resp = await client.GetAsync($"/api/files/{fileId:D}");
            if (resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                var status = doc.RootElement.GetProperty("scanStatus").GetString();
                if (status is not null && acceptable.Contains(status, StringComparer.Ordinal))
                    return;
            }
            await Task.Delay(200);
        }
        Assert.Fail($"Scan status did not reach [{string.Join(',', acceptable)}] for {fileId:D}");
    }

    private static byte[] MinimalPdf() => "%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n"u8.ToArray();

    private sealed class TestHostEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
