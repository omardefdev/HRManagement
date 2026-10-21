using Microsoft.EntityFrameworkCore;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Integration;
using HRManagement.ServiceDefaults.Localization;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.ServiceDefaults.Seeding;
using HR.Movements.Infrastructure.Data;
using MassTransit;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseHrManagementSerilog();
builder.AddHrProductionNetworking();
builder.AddHrDataProtection("HR.Movements.Web");
builder.AddHrOpenTelemetry();
builder.AddHrLocalization();

builder.Services.AddControllersWithViews()
    .AddHrModuleAuthorizeFilter(HrManagementPolicies.ModuleHrMovements);
builder.Services.AddHrOpenApi("HR.Movements API");

builder.Services.AddHttpContextAccessor();
builder.Services.AddTenantContext();
builder.Services.AddHrFileUrlProvider(builder.Configuration);
builder.Services.AddScoped<AuditSaveChangesInterceptor>();
builder.Services.AddSignalR();

var movementsConn = builder.Configuration.GetConnectionString("MovementsDb");
builder.Services.AddDbContext<MovementsDbContext>((sp, options) =>
    options.UseNpgsql(movementsConn, npgsql =>
        npgsql.MigrationsAssembly(typeof(MovementsDbContext).Assembly.GetName().Name))
           .AddInterceptors(
               sp.GetRequiredService<AuditSaveChangesInterceptor>(),
               sp.GetRequiredService<TenantIdStampInterceptor>(),
               sp.GetRequiredService<NpgsqlTenantSessionInterceptor>()));
builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<MovementsDbContext>());
builder.Services.AddHrPolicyEnforceClient(builder.Configuration);
builder.Services.AddHrCoreActiveRegimeClient(builder.Configuration);

builder.Services.Configure<HR.Movements.Infrastructure.Sagas.TransferSagaOptions>(
    builder.Configuration.GetSection(HR.Movements.Infrastructure.Sagas.TransferSagaOptions.SectionName));
builder.Services.Configure<HR.Movements.Infrastructure.Sagas.SecondmentSagaOptions>(
    builder.Configuration.GetSection(HR.Movements.Infrastructure.Sagas.SecondmentSagaOptions.SectionName));
builder.Services.AddServiceMassTransit(
    builder.Configuration,
    configureConsumers: x =>
    {
        x.AddConsumers(typeof(HR.Movements.Web.Consumers.OrgUnitChangedConsumer).Assembly);
        x.AddConsumers(typeof(HR.Movements.Infrastructure.Consumers.InternalPlacementCompletedConsumer).Assembly);
        x.AddSagaStateMachine<HR.Movements.Infrastructure.Sagas.TransferStateMachine, HR.Movements.Infrastructure.Sagas.TransferSagaState>()
            .EntityFrameworkRepository(r =>
            {
                r.ConcurrencyMode = ConcurrencyMode.Pessimistic;
                r.ExistingDbContext<MovementsDbContext>();
                r.UsePostgres();
            });
        x.AddSagaStateMachine<HR.Movements.Infrastructure.Sagas.SecondmentStateMachine, HR.Movements.Infrastructure.Sagas.SecondmentSagaState>()
            .EntityFrameworkRepository(r =>
            {
                r.ConcurrencyMode = ConcurrencyMode.Pessimistic;
                r.ExistingDbContext<MovementsDbContext>();
                r.UsePostgres();
            });
    },
    configureOutbox: x => x.AddPostgresOutbox<MovementsDbContext>(),
    consumerAssembly: typeof(HR.Movements.Infrastructure.Consumers.TransferExecutedConsumer).Assembly);
builder.Services.AddModuleAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddHrManagementAuthorizationPolicies();
builder.Services.AddOrgScopeAuthorization();
builder.Services.AddHrServiceHealthChecks<MovementsDbContext>(builder.Configuration);

var app = builder.Build();
app.UseHrProductionMiddleware(app.Configuration);

await app.Services.TryMigrateAsync<MovementsDbContext>();
var seedProfile = SeedProfileResolver.Resolve(app.Configuration, app.Environment);

if (seedProfile != SeedProfile.None)
{
    try
    {
        using var demoScope = app.Services.CreateScope();
        var db = demoScope.ServiceProvider.GetRequiredService<MovementsDbContext>();
        if (seedProfile == SeedProfile.Acceptance)
            await AcceptanceMovementsSeeder.SeedAsync(db);
        else
            await DemoMovementsSeeder.SeedAsync(db);
    }
    catch (Exception ex) when (seedProfile != SeedProfile.Acceptance)
    {
        app.Logger.LogWarning(ex, "HR.Movements demo seed skipped.");
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
    pattern: "{controller=TransferRequests}/{action=Index}/{id?}");

app.MapHrHealthChecks();
app.MapHub<NotificationHub>("/hubs/notifications");

app.MapHrOpenTelemetryEndpoints();

app.Run();
