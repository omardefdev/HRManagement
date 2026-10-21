using Microsoft.EntityFrameworkCore;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Localization;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.ServiceDefaults.Seeding;
using HR.Training.Infrastructure.Consumers;
using HR.Training.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseHrManagementSerilog();
builder.AddHrProductionNetworking();
builder.AddHrDataProtection("HR.Training.Web");
builder.AddHrOpenTelemetry();
builder.AddHrLocalization();

builder.Services.AddControllersWithViews()
    .AddHrModuleAuthorizeFilter(HrManagementPolicies.ModuleHrTraining);
builder.Services.AddHrOpenApi("HR.Training API");

builder.Services.AddHttpContextAccessor();
builder.Services.AddTenantContext();
builder.Services.AddHrFileUrlProvider(builder.Configuration);
builder.Services.AddScoped<AuditSaveChangesInterceptor>();
builder.Services.AddSignalR();

var trainingConn = builder.Configuration.GetConnectionString("TrainingDb");
builder.Services.AddDbContext<TrainingDbContext>((sp, options) =>
    options.UseNpgsql(trainingConn, npgsql =>
        npgsql.MigrationsAssembly(typeof(TrainingDbContext).Assembly.GetName().Name))
           .AddInterceptors(
               sp.GetRequiredService<AuditSaveChangesInterceptor>(),
               sp.GetRequiredService<TenantIdStampInterceptor>(),
               sp.GetRequiredService<NpgsqlTenantSessionInterceptor>()));

builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<TrainingDbContext>());
builder.Services.AddHrPolicyEnforceClient(builder.Configuration);
builder.Services.AddServiceMassTransit(builder.Configuration,
    configureOutbox: x => x.AddPostgresOutbox<TrainingDbContext>(),
    consumerAssembly: typeof(EmployeeCreatedConsumer).Assembly);
builder.Services.AddHrFileReferenceClient(builder.Configuration);
builder.Services.AddModuleAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddHrManagementAuthorizationPolicies();
builder.Services.AddHrServiceHealthChecks<TrainingDbContext>(builder.Configuration);

var app = builder.Build();
app.UseHrProductionMiddleware(app.Configuration);

await app.Services.TryMigrateAsync<TrainingDbContext>();
var seedProfile = SeedProfileResolver.Resolve(app.Configuration, app.Environment);
try
{
    if (seedProfile != SeedProfile.None)
    {
        using var scope = app.Services.CreateScope();
        var trainingDb = scope.ServiceProvider.GetRequiredService<TrainingDbContext>();
        if (seedProfile == SeedProfile.Acceptance)
            await AcceptanceTrainingSeeder.SeedAsync(trainingDb);
        else
        {
            await ELearningSeeder.SeedAsync(trainingDb);
            await DemoTrainingSeeder.SeedAsync(trainingDb);
        }
    }
}
catch (Exception ex) when (seedProfile != SeedProfile.Acceptance)
{
    app.Logger.LogWarning(ex, "HR.Training e-learning seed skipped.");
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
    pattern: "{controller=TrainingEnrollments}/{action=Index}/{id?}");

app.MapHrHealthChecks();
app.MapHub<NotificationHub>("/hubs/notifications");

app.MapHrOpenTelemetryEndpoints();

app.Run();
