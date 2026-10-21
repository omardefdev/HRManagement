using MassTransit;
using Microsoft.EntityFrameworkCore;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Localization;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.ServiceDefaults.Seeding;
using HRManagement.SharedKernel.Tenancy;
using HR.Health.Infrastructure.Consumers;
using HR.Health.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseHrManagementSerilog();
builder.AddHrProductionNetworking();
builder.AddHrDataProtection("HR.Health.Web");
builder.AddHrOpenTelemetry();
builder.AddHrLocalization();

builder.Services.AddControllersWithViews()
    .AddHrModuleAuthorizeFilter(HrManagementPolicies.ModuleHrHealth);
builder.Services.AddHrOpenApi("HR.Health API");

builder.Services.AddHttpContextAccessor();
builder.Services.AddTenantContext();
builder.Services.AddHrFileUrlProvider(builder.Configuration);
builder.Services.AddScoped<AuditSaveChangesInterceptor>();
builder.Services.AddSignalR();

var healthConn = builder.Configuration.GetConnectionString("HealthDb");
builder.Services.AddDbContext<HealthDbContext>((sp, options) =>
    options.UseNpgsql(healthConn, npgsql =>
        npgsql.MigrationsAssembly(typeof(HealthDbContext).Assembly.GetName().Name))
           .AddInterceptors(
               sp.GetRequiredService<AuditSaveChangesInterceptor>(),
               sp.GetRequiredService<TenantIdStampInterceptor>(),
               sp.GetRequiredService<NpgsqlTenantSessionInterceptor>()));

builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<HealthDbContext>());
builder.Services.AddHrPolicyEnforceClient(builder.Configuration);
builder.Services.AddServiceMassTransit(
    builder.Configuration,
    configureOutbox: x => x.AddPostgresOutbox<HealthDbContext>(),
    consumerAssembly: typeof(EmployeeCreatedConsumer).Assembly);
builder.Services.AddModuleAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddHrManagementAuthorizationPolicies();
builder.Services.AddHrServiceHealthChecks<HealthDbContext>(builder.Configuration);

var app = builder.Build();
app.UseHrProductionMiddleware(app.Configuration);

await app.Services.TryMigrateAsync<HealthDbContext>();
var seedProfile = SeedProfileResolver.Resolve(app.Configuration, app.Environment);

if (seedProfile != SeedProfile.None)
{
    try
    {
        using var demoScope = app.Services.CreateScope();
        TenantIdFlow.Set(TenantDefaults.DefaultTenantId);
        try
        {
            var db = demoScope.ServiceProvider.GetRequiredService<HealthDbContext>();
            if (seedProfile == SeedProfile.Acceptance)
                await AcceptanceHealthSeeder.SeedAsync(db);
            else
                await DemoHealthSeeder.SeedAsync(db);
        }
        finally
        {
            TenantIdFlow.Set(null);
        }
    }
    catch (Exception ex) when (seedProfile != SeedProfile.Acceptance)
    {
        app.Logger.LogWarning(ex, "HR.Health demo seed skipped.");
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
    pattern: "{controller=MedicalRecommendations}/{action=Index}/{id?}");

app.MapHrHealthChecks();
app.MapHub<NotificationHub>("/hubs/notifications");

app.MapHrOpenTelemetryEndpoints();

app.Run();
