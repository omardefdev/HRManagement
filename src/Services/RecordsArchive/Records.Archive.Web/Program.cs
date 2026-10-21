using MassTransit;
using Microsoft.EntityFrameworkCore;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Localization;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.ServiceDefaults.CodeGeneration;
using HRManagement.ServiceDefaults.Seeding;
using Records.Archive.Infrastructure.Consumers;
using Records.Archive.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseHrManagementSerilog();
builder.AddHrProductionNetworking();
builder.AddHrDataProtection("Records.Archive.Web");
builder.AddHrOpenTelemetry();
builder.AddHrLocalization();

builder.Services.AddControllersWithViews()
    .AddHrModuleAuthorizeFilter(HrManagementPolicies.ModuleRecordsArchive);
builder.Services.AddHrOpenApi("Records.Archive API");

builder.Services.AddHttpContextAccessor();
builder.Services.AddTenantContext();
builder.Services.AddHrFileUrlProvider(builder.Configuration);
builder.Services.AddScoped<AuditSaveChangesInterceptor>();
builder.Services.AddSignalR();

var archiveConn = builder.Configuration.GetConnectionString("ArchiveDb");
builder.Services.AddDbContext<ArchiveDbContext>((sp, options) =>
    options.UseNpgsql(archiveConn, npgsql =>
        npgsql.MigrationsAssembly(typeof(ArchiveDbContext).Assembly.GetName().Name))
           .AddInterceptors(
               sp.GetRequiredService<AuditSaveChangesInterceptor>(),
               sp.GetRequiredService<TenantIdStampInterceptor>(),
               sp.GetRequiredService<NpgsqlTenantSessionInterceptor>()));

builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<ArchiveDbContext>());
builder.Services.AddHrPolicyEnforceClient(builder.Configuration);
builder.Services.AddHrCodeGeneration(builder.Configuration);
builder.Services.AddExportJobInfrastructure(builder.Configuration);
builder.Services.AddScoped<Records.Archive.Infrastructure.ExportJobs.IArchiveExportJobService, Records.Archive.Infrastructure.ExportJobs.ArchiveExportJobService>();
builder.Services.AddServiceMassTransit(
    builder.Configuration,
    consumerAssembly: typeof(SeparationCompletedConsumer).Assembly);
builder.Services.AddHrFileReferenceClient(builder.Configuration);
builder.Services.AddModuleAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddHrManagementAuthorizationPolicies();
builder.Services.AddHrServiceHealthChecks<ArchiveDbContext>(builder.Configuration);

var app = builder.Build();
app.UseHrProductionMiddleware(app.Configuration);

await app.Services.TryMigrateAsync<ArchiveDbContext>();
var seedProfile = SeedProfileResolver.Resolve(app.Configuration, app.Environment);
try
{
    if (seedProfile != SeedProfile.None)
    {
        using var scope = app.Services.CreateScope();
        var archiveDb = scope.ServiceProvider.GetRequiredService<ArchiveDbContext>();
        if (seedProfile == SeedProfile.Acceptance)
            await AcceptanceArchiveSeeder.SeedAsync(archiveDb);
        else
            await DemoArchiveSeeder.SeedAsync(archiveDb);
    }
}
catch (Exception ex) when (seedProfile != SeedProfile.Acceptance)
{
    app.Logger.LogWarning(ex, "Records.Archive demo seed skipped.");
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
    pattern: "{controller=ArchiveItems}/{action=Index}/{id?}");

app.MapHrHealthChecks();
app.MapHub<NotificationHub>("/hubs/notifications");

app.MapHrOpenTelemetryEndpoints();

app.Run();
