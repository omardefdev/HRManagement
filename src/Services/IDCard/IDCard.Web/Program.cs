using MassTransit;
using Microsoft.EntityFrameworkCore;
using HRManagement.ServiceDefaults;
using IDCard.Infrastructure.Consumers;
using IDCard.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseHrManagementSerilog();
builder.AddHrProductionNetworking();
builder.AddHrDataProtection("IDCard.Web");
builder.AddHrOpenTelemetry();

builder.Services.AddControllersWithViews()
    .AddHrModuleAuthorizeFilter(HrManagementPolicies.ModuleIdCard);
builder.Services.AddHrOpenApi("IDCard API");

builder.Services.AddHttpContextAccessor();
builder.Services.AddTenantContext();
builder.Services.AddHrFileUrlProvider(builder.Configuration);
builder.Services.AddScoped<AuditSaveChangesInterceptor>();
builder.Services.AddSignalR();

var idCardConn = builder.Configuration.GetConnectionString("IDCardDb");
builder.Services.AddDbContext<IDCardDbContext>((sp, options) =>
    options.UseNpgsql(idCardConn, npgsql =>
        npgsql.MigrationsAssembly(typeof(IDCardDbContext).Assembly.GetName().Name))
           .AddInterceptors(
               sp.GetRequiredService<AuditSaveChangesInterceptor>(),
               sp.GetRequiredService<TenantIdStampInterceptor>(),
               sp.GetRequiredService<NpgsqlTenantSessionInterceptor>()));

builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<IDCardDbContext>());
builder.Services.AddServiceMassTransit(
    builder.Configuration,
    configureOutbox: x => x.AddPostgresOutbox<IDCardDbContext>(),
    consumerAssembly: typeof(EmployeeCreatedConsumer).Assembly);
builder.Services.AddHrFileReferenceClient(builder.Configuration);
builder.Services.AddModuleAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddHrManagementAuthorizationPolicies();
builder.Services.AddHrServiceHealthChecks<IDCardDbContext>(builder.Configuration);

var app = builder.Build();
app.UseHrProductionMiddleware(app.Configuration);

await app.Services.TryMigrateAsync<IDCardDbContext>();

if (app.Environment.IsDevelopment())
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDCardDbContext>();
        await DemoIDCardSeeder.SeedAsync(db);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "IDCard demo seed skipped.");
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
    pattern: "{controller=CardRequests}/{action=Index}/{id?}");

app.MapHrHealthChecks();
app.MapHub<NotificationHub>("/hubs/notifications");

app.MapHrOpenTelemetryEndpoints();

app.Run();
