using MassTransit;
using Microsoft.EntityFrameworkCore;
using HRManagement.ServiceDefaults;
using Auth.Directory.Infrastructure.Consumers;
using Auth.Directory.Infrastructure.Data;
using Auth.Directory.Infrastructure.Keycloak;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseHrManagementSerilog();
builder.AddHrProductionNetworking();
builder.AddHrDataProtection("Auth.Directory.Web");
builder.AddHrOpenTelemetry();

builder.Services.AddControllersWithViews()
    .AddHrModuleAuthorizeFilter(HrManagementPolicies.ModuleAuthDirectory);
builder.Services.AddHrOpenApi("Auth.Directory API");

builder.Services.AddHttpContextAccessor();
builder.Services.AddTenantContext();
builder.Services.AddHrFileUrlProvider(builder.Configuration);
builder.Services.AddScoped<AuditSaveChangesInterceptor>();
builder.Services.AddSignalR();

var authConn = builder.Configuration.GetConnectionString("AuthDirectoryDb");
builder.Services.AddDbContext<AuthDirectoryDbContext>((sp, options) =>
    options.UseNpgsql(authConn, npgsql =>
        npgsql.MigrationsAssembly(typeof(AuthDirectoryDbContext).Assembly.GetName().Name))
           .AddInterceptors(
               sp.GetRequiredService<AuditSaveChangesInterceptor>(),
               sp.GetRequiredService<TenantIdStampInterceptor>(),
               sp.GetRequiredService<NpgsqlTenantSessionInterceptor>()));

builder.Services.AddHttpClient<IKeycloakUserProvisioningService, KeycloakUserProvisioningService>();
builder.Services.AddScoped<HRManagement.SharedKernel.Interfaces.IDirectoryEntrySyncService, Auth.Directory.Infrastructure.Services.DirectoryEntrySyncService>();
builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<AuthDirectoryDbContext>());
builder.Services.AddServiceMassTransit(
    builder.Configuration,
    consumerAssembly: typeof(EmployeeCreatedConsumer).Assembly);
builder.Services.AddModuleAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddHrManagementAuthorizationPolicies();
builder.Services.AddHrServiceHealthChecks<AuthDirectoryDbContext>(builder.Configuration);

var app = builder.Build();
app.UseHrProductionMiddleware(app.Configuration);

await app.Services.TryMigrateAsync<AuthDirectoryDbContext>();

if (app.Environment.IsDevelopment())
{
    using var demoScope = app.Services.CreateScope();
    TenantIdFlow.Set(HRManagement.SharedKernel.Tenancy.TenantDefaults.DefaultTenantId);
    try
    {
        await DemoAuthSeeder.SeedAsync(
            demoScope.ServiceProvider.GetRequiredService<AuthDirectoryDbContext>());
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
    pattern: "{controller=AuthUsers}/{action=Index}/{id?}");

app.MapHrHealthChecks();
app.MapHub<NotificationHub>("/hubs/notifications");

app.MapHrOpenTelemetryEndpoints();

app.Run();

