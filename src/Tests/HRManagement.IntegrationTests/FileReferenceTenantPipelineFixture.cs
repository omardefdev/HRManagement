using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using File.Storage.Infrastructure.Data;
using HR.Core.Infrastructure.Data;
using HRManagement.FileStorage;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;

namespace HRManagement.IntegrationTests;

/// <summary>
/// One PostgreSQL (Testcontainers) holding a migrated HR.Core database (the holder and its file-reference outbox) and a
/// migrated File.Storage database behind the real File.Storage host (local storage, Development, hosted services off).
/// Service principals carry no tenant_id claim, as real client_credentials tokens do not, so File.Storage takes the
/// tenant only from X-Tenant-ID. Each test uses fresh tenant ids.
/// </summary>
public sealed class FileReferenceTenantPipelineFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private string _storageRoot = "";
    private string _hrCoreConnection = "";
    private string _fileStorageConnection = "";

    public bool Available { get; private set; }
    public string? UnavailableReason { get; private set; }
    public WebApplicationFactory<FileStorageWebProgram> FileStorage { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
            await _postgres.StartAsync();
            _hrCoreConnection = WithDatabase(_postgres.GetConnectionString(), "hr_core_file_refs");
            _fileStorageConnection = WithDatabase(_postgres.GetConnectionString(), "file_storage_file_refs");

            await using (var hr = CreateHrCoreContext(null))
                await hr.Database.MigrateAsync();
            await using (var files = CreateFileStorageContext(null))
                await files.Database.MigrateAsync();

            _storageRoot = Path.Combine(Path.GetTempPath(), "file-ref-tenants-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_storageRoot);
            FileStorage = CreateFileStorageHost();
            await using (var scope = FileStorage.Services.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<IFileStorageService>().EnsureBucketsAsync();
            Available = true;
        }
        catch (Exception ex)
        {
            UnavailableReason = $"Docker/PostgreSQL unavailable: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (Available)
            await FileStorage.DisposeAsync();
        if (_postgres is not null)
            await _postgres.DisposeAsync();
        try
        {
            if (Directory.Exists(_storageRoot))
                Directory.Delete(_storageRoot, recursive: true);
        }
        catch (IOException)
        {
            // Disposable temp folder.
        }
    }

    /// <summary>HR.Core context; <c>null</c> is the dispatcher's background scope (no tenant, EffectiveTenantId = Guid.Empty).</summary>
    public HRCoreDbContext CreateHrCoreContext(Guid? tenantId) =>
        new(new DbContextOptionsBuilder<HRCoreDbContext>()
                .UseNpgsql(_hrCoreConnection, npgsql =>
                    npgsql.MigrationsAssembly(typeof(HRCoreDbContext).Assembly.GetName().Name))
                .AddInterceptors(new TenantIdStampInterceptor(new Tenant(tenantId)))
                .Options,
            new Tenant(tenantId));

    /// <summary>File.Storage context for assertions; <c>null</c> = no tenant.</summary>
    public FileStorageDbContext CreateFileStorageContext(Guid? tenantId) =>
        new(new DbContextOptionsBuilder<FileStorageDbContext>()
                .UseNpgsql(_fileStorageConnection, npgsql =>
                    npgsql.MigrationsAssembly(typeof(FileStorageDbContext).Assembly.GetName().Name))
                .Options,
            new Tenant(tenantId));

    /// <summary>HR.Core's production delivery client (service auth replaced by test headers, ForwardTenantHandler kept).</summary>
    public ServiceProvider CreateHrCoreDeliveryServices()
    {
        var services = new ServiceCollection();
        services.AddHttpContextAccessor();
        services.AddTransient<ForwardTenantHandler>();
        services.AddTransient(_ => new ServicePrincipalHeaders(ServiceClientIds.HrCore, ServiceScopes.FileStorageReferences));
        services.AddHttpClient("FileStorage", c => c.BaseAddress = FileStorage.Server.BaseAddress)
            .ConfigurePrimaryHttpMessageHandler(() => FileStorage.Server.CreateHandler())
            .AddHttpMessageHandler<ServicePrincipalHeaders>()
            .AddHttpMessageHandler<ForwardTenantHandler>();
        services.AddScoped<HttpFileReferenceClient>();
        return services.BuildServiceProvider();
    }

    /// <summary>Uploads a small PDF through <c>api/files/service-upload</c> as svc-hr-core in <paramref name="tenantId"/> (status Pending).</summary>
    public async Task<Guid> ServiceUploadAsync(Guid tenantId, string fileName = "cv.pdf")
    {
        using var client = FileStorage.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var file = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/files/service-upload")
        {
            Content = new MultipartFormDataContent
            {
                { file, "file", fileName },
                { new StringContent("CandidateDocument"), "constraintProfile" }
            }
        };
        request.Headers.Add(TenantIdMiddleware.HeaderName, tenantId.ToString("D"));
        request.Headers.Add(TestServiceAuthHandler.ClientHeader, ServiceClientIds.HrCore);
        request.Headers.Add(TestServiceAuthHandler.ScopeHeader, ServiceScopes.FileStorageServiceUpload);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("fileId").GetGuid();
    }

    private WebApplicationFactory<FileStorageWebProgram> CreateFileStorageHost() =>
        new WebApplicationFactory<FileStorageWebProgram>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Development);
            builder.UseSetting("ConnectionStrings:FileStorageDb", _fileStorageConnection);
            builder.UseSetting("FileStorage:Provider", "Local");
            builder.UseSetting("FileStorage:LocalRootPath", _storageRoot);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:RunMigrationsAtStartup"] = "false",
                    ["ConnectionStrings:FileStorageDb"] = _fileStorageConnection,
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
                        options.DefaultScheme = TestServiceAuthHandler.SchemeName;
                        options.DefaultAuthenticateScheme = TestServiceAuthHandler.SchemeName;
                        options.DefaultChallengeScheme = TestServiceAuthHandler.SchemeName;
                        options.DefaultForbidScheme = TestServiceAuthHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestServiceAuthHandler>(TestServiceAuthHandler.SchemeName, _ => { });
            });
        });

    private static string WithDatabase(string connectionString, string database) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Database = database }.ConnectionString;

    private sealed class Tenant(Guid? tenantId) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public bool IsPlatformOperator => false;
        public Guid RequireTenantId() => tenantId ?? throw new InvalidOperationException("No tenant.");
    }

    /// <summary>Stands in for ServiceAuthHandler: marks the outgoing call as the given service client and scope.</summary>
    private sealed class ServicePrincipalHeaders(string clientId, string scope) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.TryAddWithoutValidation(TestServiceAuthHandler.ClientHeader, clientId);
            request.Headers.TryAddWithoutValidation(TestServiceAuthHandler.ScopeHeader, scope);
            return base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>Service principals shaped like Keycloak client_credentials tokens (no tenant_id claim).</summary>
    private sealed class TestServiceAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "FileRefTenantService";
        public const string ClientHeader = "X-FileRef-Client";
        public const string ScopeHeader = "X-FileRef-Scope";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var clientId = Request.Headers[ClientHeader].ToString();
            if (string.IsNullOrWhiteSpace(clientId))
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim>
            {
                new("preferred_username", $"service-account-{clientId}"),
                new("azp", clientId),
                new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D"))
            };
            var scope = Request.Headers[ScopeHeader].ToString();
            if (!string.IsNullOrWhiteSpace(scope))
                claims.Add(new Claim("scope", scope));

            var identity = new ClaimsIdentity(claims, SchemeName, "preferred_username", ClaimTypes.Role);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
