using HRManagement.SharedKernel.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Application.Options;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using Payroll.Infrastructure.Data;

namespace HRManagement.IntegrationTests;

internal static class PayrollPostgresTestSupport
{
    internal sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId => tenantId;
        public bool IsPlatformOperator => false;
        public Guid RequireTenantId() => tenantId;
    }

    public static async Task<(ServiceProvider Root, AsyncServiceScope Scope, PayrollDbContext Db, IMediator Mediator)> CreateScopeAsync(
        PostgreSqlFixture fixture,
        Action<PayrollBatchOptions>? configure = null,
        Guid? tenantId = null)
    {
        var dbName = TestDatabaseNames.Unique("payroll_");
        var cs = await fixture.CreateDatabaseAsync(dbName);
        var tenant = new FixedTenant(tenantId ?? TenantDefaults.DefaultTenantId);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenant);
        services.AddDbContext<PayrollDbContext>(o =>
            o.UseNpgsql(cs, n => n.MigrationsAssembly(typeof(PayrollDbContext).Assembly.GetName().Name)));
        PayrollIntegrationTestSetup.AddPayrollConsumerServices(services);
        if (configure is not null)
            services.PostConfigure(configure);

        var root = services.BuildServiceProvider();
        var scope = root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
        await db.Database.MigrateAsync();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        return (root, scope, db, mediator);
    }

    public static async Task<PayrollConfiguration> SeedPublishedConfigAsync(PayrollDbContext db)
    {
        var config = PayrollRegimeSeed.CreateLaw50Configuration(isActive: true);
        config.ContentHash = PayrollConfigurationHashService.ComputeContentHash(config);
        config.EffectiveFrom = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        db.PayrollConfigurations.Add(config);
        await db.SaveChangesAsync();
        return config;
    }

    public static async Task SeedEmployeesAsync(PayrollDbContext db, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var id = Guid.Parse($"b0000001-0000-4000-8000-{i + 1:D12}");
            if (await db.EmployeePayProfiles.AnyAsync(p => p.EmployeeId == id))
                continue;

            db.EmployeePayProfiles.Add(TestDataFactory.CreateActivePayProfile(id, 100_000m + (i * 5_000m)));
        }

        await db.SaveChangesAsync();
    }

    public static async Task CancelRunForDualTestAsync(PayrollDbContext db, Guid payrollRunId)
    {
        db.ChangeTracker.Clear();
        var run = await db.PayrollRuns.FirstAsync(r => r.Id == payrollRunId);
        run.MarkCancelled();
        await db.SaveChangesAsync();
    }
}
