using MassTransit;
using HR.Contracts.Application;
using HR.Contracts.Infrastructure;
using Microsoft.EntityFrameworkCore;
using HRManagement.ServiceDefaults;
using HR.Contracts.Infrastructure.Consumers;
using HR.Contracts.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseHrManagementSerilog();
builder.AddHrProductionNetworking();
builder.AddHrDataProtection("HR.Contracts.Web");
builder.AddHrOpenTelemetry();

builder.Services.AddControllersWithViews()
    .AddHrModuleAuthorizeFilter(HrManagementPolicies.ModuleHrContracts);
builder.Services.AddHrOpenApi("HR.Contracts API");

builder.Services.AddHttpContextAccessor();
builder.Services.AddTenantContext();
builder.Services.AddHrFileUrlProvider(builder.Configuration);
builder.Services.AddScoped<AuditSaveChangesInterceptor>();
builder.Services.AddSignalR();

var contractsConn = builder.Configuration.GetConnectionString("ContractsDb");
builder.Services.AddDbContext<ContractsDbContext>((sp, options) =>
    options.UseNpgsql(contractsConn, npgsql =>
        npgsql.MigrationsAssembly(typeof(ContractsDbContext).Assembly.GetName().Name))
           .AddInterceptors(
               sp.GetRequiredService<AuditSaveChangesInterceptor>(),
               sp.GetRequiredService<TenantIdStampInterceptor>(),
               sp.GetRequiredService<NpgsqlTenantSessionInterceptor>()));
builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<ContractsDbContext>());
builder.Services.AddHrContractsApplication();
builder.Services.AddHrContractsInfrastructure(builder.Configuration);
builder.Services.AddHrFileReferenceClient(builder.Configuration);

builder.Services.Configure<HR.Contracts.Infrastructure.Sagas.SeparationSagaOptions>(
    builder.Configuration.GetSection(HR.Contracts.Infrastructure.Sagas.SeparationSagaOptions.SectionName));
builder.Services.Configure<HR.Contracts.Infrastructure.Sagas.HireSagaOptions>(
    builder.Configuration.GetSection(HR.Contracts.Infrastructure.Sagas.HireSagaOptions.SectionName));
builder.Services.AddServiceMassTransit(
    builder.Configuration,
    configureConsumers: x =>
    {
        x.AddSagaStateMachine<HR.Contracts.Infrastructure.Sagas.SeparationStateMachine, HR.Contracts.Infrastructure.Sagas.SeparationSagaState>()
            .EntityFrameworkRepository(r =>
            {
                r.ConcurrencyMode = ConcurrencyMode.Pessimistic;
                r.ExistingDbContext<ContractsDbContext>();
                r.UsePostgres();
            });
        x.AddSagaStateMachine<HR.Contracts.Infrastructure.Sagas.HireCaseStateMachine, HR.Contracts.Infrastructure.Sagas.HireCaseSagaState>()
            .EntityFrameworkRepository(r =>
            {
                r.ConcurrencyMode = ConcurrencyMode.Pessimistic;
                r.ExistingDbContext<ContractsDbContext>();
                r.UsePostgres();
            });
    },
    configureOutbox: x => x.AddPostgresOutbox<ContractsDbContext>(),
    consumerAssembly: typeof(EmployeeCreatedConsumer).Assembly);

builder.Services.AddModuleAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddHrManagementAuthorizationPolicies();
builder.Services.AddHrServiceHealthChecks<ContractsDbContext>(builder.Configuration);

var app = builder.Build();
app.UseHrProductionMiddleware(app.Configuration);

await app.Services.TryMigrateAsync<ContractsDbContext>();
try
{
    using var scope = app.Services.CreateScope();
    HRManagement.ServiceDefaults.TenantIdFlow.Set(HRManagement.SharedKernel.Tenancy.TenantDefaults.DefaultTenantId);
    try
    {
        var contractsDb = scope.ServiceProvider.GetRequiredService<ContractsDbContext>();
        await ContractsDataSeeder.SeedAsync(contractsDb);
        if (app.Environment.IsDevelopment())
            await DemoContractsSeeder.SeedAsync(contractsDb);
    }
    finally
    {
        HRManagement.ServiceDefaults.TenantIdFlow.Set(null);
    }
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "HR.Contracts seed skipped.");
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
    pattern: "{controller=Contracts}/{action=Index}/{id?}");

app.MapHrHealthChecks();
app.MapHub<NotificationHub>("/hubs/notifications");

app.MapHrOpenTelemetryEndpoints();

app.Run();

