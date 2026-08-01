using MassTransit;
using Microsoft.EntityFrameworkCore;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Privacy;
using Compliance.Infrastructure.Consumers;
using Compliance.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseHrManagementSerilog();
builder.AddHrProductionNetworking();
builder.AddHrDataProtection("Compliance.Web");
builder.AddHrOpenTelemetry();
builder.Services.AddHrDsarDeletion();

builder.Services.AddControllersWithViews()
    .AddHrModuleAuthorizeFilter(HrManagementPolicies.ModuleCompliance);
builder.Services.AddHrOpenApi("Compliance API");

builder.Services.AddHttpContextAccessor();
builder.Services.AddTenantContext();
builder.Services.AddHrFileUrlProvider(builder.Configuration);
builder.Services.AddHrFileReferenceClient(builder.Configuration);
builder.Services.AddScoped<AuditSaveChangesInterceptor>();
builder.Services.AddSignalR();

var complianceConn = builder.Configuration.GetConnectionString("ComplianceDb");
builder.Services.AddDbContext<ComplianceDbContext>((sp, options) =>
    options.UseNpgsql(complianceConn, npgsql =>
        npgsql.MigrationsAssembly(typeof(ComplianceDbContext).Assembly.GetName().Name))
           .AddInterceptors(
               sp.GetRequiredService<AuditSaveChangesInterceptor>(),
               sp.GetRequiredService<TenantIdStampInterceptor>(),
               sp.GetRequiredService<NpgsqlTenantSessionInterceptor>()));

builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<ComplianceDbContext>());
builder.Services.AddExportJobInfrastructure(builder.Configuration);
builder.Services.AddScoped<Compliance.Infrastructure.ExportJobs.IComplianceExportJobService, Compliance.Infrastructure.ExportJobs.ComplianceExportJobService>();
builder.Services.AddServiceMassTransit(
    builder.Configuration,
    configureOutbox: x => x.AddPostgresOutbox<ComplianceDbContext>(),
    consumerAssembly: typeof(MedicalRecommendationIssuedConsumer).Assembly);
builder.Services.AddModuleAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddHrManagementAuthorizationPolicies();
builder.Services.AddHrServiceHealthChecks<ComplianceDbContext>(builder.Configuration);

var app = builder.Build();
app.UseHrProductionMiddleware(app.Configuration);

await app.Services.TryMigrateAsync<ComplianceDbContext>();

if (app.Environment.IsDevelopment())
{
    using var demoScope = app.Services.CreateScope();
    TenantIdFlow.Set(HRManagement.SharedKernel.Tenancy.TenantDefaults.DefaultTenantId);
    try
    {
        await DemoComplianceSeeder.SeedAsync(
            demoScope.ServiceProvider.GetRequiredService<ComplianceDbContext>());
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
    pattern: "{controller=Complaints}/{action=Index}/{id?}");

app.MapHrHealthChecks();
app.MapHub<NotificationHub>("/hubs/notifications");

app.MapHrOpenTelemetryEndpoints();

app.Run();

