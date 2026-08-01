using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using File.Storage.Infrastructure.Data;
using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HRManagement.FileStorage;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Enums;
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

public class FileUploadLifecycleIntegrationTests
    : IClassFixture<WebApplicationFactory<FileStorageWebProgram>>,
      IClassFixture<WebApplicationFactory<HRCoreWebProgram>>
{
    private readonly WebApplicationFactory<FileStorageWebProgram> _fileStorageFactory;
    private readonly WebApplicationFactory<HRCoreWebProgram> _hrCoreFactory;
    private readonly string _storageRoot;
    private readonly string _fileStorageDbPath;
    private readonly string _hrCoreDbPath;

    public FileUploadLifecycleIntegrationTests(
        WebApplicationFactory<FileStorageWebProgram> fileStorageFactory,
        WebApplicationFactory<HRCoreWebProgram> hrCoreFactory)
    {
        _storageRoot = Path.Combine(Path.GetTempPath(), "hr-file-upload-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_storageRoot);
        _fileStorageDbPath = Path.Combine(_storageRoot, "file-storage.db");
        _hrCoreDbPath = Path.Combine(_storageRoot, "hr-core.db");

        _fileStorageFactory = fileStorageFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Development);
            builder.UseSetting("ConnectionStrings:FileStorageDb", $"Data Source={_fileStorageDbPath}");
            builder.UseSetting("FileStorage:Provider", "Local");
            builder.UseSetting("FileStorage:LocalRootPath", _storageRoot);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:RunMigrationsAtStartup"] = "false",
                    ["ConnectionStrings:FileStorageDb"] = $"Data Source={_fileStorageDbPath}",
                    ["FileStorage:Provider"] = "Local",
                    ["FileStorage:LocalRootPath"] = _storageRoot,
                    ["HrAuth:BehindGateway"] = "true",
                    ["Keycloak:Authority"] = "http://localhost:8080/realms/hrmanagement"
                });
            });

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddIntegrationTestAuthentication();
            });
        });

        _hrCoreFactory = hrCoreFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Development);
            builder.UseSetting("ConnectionStrings:HRCoreDb", $"Data Source={_hrCoreDbPath}");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:RunMigrationsAtStartup"] = "false",
                    ["ConnectionStrings:HRCoreDb"] = $"Data Source={_hrCoreDbPath}",
                    ["HrAuth:BehindGateway"] = "true",
                    ["Keycloak:Authority"] = "http://localhost:8080/realms/hrmanagement",
                    ["HrGateway:InternalBaseUrl"] = "http://localhost:5200"
                });
            });

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddIntegrationTestAuthentication();
            });
        });
    }

    [Fact]
    public async Task Upload_persists_to_storage_and_hr_core_can_save_and_download_file_id()
    {
        await using (var fileScope = _fileStorageFactory.Services.CreateAsyncScope())
        {
            var fileDb = fileScope.ServiceProvider.GetRequiredService<FileStorageDbContext>();
            await fileDb.Database.EnsureCreatedAsync();
            var storage = fileScope.ServiceProvider.GetRequiredService<IFileStorageService>();
            await storage.EnsureBucketsAsync();
        }

        await using (var coreScope = _hrCoreFactory.Services.CreateAsyncScope())
        {
            var coreDb = coreScope.ServiceProvider.GetRequiredService<HRCoreDbContext>();
            await coreDb.Database.EnsureCreatedAsync();
        }

        var uploadClient = _fileStorageFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var fileContent = "%PDF-1.4 integration-test-certificate"u8.ToArray();
        using var form = new MultipartFormDataContent();
        using var streamContent = new ByteArrayContent(fileContent);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(streamContent, "file", "certificate.pdf");

        var uploadResponse = await uploadClient.PostAsync("/api/files", form);
        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);

        var uploadJson = await uploadResponse.Content.ReadAsStringAsync();
        using var uploadDoc = JsonDocument.Parse(uploadJson);
        var fileId = uploadDoc.RootElement.GetProperty("fileId").GetGuid();
        Assert.NotEqual(Guid.Empty, fileId);

        await using (var fileScope = _fileStorageFactory.Services.CreateAsyncScope())
        {
            TenantIdFlow.Set(TenantDefaults.DefaultTenantId);
            try
            {
                var fileDb = fileScope.ServiceProvider.GetRequiredService<FileStorageDbContext>();
                var record = await fileDb.Files.AsNoTracking().SingleAsync(x => x.Id == fileId);
                Assert.Equal("certificate.pdf", record.OriginalName);
                Assert.True(record.Size > 0);
                Assert.False(string.IsNullOrWhiteSpace(record.ObjectKey));

                var storage = fileScope.ServiceProvider.GetRequiredService<IFileStorageService>();
                var reference = new FileReference(
                    record.Id,
                    record.Bucket,
                    record.ObjectKey,
                    record.MimeType,
                    record.Size,
                    record.Sha256);
                Assert.True(await storage.ExistsAsync(reference));
            }
            finally
            {
                TenantIdFlow.Set(null);
            }
        }

        var employeeId = Guid.NewGuid();
        await using (var coreScope = _hrCoreFactory.Services.CreateAsyncScope())
        {
            TenantIdFlow.Set(TenantDefaults.DefaultTenantId);
            try
            {
                var coreDb = coreScope.ServiceProvider.GetRequiredService<HRCoreDbContext>();
                coreDb.Employees.Add(new Employee
                {
                    Id = employeeId,
                    NationalId = "9000000001",
                    FirstName = "Test",
                    LastName = "Employee",
                    FullName = "Test Employee",
                    BirthDate = new DateTime(1990, 1, 1),
                    Gender = Gender.Male,
                    CurrentStatus = EmploymentStatus.Active
                });
                coreDb.EmployeeDocuments.Add(new EmployeeDocument
                {
                    Id = Guid.NewGuid(),
                    EmployeeId = employeeId,
                    DocType = DocumentType.Certificate,
                    DocTitle = "Upload lifecycle",
                    FileId = fileId
                });
                await coreDb.SaveChangesAsync();

                var saved = await coreDb.EmployeeDocuments.AsNoTracking()
                    .SingleAsync(x => x.EmployeeId == employeeId);
                Assert.Equal(fileId, saved.FileId);
            }
            finally
            {
                TenantIdFlow.Set(null);
            }
        }

        await using (var scanScope = _fileStorageFactory.Services.CreateAsyncScope())
        {
            TenantIdFlow.Set(TenantDefaults.DefaultTenantId);
            try
            {
                var scanDb = scanScope.ServiceProvider.GetRequiredService<FileStorageDbContext>();
                var fileRecord = await scanDb.Files.FirstAsync(x => x.Id == fileId);
                fileRecord.ScanStatus = File.Storage.Domain.Enums.FileScanStatus.Clean;
                await scanDb.SaveChangesAsync();
            }
            finally
            {
                TenantIdFlow.Set(null);
            }
        }

        var downloadResponse = await uploadClient.GetAsync($"/api/files/{fileId:D}/download");
        Assert.Equal(HttpStatusCode.OK, downloadResponse.StatusCode);
        var downloaded = await downloadResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(fileContent, downloaded);
    }
}
