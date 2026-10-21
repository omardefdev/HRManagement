using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.ServiceDefaults.Privacy;
using HRManagement.ServiceDefaults.Localization;
using HRManagement.ServiceDefaults.CodeGeneration;
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
using HRManagement.ServiceDefaults.Seeding;
using HRManagement.Documents.QuestPdf;
using HR.Core.Infrastructure;
using HR.Core.Infrastructure.Consumers;
using HR.Core.Infrastructure.Middleware;
using HR.Core.Infrastructure.Services.Establishment;
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

builder.Services.AddHrQuestPdfDocuments();
builder.Services.AddControllersWithViews()
    .AddHrModuleAuthorizeFilter(HrManagementPolicies.ModuleHrCore);
builder.Services.AddHrOpenApi("HR.Core API");

builder.Services.AddHttpContextAccessor();
builder.Services.AddTenantContext();
builder.Services.AddHrFileUrlProvider(builder.Configuration);
builder.Services.AddSingleton<AuditSaveChangesInterceptor>();
builder.Services.AddSingleton<EmployeePiiProtectionInterceptor>();
builder.Services.AddSingleton<EmployeePiiMaterializationInterceptor>();
builder.Services.AddSingleton<ArabicNameNormalizationInterceptor>();
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
builder.Services.AddFileReferenceOutbox<HRCoreDbContext>(builder.Configuration);
builder.Services.AddScoped<HR.Core.Domain.Services.RequisitionCloseService>();
builder.Services.AddScoped<HR.Core.Infrastructure.Services.Recruitment.RecruitmentHireClosureService>();
builder.Services.AddScoped<HR.Core.Infrastructure.Integration.RecruitmentIntegrationService>();
builder.Services.AddScoped<HR.Core.Infrastructure.Integration.DiwanIntegrationService>();
builder.Services.AddScoped<HR.Core.Infrastructure.Integration.AttendanceIntegrationService>();
builder.Services.AddScoped<HR.Core.Infrastructure.Services.LeaveScheduleReconciler>();
if (builder.Configuration.GetValue("LeaveScheduleReconciler:Enabled", true))
    builder.Services.AddHostedService<HR.Core.Infrastructure.Services.LeaveScheduleReconcilerJob>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<HR.Core.Infrastructure.IOrgUnitHierarchyCache, HR.Core.Infrastructure.OrgUnitHierarchyCache>();
builder.Services.AddScoped<HR.Core.Infrastructure.OrgScopeService>();
builder.Services.AddScoped<HR.Core.Web.Services.EstablishmentScopeService>();
builder.Services.AddScoped<HR.Core.Infrastructure.OrgUnitPathService>();
builder.Services.AddScoped<HR.Core.Domain.Services.EstablishmentService>();
builder.Services.AddScoped<HR.Core.Infrastructure.Services.Establishment.IEstablishmentChangeApplier, HR.Core.Infrastructure.Services.Establishment.EstablishmentChangeApplier>();
builder.Services.AddScoped<HR.Core.Infrastructure.Services.Establishment.EstablishmentChangeLifecycleService>();
builder.Services.AddScoped<HR.Core.Web.Services.EstablishmentChangeWorkflowUiService>();
builder.Services.AddScoped<HR.Core.Infrastructure.Services.Establishment.EstablishmentChangeWorkflowHandler>();
builder.Services.AddScoped<HR.Core.Infrastructure.Services.Establishment.EstablishmentLegalSourceValidator>();
builder.Services.AddScoped<HRManagement.SharedKernel.MasterData.IReferenceDataProvider, HR.Core.Infrastructure.Services.MasterData.ReferenceDataProvider>();
builder.Services.AddScoped<HR.Core.Infrastructure.Services.MasterData.IReferenceCatalogAdminService, HR.Core.Infrastructure.Services.MasterData.ReferenceCatalogAdminService>();
builder.Services.AddScoped<HR.Core.Infrastructure.Services.Establishment.EstablishmentReferenceValidator>();
builder.Services.AddScoped<HR.Core.Web.Services.EstablishmentPayloadSummaryBuilder>();
builder.Services.AddHrEstablishmentTemporal();
builder.Services.AddHrEstablishmentPlanning();
builder.Services.AddScoped<HR.Core.Domain.Services.RecruitmentRequisitionService>();
builder.Services.AddScoped<HR.Core.Infrastructure.Services.Recruitment.RecruitmentRequisitionLifecycleService>();
builder.Services.AddScoped<HR.Core.Web.Services.RecruitmentRequisitionWorkflowUiService>();
builder.Services.AddScoped<HR.Core.Web.Services.OfferLetterWorkflowUiService>();
builder.Services.AddScoped<HR.Core.Infrastructure.Services.Recruitment.RecruitmentApplicationIntakeService>();
builder.Services.AddScoped<HR.Core.Infrastructure.Services.Recruitment.RecruitmentRequisitionWorkflowHandler>();
builder.Services.AddScoped<HR.Core.Infrastructure.Services.Recruitment.RecruitmentSelectionService>();
builder.Services.AddScoped<HR.Core.Infrastructure.Services.Recruitment.RecruitmentOfferService>();
builder.Services.AddScoped<HR.Core.Infrastructure.Services.Recruitment.OfferLetterWorkflowHandler>();
builder.Services.AddScoped<HR.Core.Infrastructure.Services.Recruitment.RecruitmentAppealService>();
builder.Services.AddScoped<HR.Core.Infrastructure.Services.Recruitment.RecruitmentAppealWorkflowHandler>();
builder.Services.AddScoped<HR.Core.Web.Services.RecruitmentAppealWorkflowUiService>();
builder.Services.AddScoped<HR.Core.Infrastructure.Services.Recruitment.RecruitmentInterviewService>();
builder.Services.Configure<HR.Core.Infrastructure.Services.Recruitment.RecruitmentOptions>(
    builder.Configuration.GetSection(HR.Core.Infrastructure.Services.Recruitment.RecruitmentOptions.SectionName));
