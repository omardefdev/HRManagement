using MassTransit;
using Microsoft.EntityFrameworkCore;
using Payroll.Infrastructure.Consumers;
using Payroll.Infrastructure.Data;
using Payroll.Infrastructure;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Messaging;
using HRManagement.Documents.QuestPdf;
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
using HRManagement.ServiceDefaults.Seeding;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseHrManagementSerilog();
builder.AddHrProductionNetworking();
builder.AddHrDataProtection("Payroll.Web");
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
builder.AddGovernmentConnectors();
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
builder.AddHrLocalization();

builder.Services.AddHrQuestPdfDocuments();
builder.Services.AddControllersWithViews()
    .AddHrModuleAuthorizeFilter(HrManagementPolicies.ModulePayroll, HrManagementPolicies.PayrollWrite)
    // PLAN-53 (§2 rule 3): the branch scope of the salary change requests and of the pay profiles (a foreign id is 404 before the action).
    .AddOrgScopeFilter<PayrollDbContext>(
        Payroll.Infrastructure.OrgScope.PayrollOrgScopeKeys.Service, Payroll.Infrastructure.OrgScope.PayrollOrgScopeKeys.Configure);
builder.Services.AddSnapshotOrgScopeProvider<PayrollDbContext>();
builder.Services.AddHrOpenApi("Payroll API");

builder.Services.AddHttpContextAccessor();
builder.Services.AddTenantContext();
builder.Services.AddHrFileUrlProvider(builder.Configuration);
builder.Services.AddScoped<AuditSaveChangesInterceptor>();
builder.Services.AddSignalR();

var payrollConn = builder.Configuration.GetConnectionString("PayrollDb");
builder.Services.AddDbContext<PayrollDbContext>((sp, options) =>
    options.UseNpgsql(payrollConn, npgsql =>
        npgsql.MigrationsAssembly(typeof(PayrollDbContext).Assembly.GetName().Name))
           .AddInterceptors(
               sp.GetRequiredService<AuditSaveChangesInterceptor>(),
               sp.GetRequiredService<TenantIdStampInterceptor>(),
               sp.GetRequiredService<NpgsqlTenantSessionInterceptor>()));
builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<PayrollDbContext>());

builder.Services.AddPayrollInfrastructure(builder.Configuration);
// PLAN-53 (P-7): applies the scheduled salary changes that are due (one replica per cycle, every agency under its own tenant).
builder.Services.AddHostedService<Payroll.Infrastructure.Services.PayProfileChangeActivationJob>();
builder.Services.AddServiceMassTransit(
    builder.Configuration,
    configureOutbox: x => x.AddPostgresOutbox<PayrollDbContext>(consumerOutbox: true),
    consumerAssembly: typeof(ContractActivatedConsumer).Assembly);
builder.Services.AddModuleAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddHrManagementAuthorizationPolicies();
builder.Services.AddHrServiceHealthChecks<PayrollDbContext>(builder.Configuration);

var app = builder.Build();
app.UseHrProductionMiddleware(app.Configuration);

// PLAN-53 (D-53-4): skipping the approval of scale and grade changes is a Development convenience only. Anywhere else the setting is
// ignored, and the service says so once at start.
{
    var masterData = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Payroll.Application.Options.PayrollMasterDataOptions>>().Value;
    if (Payroll.Application.Features.ReferenceChanges.PayrollMasterDataApproval.IsIgnored(masterData, app.Environment))
        app.Logger.LogWarning(Payroll.Application.Features.ReferenceChanges.PayrollMasterDataApproval.IgnoredWarning, app.Environment.EnvironmentName);
}

// PLAN-54 (D-PAY-12): the legal attestor's policy is a setting; a name that is unknown, or that names Payroll.LegalAttest itself, stops
// the service here rather than leaving attestation open to nobody or to a loop.
await HRManagement.ServiceDefaults.PayrollLegalAttestPolicy.ValidateAsync(app.Services);

// PLAN-54 (§4 rule 2): skipping the second person of a manual allowance, deduction or adjustment is a Development convenience only.
{
    var manualInputs = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Payroll.Application.Features.ManualInputs.PayrollManualInputOptions>>().Value;
    if (Payroll.Application.Features.ManualInputs.ManualInputApproval.IsIgnored(manualInputs, app.Environment))
        app.Logger.LogWarning(Payroll.Application.Features.ManualInputs.ManualInputApproval.IgnoredWarning, app.Environment.EnvironmentName);
}

await app.Services.TryMigrateAsync<PayrollDbContext>();
var seedProfile = SeedProfileResolver.Resolve(app.Configuration, app.Environment);
HRManagement.ServiceDefaults.TenantIdFlow.Set(HRManagement.SharedKernel.Tenancy.TenantDefaults.DefaultTenantId);
try
{
    if (seedProfile == SeedProfile.Demo)
    {
        await SeedPayrollDataAsync(app.Services);
        using var demoScope = app.Services.CreateScope();
        var demoDb = demoScope.ServiceProvider.GetRequiredService<PayrollDbContext>();
        await Payroll.Infrastructure.Data.DemoPayrollSeeder.SeedAsync(demoDb);
    }
    else if (seedProfile == SeedProfile.Acceptance)
    {
        using var acceptanceScope = app.Services.CreateScope();
        var acceptanceDb = acceptanceScope.ServiceProvider.GetRequiredService<PayrollDbContext>();
        await AcceptancePayrollSeeder.SeedAsync(acceptanceDb);
    }
}
finally
{
    HRManagement.ServiceDefaults.TenantIdFlow.Set(null);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseHrOpenApi();

app.UseStaticFiles();
app.UseRouting();
app.UseHrOutboxFlushGuard();
app.UseHrLocalization();
app.UseHrCorrelationId();
app.UseAuthentication();
app.UseHrTenantId();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=PayrollRuns}/{action=Index}/{id?}");

app.MapHrHealthChecks();
app.MapHub<NotificationHub>("/hubs/notifications");

app.MapHrOpenTelemetryEndpoints();

app.Run();

static async Task SeedPayrollDataAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();

    if (!await db.LegalDecisionRecords.AnyAsync())
    {
        db.LegalDecisionRecords.AddRange(
            Payroll.Domain.Services.LegalDecisionRegisterSeed.CreateDefaults());
        await db.SaveChangesAsync();
    }

    if (await db.PayrollConfigurations.AnyAsync()) return;

    var law50 = Payroll.Domain.Services.PayrollRegimeSeed.CreateLaw50Configuration(isActive: true);
    var law53 = Payroll.Domain.Services.PayrollRegimeSeed.CreateLaw53Configuration(isActive: false);
    var civil = Payroll.Domain.Services.PayrollRegimeSeed.CreateCivilServiceConfiguration(isActive: false);

    db.PayrollConfigurations.AddRange(law50, law53, civil);
    await db.SaveChangesAsync();

    db.OfficialHolidays.AddRange(
        Payroll.Domain.Services.PayrollRegimeSeed.CreateDefaultHolidays(law50.Id));
    db.OfficialHolidays.AddRange(
        Payroll.Domain.Services.PayrollRegimeSeed.CreateDefaultHolidays(law53.Id));
    db.OfficialHolidays.AddRange(
        Payroll.Domain.Services.PayrollRegimeSeed.CreateDefaultHolidays(civil.Id));

    await db.SaveChangesAsync();
}

/// <summary>The entry point as a public type, for the tests that host the service in process (PLAN-53).</summary>
public partial class PayrollWebProgram { }
