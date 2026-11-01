using Microsoft.EntityFrameworkCore;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.CodeGeneration;
using HRManagement.ServiceDefaults.Localization;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.ServiceDefaults.Seeding;
using IT.Infra.Infrastructure.Consumers;
using IT.Infra.Infrastructure.Data;
using IT.Infra.Infrastructure.Integration;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseHrManagementSerilog();
builder.AddHrProductionNetworking();
builder.AddHrDataProtection("IT.Infra.Web");
builder.AddHrOpenTelemetry();
builder.AddHrLocalization();

builder.Services.AddControllersWithViews()
    .AddHrModuleAuthorizeFilter(HrManagementPolicies.ModuleItInfra);
builder.Services.AddHrOpenApi("IT.Infra API");

builder.Services.AddHttpContextAccessor();
builder.Services.AddTenantContext();
builder.Services.AddHrFileUrlProvider(builder.Configuration);
builder.Services.AddScoped<AuditSaveChangesInterceptor>();
builder.Services.AddSignalR();

var itConn = builder.Configuration.GetConnectionString("ITInfraDb");
builder.Services.AddDbContext<ITInfraDbContext>((sp, options) =>
    options.UseNpgsql(itConn, npgsql =>
        npgsql.MigrationsAssembly(typeof(ITInfraDbContext).Assembly.GetName().Name))
           .AddInterceptors(
               sp.GetRequiredService<AuditSaveChangesInterceptor>(),
               sp.GetRequiredService<TenantIdStampInterceptor>(),
               sp.GetRequiredService<NpgsqlTenantSessionInterceptor>()));

builder.Services.AddDirectorySyncClient(builder.Configuration);

builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<ITInfraDbContext>());
builder.Services.AddHrPolicyEnforceClient(builder.Configuration);
builder.Services.AddHrCodeGeneration(builder.Configuration);
builder.Services.AddServiceMassTransit(
    builder.Configuration,
    consumerAssembly: typeof(EmployeeCreatedConsumer).Assembly);
builder.Services.AddModuleAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddHrManagementAuthorizationPolicies();
builder.Services.AddHrServiceHealthChecks<ITInfraDbContext>(builder.Configuration);

var app = builder.Build();
app.UseHrProductionMiddleware(app.Configuration);

await app.Services.TryMigrateAsync<ITInfraDbContext>();
var seedProfile = SeedProfileResolver.Resolve(app.Configuration, app.Environment);
try
{
    if (seedProfile != SeedProfile.None)
    {
        using var scope = app.Services.CreateScope();
        var itDb = scope.ServiceProvider.GetRequiredService<ITInfraDbContext>();
        if (seedProfile == SeedProfile.Acceptance)
            await AcceptanceITSeeder.SeedAsync(itDb);
        else
            await DemoITSeeder.SeedAsync(itDb);
    }
}
catch (Exception ex) when (seedProfile != SeedProfile.Acceptance)
{
    app.Logger.LogWarning(ex, "IT.Infra demo seed skipped.");
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
    pattern: "{controller=UserDevices}/{action=Index}/{id?}");

app.MapHrHealthChecks();
app.MapHub<NotificationHub>("/hubs/notifications");

app.MapHrOpenTelemetryEndpoints();

app.Run();
