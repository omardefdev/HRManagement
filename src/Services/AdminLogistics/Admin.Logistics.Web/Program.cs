using Microsoft.EntityFrameworkCore;
using HRManagement.ServiceDefaults;
using Admin.Logistics.Infrastructure.Consumers;
using Admin.Logistics.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseHrManagementSerilog();
builder.AddHrProductionNetworking();
builder.AddHrDataProtection("Admin.Logistics.Web");
builder.AddHrOpenTelemetry();

builder.Services.AddControllersWithViews()
    .AddHrModuleAuthorizeFilter(HrManagementPolicies.ModuleAdminLogistics);
builder.Services.AddHrOpenApi("Admin.Logistics API");

builder.Services.AddHttpContextAccessor();
builder.Services.AddTenantContext();
builder.Services.AddHrFileUrlProvider(builder.Configuration);
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
try
{
    if (app.Environment.IsDevelopment())
    {
        using var scope = app.Services.CreateScope();
        var logisticsDb = scope.ServiceProvider.GetRequiredService<AdminLogisticsDbContext>();
        await DemoLogisticsSeeder.SeedAsync(logisticsDb);
    }
}
catch (Exception ex)
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

