using Microsoft.EntityFrameworkCore;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Localization;
using HRManagement.Documents.QuestPdf;
using HRManagement.ServiceDefaults.CodeGeneration;
using HRManagement.ServiceDefaults.Seeding;
using Admin.Logistics.Infrastructure;
using Admin.Logistics.Infrastructure.Abstractions;
using Admin.Logistics.Infrastructure.Consumers;
using Admin.Logistics.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseHrManagementSerilog();
builder.AddHrProductionNetworking();
builder.AddHrDataProtection("Admin.Logistics.Web");
builder.AddHrOpenTelemetry();
builder.AddHrLocalization();

builder.Services.AddHrQuestPdfDocuments();
builder.Services.AddControllersWithViews()
    .AddHrModuleAuthorizeFilter(HrManagementPolicies.ModuleAdminLogistics);
builder.Services.AddHrOpenApi("Admin.Logistics API");

builder.Services.AddHttpContextAccessor();
builder.Services.AddTenantContext();
builder.Services.AddHrFileUrlProvider(builder.Configuration);
builder.Services.AddHrFileReferenceClient(builder.Configuration);
builder.Services.AddAdminLogisticsInfrastructure(builder.Configuration);
builder.Services.AddHrCodeGeneration(builder.Configuration);
builder.Services.AddScoped<AuditSaveChangesInterceptor>();
builder.Services.AddSignalR();

var adminConn = builder.Configuration.GetConnectionString("AdminDb");
builder.Services.AddDbContext<AdminLogisticsDbContext>((sp, options) =>
    options.UseNpgsql(adminConn, npgsql =>
        npgsql.MigrationsAssembly(typeof(AdminLogisticsDbContext).Assembly.GetName().Name))
           .AddInterceptors(
               sp.GetRequiredService<AuditSaveChangesInterceptor>(),
               sp.GetRequiredService<TenantIdStampInterceptor>(),
               sp.GetRequiredService<NpgsqlTenantSessionInterceptor>()));

builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<AdminLogisticsDbContext>());
builder.Services.AddServiceMassTransit(
    builder.Configuration,
    consumerAssembly: typeof(SeparationCompletedConsumer).Assembly);
builder.Services.AddModuleAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddHrManagementAuthorizationPolicies();
builder.Services.AddHrServiceHealthChecks<AdminLogisticsDbContext>(builder.Configuration);

var app = builder.Build();
app.UseHrProductionMiddleware(app.Configuration);

await app.Services.TryMigrateAsync<AdminLogisticsDbContext>();
var seedProfile = SeedProfileResolver.Resolve(app.Configuration, app.Environment);
try
{
    using var scope = app.Services.CreateScope();
    var logisticsDb = scope.ServiceProvider.GetRequiredService<AdminLogisticsDbContext>();

    if (seedProfile == SeedProfile.Acceptance)
        await AcceptanceLogisticsSeeder.SeedAsync(logisticsDb);
    else if (seedProfile == SeedProfile.Demo)
        await DemoLogisticsSeeder.SeedAsync(logisticsDb);

    if (app.Environment.IsDevelopment())
    {
        var reclassifyOnStartup = app.Configuration.GetValue("Assets:ReclassifyOnStartup", true);
        if (reclassifyOnStartup)
        {
            var classifier = scope.ServiceProvider.GetRequiredService<IAssetClassifier>();
            var count = await classifier.ReclassifyAllAsync();
            app.Logger.LogInformation("Asset policy reclassification applied to {Count} assets.", count);
        }
    }
}
catch (Exception ex) when (seedProfile != SeedProfile.Acceptance)
{
    app.Logger.LogWarning(ex, "Admin.Logistics demo seed skipped.");
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
    pattern: "{controller=Assets}/{action=Index}/{id?}");

app.MapHrHealthChecks();
app.MapHub<NotificationHub>("/hubs/notifications");

app.MapHrOpenTelemetryEndpoints();

app.Run();

public partial class AdminLogisticsWebProgram { }