builder.Services.AddScoped<HR.Core.Infrastructure.Services.Recruitment.RecruitmentAssessmentCriteriaService>();
builder.Services.AddScoped<HR.Core.Infrastructure.Services.Recruitment.SelectionCommitteeService>();
builder.Services.AddScoped<HR.Core.Infrastructure.Services.Recruitment.SelectionCommitteeLifecycleService>();
builder.Services.AddScoped<HR.Core.Infrastructure.Services.Recruitment.SelectionCommitteeWorkflowHandler>();
builder.Services.AddScoped<HR.Core.Web.Services.SelectionCommitteeWorkflowUiService>();
builder.Services.AddScoped<HR.Core.Web.Services.WorkflowInboxEligibilityService>();
builder.Services.AddScoped<HR.Core.Web.Services.WorkflowDeepLinkResolver>();
builder.Services.AddScoped<HR.Core.Web.Services.WorkflowInboxQueryService>();
builder.Services.AddScoped<HR.Core.Web.Services.HrCoreWorkInboxService>();
builder.Services.AddScoped<IWorkInboxService, HR.Core.Web.Services.HrCoreWorkInboxService>();
builder.Services.AddPublicCareersRateLimiting(builder.Configuration);
builder.Services.AddScoped<HR.Core.Infrastructure.Integration.OrgUnitIntegrationService>();
builder.Services.AddExportJobInfrastructure(builder.Configuration);
builder.Services.AddScoped<HR.Core.Infrastructure.ExportJobs.IHRCoreExportJobService, HR.Core.Infrastructure.ExportJobs.HRCoreExportJobService>();
builder.Services.AddScoped<HR.Core.Infrastructure.Onboarding.AgencyOrgStructureProvisioner>();
builder.Services.AddScoped<HR.Core.Infrastructure.Onboarding.AgencyProvisioningService>();
builder.Services.AddScoped<HRManagement.ServiceDefaults.Tenancy.ITenantQuarantineQuery, HR.Core.Infrastructure.Tenancy.HrCoreTenantQuarantineQuery>();
builder.Services.AddScoped<HRManagement.ServiceDefaults.Tenancy.TenantQuarantineGuard>();
builder.Services.AddServiceMassTransit(
    builder.Configuration,
    configureOutbox: x => x.AddPostgresOutbox<HRCoreDbContext>(),
    consumerAssembly: typeof(TrainingCompletedConsumer).Assembly);
builder.Services.AddHrCoreExternalIntegrations(builder.Configuration);
builder.Services.AddModuleAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddHrManagementAuthorizationPolicies();
builder.Services.AddOrgScopeAuthorization();
builder.Services.AddHrPolicyEngine();
builder.Services.AddHrOrgStructure();
builder.Services.AddHrLegislationRegister();
builder.Services.AddHrServiceRegimes();
builder.Services.AddHrCodeGenerationEngine();
builder.Services.AddHrPolicyEnforceClient(builder.Configuration);
builder.Services.AddHrCodeGeneration(builder.Configuration);
builder.Services.AddOrganizationBranding(builder.Configuration);
builder.Services.AddHrServiceHealthChecks<HRCoreDbContext>(builder.Configuration);

var app = builder.Build();
app.UseHrProductionMiddleware(app.Configuration);

await app.Services.TryMigrateAsync<HRCoreDbContext>();
var seedProfile = SeedProfileResolver.Resolve(app.Configuration, app.Environment);
try
{
    using var scope = app.Services.CreateScope();
    TenantIdFlow.Set(HRManagement.SharedKernel.Tenancy.TenantDefaults.DefaultTenantId);
    try
    {
        var coreDb = scope.ServiceProvider.GetRequiredService<HRCoreDbContext>();
        await HR.Core.Infrastructure.Seeding.NationalSeedOrchestrator.ApplyMandatoryAsync(coreDb, scope.ServiceProvider);
        if (seedProfile == SeedProfile.Demo)
        {
            try
            {
                await HR.Core.Infrastructure.Seeding.DemoSeedOrchestrator.ApplyAsync(coreDb, scope.ServiceProvider);
            }
            catch (Exception demoEx)
            {
                app.Logger.LogWarning(demoEx, "HR.Core demo seed skipped.");
            }
        }
        if (seedProfile == SeedProfile.Acceptance)
            await HR.Core.Infrastructure.Seeding.AcceptanceSeedOrchestrator.ApplyAsync(coreDb, scope.ServiceProvider);
        await scope.ServiceProvider.GetRequiredService<HR.Core.Infrastructure.OrgUnitPathService>().RecomputeAllAsync();
        await scope.ServiceProvider.GetRequiredService<HR.Core.Infrastructure.Integration.OrgUnitIntegrationService>().PublishAllOrgUnitsAsync();
    }
    finally
    {
        TenantIdFlow.Set(null);
    }
}
catch (Exception ex) when (seedProfile != SeedProfile.Acceptance)
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
app.UsePublicCareersRateLimiting();
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
