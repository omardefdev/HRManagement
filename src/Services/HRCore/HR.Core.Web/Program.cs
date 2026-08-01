using MassTransit;
using Microsoft.EntityFrameworkCore;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Privacy;
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
using HRManagement.ServiceDefaults.Signatures;
using HRManagement.ServiceDefaults.Workflow;
using HR.Core.Infrastructure;
using HR.Core.Infrastructure.Consumers;
using HR.Core.Infrastructure.Data;
using HRManagement.Integration.FileAdapters;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseHrManagementSerilog();
builder.AddHrProductionNetworking();
builder.AddHrOpenTelemetry();
builder.AddHrDataProtection("HR.Core");
builder.Services.AddHrPiiFieldEncryption();
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
builder.AddLegalSignatures();
builder.AddHrLocalization();
builder.Services.AddWorkflowEngine();

builder.Services.AddControllersWithViews()
    .AddHrModuleAuthorizeFilter(HrManagementPolicies.ModuleHrCore);
builder.Services.AddHrOpenApi("HR.Core API");

builder.Services.AddHttpContextAccessor();
builder.Services.AddTenantContext();
builder.Services.AddHrFileUrlProvider(builder.Configuration);
builder.Services.AddScoped<AuditSaveChangesInterceptor>();
builder.Services.AddScoped<EmployeePiiProtectionInterceptor>();
builder.Services.AddScoped<EmployeePiiMaterializationInterceptor>();
builder.Services.AddScoped<ArabicNameNormalizationInterceptor>();
builder.Services.AddSignalR();

var coreConn = builder.Configuration.GetConnectionString("HRCoreDb");
builder.Services.AddDbContext<HRCoreDbContext>((sp, options) =>
    options.UseHrDatabaseProvider(
            coreConn,
            typeof(HRCoreDbContext).Assembly.GetName().Name!)
           .AddInterceptors(
               sp.GetRequiredService<AuditSaveChangesInterceptor>(),
               sp.GetRequiredService<TenantIdStampInterceptor>(),
               sp.GetRequiredService<NpgsqlTenantSessionInterceptor>(),
               sp.GetRequiredService<SignatureImmutabilityInterceptor>(),
               sp.GetRequiredService<ArabicNameNormalizationInterceptor>(),
               sp.GetRequiredService<EmployeePiiProtectionInterceptor>(),
               sp.GetRequiredService<EmployeePiiMaterializationInterceptor>()));

builder.Services.AddRepositories<HRCoreDbContext>();
builder.Services.AddScoped<HR.Core.Infrastructure.Integration.EmployeeIntegrationService>();
builder.Services.AddScoped<HR.Core.Web.Services.IEmployeeDetailsViewBuilder, HR.Core.Web.Services.EmployeeDetailsViewBuilder>();
builder.Services.AddHrFileReferenceClient(builder.Configuration);
builder.Services.AddFileReferenceOutbox<HRCoreDbContext>();
builder.Services.AddScoped<HR.Core.Infrastructure.Integration.RecruitmentIntegrationService>();
builder.Services.AddScoped<HR.Core.Infrastructure.Integration.DiwanIntegrationService>();
builder.Services.AddScoped<HR.Core.Infrastructure.Integration.AttendanceIntegrationService>();
builder.Services.AddScoped<HR.Core.Infrastructure.Services.LeaveScheduleReconciler>();
builder.Services.AddHostedService<HR.Core.Infrastructure.Services.LeaveScheduleReconcilerJob>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<HR.Core.Infrastructure.IOrgUnitHierarchyCache, HR.Core.Infrastructure.OrgUnitHierarchyCache>();
builder.Services.AddScoped<HR.Core.Infrastructure.OrgScopeService>();
builder.Services.AddScoped<HR.Core.Infrastructure.OrgUnitPathService>();
builder.Services.AddScoped<HR.Core.Infrastructure.Integration.OrgUnitIntegrationService>();
builder.Services.AddExportJobInfrastructure(builder.Configuration);
builder.Services.AddScoped<HR.Core.Infrastructure.ExportJobs.IHRCoreExportJobService, HR.Core.Infrastructure.ExportJobs.HRCoreExportJobService>();
builder.Services.AddServiceMassTransit(
    builder.Configuration,
    configureOutbox: x => x.AddPostgresOutbox<HRCoreDbContext>(),
    consumerAssembly: typeof(TrainingCompletedConsumer).Assembly);
builder.Services.AddHrCoreExternalIntegrations(builder.Configuration);
builder.Services.AddModuleAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddHrManagementAuthorizationPolicies();
builder.Services.AddOrganizationBranding(builder.Configuration);
builder.Services.AddHrServiceHealthChecks<HRCoreDbContext>(builder.Configuration);

var app = builder.Build();
app.UseHrProductionMiddleware(app.Configuration);

await app.Services.TryMigrateAsync<HRCoreDbContext>();
try
{
    using var scope = app.Services.CreateScope();
    TenantIdFlow.Set(HRManagement.SharedKernel.Tenancy.TenantDefaults.DefaultTenantId);
    try
    {
        var coreDb = scope.ServiceProvider.GetRequiredService<HRCoreDbContext>();
        await HRCoreDataSeeder.SeedAsync(coreDb, scope.ServiceProvider);
        if (app.Environment.IsDevelopment())
        {
            var logger = scope.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger("DemoData");
            await DemoDataSeeder.SeedAsync(coreDb, logger);
        }
        await scope.ServiceProvider.GetRequiredService<HR.Core.Infrastructure.OrgUnitPathService>().RecomputeAllAsync();
        await scope.ServiceProvider.GetRequiredService<HR.Core.Infrastructure.Integration.OrgUnitIntegrationService>().PublishAllOrgUnitsAsync();
    }
    finally
    {
        TenantIdFlow.Set(null);
    }
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "HR.Core seed skipped.");
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
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

app.MapHrHealthChecks();
app.MapHub<NotificationHub>("/hubs/notifications");

app.MapHrOpenTelemetryEndpoints();

app.Run();

public partial class HRCoreWebProgram { }

