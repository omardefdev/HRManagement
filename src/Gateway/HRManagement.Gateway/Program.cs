using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Localization;
using HRManagement.ServiceDefaults.NationalScope;
using HRManagement.ServiceDefaults.LegalControl;
using HRManagement.ServiceDefaults.ModuleCatalog;
using HRManagement.ServiceDefaults.NoFork;
using HRManagement.ServiceDefaults.ProgramGovernance;
using HRManagement.ServiceDefaults.Ownership;
using HRManagement.ServiceDefaults.ValueKpi;
using HRManagement.ServiceDefaults.MigInventory;
using HRManagement.ServiceDefaults.MigCanonical;
using HRManagement.ServiceDefaults.MigFactory;
using HRManagement.ServiceDefaults.MigMatch;
using HRManagement.ServiceDefaults.MigEvidence;
using HRManagement.ServiceDefaults.MigCutover;
using HRManagement.ServiceDefaults.MigOnboarding;
using HRManagement.ServiceDefaults.ExtFederation;
using HRManagement.ServiceDefaults.ExtCivil;
using HRManagement.ServiceDefaults.ExtTreasury;
using HRManagement.ServiceDefaults.ExtPki;
using HRManagement.ServiceDefaults.ExtApi;
using HRManagement.ServiceDefaults.ExtSandbox;
using HRManagement.ServiceDefaults.ExtSector;
using HRManagement.ServiceDefaults.ProdPolicy;
using HRManagement.ServiceDefaults.ProdAgency;
using HRManagement.ServiceDefaults.ProdEss;
using HRManagement.ServiceDefaults.ProdBranch;
using HRManagement.ServiceDefaults.ProdWorkbox;
using HRManagement.ServiceDefaults.ProdSemantic;
using HRManagement.ServiceDefaults.ProdExtensibility;
using HRManagement.ServiceDefaults.SovBaseline;
using HRManagement.ServiceDefaults.SovData;
using HRManagement.ServiceDefaults.SovKeys;
using HRManagement.ServiceDefaults.SovZt;
using HRManagement.ServiceDefaults.SovThreat;
using HRManagement.ServiceDefaults.SovPentest;
using HRManagement.ServiceDefaults.SovVendor;
using HRManagement.ServiceDefaults.ArcCriticality;
using HRManagement.ServiceDefaults.ArcReference;
using HRManagement.ServiceDefaults.Connectors;
using HRManagement.Web.Shared;
using HRManagement.Gateway.Services;
using HRManagement.Gateway.Middleware;
using HRManagement.Gateway.Data;
using HRManagement.ServiceDefaults.Evidence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
if (string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase))
{
    builder.Configuration.AddJsonFile("appsettings.Docker.json", optional: false, reloadOnChange: false);
}
builder.Host.UseHrManagementSerilog();
builder.AddHrProductionNetworking();
// Must precede authentication: cookie and OIDC correlation state is encrypted with this key ring.
builder.AddHrDataProtection("HRManagement.Gateway");
builder.AddHrOpenTelemetry();
builder.AddNationalScope();
builder.AddLegalControl();
builder.AddModuleCatalog();
builder.AddNoForkPolicy();
builder.AddProgramGovernance();
builder.AddOwnership();
builder.AddValueKpi();
builder.AddMigInventory();
builder.AddMigCanonical();
builder.AddMigFactory();
builder.AddMigMatch();
builder.AddMigEvidence();
builder.AddMigCutover();
builder.AddMigOnboarding();
builder.AddExtFederation();
builder.AddExtCivil();
builder.AddExtTreasury();
builder.AddExtPki();
builder.AddExtApi();
builder.AddExtSandbox();
builder.AddExtSector();
builder.AddProdPolicy();
builder.AddProdAgency();
builder.AddProdEss();
builder.AddProdBranch();
builder.AddProdWorkbox();
builder.AddProdSemantic();
builder.AddProdExtensibility();
builder.AddSovBaseline();
builder.AddSovData();
builder.AddSovKeys();
builder.AddSovZt();
builder.AddSovThreat();
builder.AddSovPentest();
builder.AddSovVendor();
builder.AddArcCriticality();
builder.AddArcReference();
builder.AddGovernmentConnectors();
builder.AddHrLocalization();
builder.Services.AddOrganizationBranding(builder.Configuration);
var ratePermit = Math.Clamp(builder.Configuration.GetValue("RateLimiting:PermitLimitPerMinute", 120), 30, 2000);

builder.Services.AddControllersWithViews(o =>
        o.Filters.Add(new AuthorizeFilter(HrManagementPolicies.GatewayDashboard)))
    .AddApplicationPart(typeof(HrWebSharedMarker).Assembly);
