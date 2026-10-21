using Dev.Analytics.Infrastructure.Consumers;
using Dev.Analytics.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Localization;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.ServiceDefaults.Seeding;
using MassTransit;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseHrManagementSerilog();
builder.AddHrProductionNetworking();
builder.AddHrDataProtection("Dev.Analytics.Web");
builder.AddHrOpenTelemetry();
builder.AddHrLocalization();

builder.Services.AddControllersWithViews()
    .AddHrModuleAuthorizeFilter(HrManagementPolicies.ModuleDevAnalytics);
builder.Services.AddHrOpenApi("Dev.Analytics API");

builder.Services.AddHttpContextAccessor();
builder.Services.AddTenantContext();
builder.Services.AddHrFileUrlProvider(builder.Configuration);
builder.Services.AddScoped<AuditSaveChangesInterceptor>();
builder.Services.AddSignalR();

var analyticsConn = builder.Configuration.GetConnectionString("AnalyticsDb");
builder.Services.AddDbContext<AnalyticsDbContext>((sp, options) =>
    options.UseNpgsql(analyticsConn, npgsql =>
        npgsql.MigrationsAssembly(typeof(AnalyticsDbContext).Assembly.GetName().Name))
           .AddInterceptors(
               sp.GetRequiredService<AuditSaveChangesInterceptor>(),
               sp.GetRequiredService<TenantIdStampInterceptor>(),
               sp.GetRequiredService<NpgsqlTenantSessionInterceptor>()));

builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<AnalyticsDbContext>());
builder.Services.AddHrPolicyEnforceClient(builder.Configuration);
builder.Services.AddExportJobInfrastructure(builder.Configuration);
builder.Services.AddScoped<Dev.Analytics.Infrastructure.ExportJobs.IAnalyticsExportJobService, Dev.Analytics.Infrastructure.ExportJobs.AnalyticsExportJobService>();
builder.Services.AddSingleton<Dev.Analytics.Application.Services.IReadModelReconciliationStatusStore, Dev.Analytics.Application.Services.InMemoryReadModelReconciliationStatusStore>();
builder.Services.AddScoped<Dev.Analytics.Application.Services.ReadModelReconciliationService>();
builder.Services.AddServiceMassTransit(
    builder.Configuration,
    consumerAssembly: typeof(PayrollProcessedConsumer).Assembly);
builder.Services.AddModuleAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddHrManagementAuthorizationPolicies();
builder.Services.AddHrServiceHealthChecks<AnalyticsDbContext>(builder.Configuration);

var app = builder.Build();
app.UseHrProductionMiddleware(app.Configuration);

await app.Services.TryMigrateAsync<AnalyticsDbContext>();
var seedProfile = SeedProfileResolver.Resolve(app.Configuration, app.Environment);

if (seedProfile != SeedProfile.None)
{
    using var demoScope = app.Services.CreateScope();
    TenantIdFlow.Set(HRManagement.SharedKernel.Tenancy.TenantDefaults.DefaultTenantId);
    try
    {
        var db = demoScope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
        if (seedProfile == SeedProfile.Acceptance)
            await AcceptanceAnalyticsSeeder.SeedAsync(db);
        else
            await DemoAnalyticsSeeder.SeedAsync(db);
    }
    finally
    {
        TenantIdFlow.Set(null);
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseHrOpenApi();

app.UseStaticFiles();
app.UseRouting();
app.UseHrLocalization();
app.UseHrCorrelationId();
app.UseAuthentication();
app.UseHrTenantId();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Reports}/{action=Index}/{id?}");

app.MapControllers();
app.MapHrHealthChecks();
app.MapHub<NotificationHub>("/hubs/notifications");

app.MapHrOpenTelemetryEndpoints();

app.Run();

public partial class DevAnalyticsWebProgram { }
