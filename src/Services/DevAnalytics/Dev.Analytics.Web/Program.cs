using Dev.Analytics.Infrastructure.Consumers;
using Dev.Analytics.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using MassTransit;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseHrManagementSerilog();
builder.AddHrProductionNetworking();
builder.AddHrDataProtection("Dev.Analytics.Web");
builder.AddHrOpenTelemetry();

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
builder.Services.AddExportJobInfrastructure(builder.Configuration);
builder.Services.AddScoped<Dev.Analytics.Infrastructure.ExportJobs.IAnalyticsExportJobService, Dev.Analytics.Infrastructure.ExportJobs.AnalyticsExportJobService>();
builder.Services.AddServiceMassTransit(
    builder.Configuration,
    consumerAssembly: typeof(PayrollProcessedConsumer).Assembly);
builder.Services.AddModuleAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddHrManagementAuthorizationPolicies();
builder.Services.AddHrServiceHealthChecks<AnalyticsDbContext>(builder.Configuration);

var app = builder.Build();
app.UseHrProductionMiddleware(app.Configuration);

await app.Services.TryMigrateAsync<AnalyticsDbContext>();

if (app.Environment.IsDevelopment())
{
    using var demoScope = app.Services.CreateScope();
    TenantIdFlow.Set(HRManagement.SharedKernel.Tenancy.TenantDefaults.DefaultTenantId);
    try
    {
        await DemoAnalyticsSeeder.SeedAsync(
            demoScope.ServiceProvider.GetRequiredService<AnalyticsDbContext>());
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

