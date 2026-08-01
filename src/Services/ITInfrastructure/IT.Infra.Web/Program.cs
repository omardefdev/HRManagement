using Microsoft.EntityFrameworkCore;
using HRManagement.ServiceDefaults;
using IT.Infra.Infrastructure.Consumers;
using IT.Infra.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseHrManagementSerilog();
builder.AddHrProductionNetworking();
builder.AddHrDataProtection("IT.Infra.Web");
builder.AddHrOpenTelemetry();

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

builder.Services.AddServiceIdentityClient();
var authDirectoryBase = builder.Configuration["Services:AuthDirectory:BaseUrl"];
if (!string.IsNullOrWhiteSpace(authDirectoryBase))
{
    builder.Services.AddServiceAuthenticatedClient(
        IT.Infra.Infrastructure.Integration.HttpDirectoryEntrySyncClient.HttpClientName,
        new Uri(authDirectoryBase.TrimEnd('/') + "/"));
}
else
{
    builder.Services.AddHttpClient(IT.Infra.Infrastructure.Integration.HttpDirectoryEntrySyncClient.HttpClientName);
}

builder.Services.AddScoped<HRManagement.SharedKernel.Interfaces.IDirectoryEntrySyncService, IT.Infra.Infrastructure.Integration.HttpDirectoryEntrySyncClient>();

builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<ITInfraDbContext>());
builder.Services.AddServiceMassTransit(
    builder.Configuration,
    consumerAssembly: typeof(EmployeeCreatedConsumer).Assembly);
builder.Services.AddModuleAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddHrManagementAuthorizationPolicies();
builder.Services.AddHrServiceHealthChecks<ITInfraDbContext>(builder.Configuration);

var app = builder.Build();
app.UseHrProductionMiddleware(app.Configuration);

await app.Services.TryMigrateAsync<ITInfraDbContext>();
try
{
    if (app.Environment.IsDevelopment())
    {
        using var scope = app.Services.CreateScope();
        var itDb = scope.ServiceProvider.GetRequiredService<ITInfraDbContext>();
        await DemoITSeeder.SeedAsync(itDb);
    }
}
catch (Exception ex)
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