builder.Services.AddHttpContextAccessor();
builder.Services.AddTenantContext();
builder.Services.AddHrFileUrlProvider(builder.Configuration);
builder.Services.AddHttpClient();
builder.Services.AddGatewayHealthProbeClient(builder.Configuration);
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IGatewayModuleCatalog, GatewayModuleCatalog>();
builder.Services.AddSingleton<IGatewayJourneyCatalog, GatewayJourneyCatalog>();
builder.Services.AddScoped<IGatewayHealthProbeService, GatewayHealthProbeService>();
builder.Services.AddScoped<IGatewayAuditQueryService, GatewayAuditQueryService>();
builder.Services.AddScoped<IJourneyRebuildService, JourneyRebuildService>();
builder.Services.AddHttpClient("hr-core-journey", (sp, client) =>
{
    var baseUrl = sp.GetRequiredService<IConfiguration>()["Integration:HrCoreBaseUrl"] ?? "http://localhost:5010";
    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddSingleton<GatewayRequestMetricsStore>();
builder.Services.AddSingleton<EvidenceImmutabilityInterceptor>();
builder.Services.AddDbContext<GatewayAuditDbContext>((sp, options) =>
{
    options.UseHrDatabaseProvider(
        builder.Configuration.GetConnectionString("GatewayAuditDb"),
        typeof(GatewayAuditDbContext).Assembly.FullName!);
    options.AddInterceptors(sp.GetRequiredService<EvidenceImmutabilityInterceptor>());
});
builder.Services.Configure<EvidenceRepositoryOptions>(
    builder.Configuration.GetSection(EvidenceRepositoryOptions.SectionName));
builder.Services.AddEvidenceRepository<GatewayAuditDbContext>();
builder.Services.Configure<AuditDurabilityOptions>(
    builder.Configuration.GetSection(AuditDurabilityOptions.SectionName));
builder.Services.Configure<AuditRetentionOptions>(
    builder.Configuration.GetSection(AuditRetentionOptions.SectionName));
builder.Services.Configure<SecurityAuditOptions>(
    builder.Configuration.GetSection(SecurityAuditOptions.SectionName));
builder.Services.AddSingleton<IGatewayAuditQueue, GatewayAuditQueue>();
builder.Services.AddHostedService(sp => (GatewayAuditQueue)sp.GetRequiredService<IGatewayAuditQueue>());
builder.Services.AddSingleton<SecurityAuditPublisher>();
builder.Services.AddSingleton<ISecurityAuditPublisher>(sp => sp.GetRequiredService<SecurityAuditPublisher>());
builder.Services.AddSingleton<ISecurityAuditSink>(sp => sp.GetRequiredService<SecurityAuditPublisher>());
builder.Services.AddSingleton<IAuditChainVerifier, AuditChainVerifier>();
builder.Services.AddSingleton<AuditWormExporter>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AuditWormExporter>());
builder.Services.AddHostedService<SiemDetectorService>();
builder.Services.AddHostedService<GatewayAuditRetentionService>();
builder.Services.AddSingleton<IDurableAuditSink, GatewayDurableAuditSink>();
builder.Services.AddHttpClient("alert-sink");

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = ratePermit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 20,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }));
    options.AddFixedWindowLimiter("gateway-ui", limiter =>
    {
        limiter.PermitLimit = ratePermit;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 20;
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddGatewayTokenForwarding()
    .ConfigureHttpClient((_, handler) =>
    {
        handler.AutomaticDecompression = System.Net.DecompressionMethods.All;
    });

builder.Services.AddGatewayKeycloakAuthentication(builder.Configuration);
builder.Services.AddHrManagementAuthorizationPolicies();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<GatewayAuditDbContext>("gateway-audit-db", tags: ["db", "ready"])
    .AddCheck<GatewayAuditDlqHealthCheck>("gateway-audit-dlq", tags: ["audit", "ready"])
    .AddCheck<SecurityAuditSchemaHealthCheck>("security-audit-store", tags: ["audit", "siem", "ready"]);

var app = builder.Build();
await app.Services.TryMigrateAsync<GatewayAuditDbContext>();

app.UseHrProductionMiddleware(app.Configuration);
app.UseHrLicenseValidation();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    if (app.Configuration.GetValue("Security:EnableHsts", true))
        app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/Home/ErrorStatus/{0}");
app.UseStaticFiles();
app.UseRouting();
app.UseHrLocalization();
app.UseHrCorrelationId();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    var metrics = context.RequestServices.GetRequiredService<GatewayRequestMetricsStore>();
    var pathValue = context.Request.Path.Value ?? string.Empty;
    var isModulePath = pathValue.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
        || pathValue.StartsWith("/ui/", StringComparison.OrdinalIgnoreCase);
    if (!isModulePath)
    {
        await next();
        return;
    }

    var sw = System.Diagnostics.Stopwatch.StartNew();
    await next();
    sw.Stop();
    app.Logger.LogInformation(
        "Gateway routed {Method} {Path} -> {StatusCode} in {Elapsed}ms",
        context.Request.Method,
        context.Request.Path.Value,
        context.Response.StatusCode,
        sw.ElapsedMilliseconds);
    metrics.Record(pathValue, context.Response.StatusCode, sw.ElapsedMilliseconds);
});
app.UseCookiePolicy(new CookiePolicyOptions
{
    MinimumSameSitePolicy = SameSiteMode.Lax,
    Secure = CookieSecurePolicy.SameAsRequest,
    HttpOnly = Microsoft.AspNetCore.CookiePolicy.HttpOnlyPolicy.Always
});
app.UseAuthentication();
app.UsePlatformTenantSession();
app.UseHrTenantId();
app.UseGatewayAccessTokenRefresh();
app.UseAuthorization();
app.UseGatewayAudit();
app.UseGatewayPathAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapHrHealthChecks();
// The "gateway-ui" named rate limiter is registered and available for per-route application
// via .RequireRateLimiting("gateway-ui"). The global limiter already covers all requests;
// use the named policy for stricter per-route limits when needed in the future.
app.MapReverseProxy();

app.MapHrOpenTelemetryEndpoints();

app.Run();

public partial class GatewayWebProgram { }

