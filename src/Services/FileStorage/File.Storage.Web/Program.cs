using File.Storage.Infrastructure.Data;
using File.Storage.Infrastructure.Jobs;
using File.Storage.Infrastructure.Services;
using HRManagement.FileStorage;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Files;
using HRManagement.ServiceDefaults.Seeding;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseHrManagementSerilog();
builder.AddHrProductionNetworking();
// PLAN-36: MinIO is an optional compose profile, so its keys are checked here instead of by compose.
builder.Services.Configure<ProductionConfigurationGuardOptions>(options => options.ObjectStorageCredentialsRequired = true);
builder.AddHrDataProtection("File.Storage.Web");
builder.AddHrOpenTelemetry();

builder.Services.AddControllers();
builder.Services.AddHrOpenApi("File Storage API");
builder.Services.AddHttpContextAccessor();
builder.Services.AddTenantContext();
builder.Services.AddScoped<AuditSaveChangesInterceptor>();

var conn = builder.Configuration.GetConnectionString("FileStorageDb");
builder.Services.AddDbContext<FileStorageDbContext>((sp, options) =>
    options.UseHrDatabaseProvider(
            conn,
            typeof(FileStorageDbContext).Assembly.GetName().Name!)
        .AddInterceptors(
               sp.GetRequiredService<AuditSaveChangesInterceptor>(),
               sp.GetRequiredService<TenantIdStampInterceptor>(),
               sp.GetRequiredService<NpgsqlTenantSessionInterceptor>())
        .ConfigureWarnings(w => w.Log(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));
builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<FileStorageDbContext>());

builder.Services.AddHrFileStorage(builder.Configuration);
builder.Services.AddScoped<FilePlatformService>();
// SEC-003: object-level file authorization wired into runtime.
builder.Services.AddSingleton<IFileAccessPolicy, FileAccessPolicy>();
builder.Services.AddScoped<FileAccessGrantService>();
// PLAN-30 (FS-030): HR.Core's service upload of candidate documents.
builder.Services.AddScoped<FileServiceUploadService>();
builder.Services.AddHostedService<FileGarbageCollectorJob>();
builder.Services.AddHostedService<FileScanProcessorJob>();
builder.Services.AddServiceMassTransit(builder.Configuration);
builder.Services.AddModuleAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddHrManagementAuthorizationPolicies();
builder.Services.AddHrServiceHealthChecks<FileStorageDbContext>(builder.Configuration);

var app = builder.Build();
app.UseHrProductionMiddleware(app.Configuration);

await app.Services.TryMigrateAsync<FileStorageDbContext>();
var seedProfile = SeedProfileResolver.Resolve(app.Configuration, app.Environment);
if (seedProfile == SeedProfile.Acceptance)
{
    using var seedScope = app.Services.CreateScope();
    await AcceptanceFileStorageSeeder.SeedAsync(
        seedScope.ServiceProvider.GetRequiredService<FileStorageDbContext>(),
        seedScope.ServiceProvider.GetRequiredService<IFileStorageService>());
}

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Home/Error");

app.UseHrOpenApi();
app.UseRouting();
app.UseHrCorrelationId();
app.UseAuthentication();
app.UseHrTenantId();
app.UseAuthorization();

app.MapControllers();
app.MapHrHealthChecks();
app.MapHrOpenTelemetryEndpoints();

app.Run();

public partial class FileStorageWebProgram;
