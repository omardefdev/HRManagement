using System.Security.Claims;
using HR.Core.Domain.Entities.Policy;
using HR.Core.Domain.Services.Policy;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Data.Interceptors;
using HR.Core.Infrastructure.Services.Policy;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace HRManagement.IntegrationTests;

/// <summary>PLAN-47 on PostgreSQL: a migrated HR.Core database, its forced row security, and the app role's context.</summary>
internal static class Pol047Pg
{
    public static readonly Guid National = TenantDefaults.DefaultTenantId;
    public static readonly Guid TenantA = Guid.Parse("a0000001-0000-4000-8000-000000000001");
    public const string ValidWhen = """{"all":[{"field":"category","operator":"Equals","value":"x"}]}""";
    public const string ValidThen = """[{"effect":"RequireEvidence"}]""";

    public static async Task<string> MigratedAsync(PostgreSqlFixture postgres, string name)
    {
        var admin = await postgres.CreateDatabaseAsync($"pol047_{name}_{Guid.NewGuid():N}"[..40]);
        await using var db = AdminContext(admin);
        await db.Database.MigrateAsync();
        return admin;
    }

    /// <summary>TEN-002C's hardening (ENABLE + FORCE on every TenantId table), then a NOSUPERUSER NOBYPASSRLS role.</summary>
    public static async Task<string> ForcedAppAsync(PostgreSqlFixture postgres, string admin)
    {
        var script = await global::System.IO.File.ReadAllTextAsync(Path.Combine(HRManagement.TestSupport.RepoPaths.Root, "scripts", "sql", "ten002c-harden-tenant-rls.sql"));
        await ExecAsync(admin, script.Replace(@"\set ON_ERROR_STOP on", string.Empty, StringComparison.Ordinal));
        return await postgres.CreateAppConnectionAsync(new NpgsqlConnectionStringBuilder(admin).Database!);
    }

    /// <summary>The superuser (sees every tenant), with the immutability interceptor: what a seeder writes.</summary>
    public static HRCoreDbContext AdminContext(string cs) =>
        new(new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseNpgsql(cs, n => n.MigrationsAssembly(typeof(HRCoreDbContext).Assembly.GetName().Name))
            .AddInterceptors(new PublishedPolicyImmutabilityInterceptor())
            .Options);

    /// <summary>The app role inside one tenant, with the interceptors HR.Core registers (tenant stamp, session tenant, immutability).</summary>
    public static ServiceProvider AppProvider(string app, IConfiguration? configuration = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration ?? new ConfigurationBuilder().Build());
        services.AddHttpContextAccessor();
        services.AddTenantContext();
        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddSingleton<PublishedPolicyImmutabilityInterceptor>();
        services.AddDbContext<HRCoreDbContext>((sp, o) => o
            .UseNpgsql(app, n => n.MigrationsAssembly(typeof(HRCoreDbContext).Assembly.GetName().Name))
            .AddInterceptors(
                sp.GetRequiredService<AuditSaveChangesInterceptor>(),
                sp.GetRequiredService<PublishedPolicyImmutabilityInterceptor>(),
                sp.GetRequiredService<TenantIdStampInterceptor>(),
                sp.GetRequiredService<NpgsqlTenantSessionInterceptor>()));
        return services.BuildServiceProvider();
    }

    public static async Task<T> AsTenantAsync<T>(ServiceProvider provider, Guid tenant, Func<HRCoreDbContext, Task<T>> work)
    {
        var previous = TenantIdFlow.Get();
        TenantIdFlow.Set(tenant);
        try
        {
            await using var scope = provider.CreateAsyncScope();
            return await work(scope.ServiceProvider.GetRequiredService<HRCoreDbContext>());
        }
        finally
        {
            TenantIdFlow.Set(previous);
        }
    }

    public static async Task ExecAsync(string cs, string sql, Guid? tenant = null)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        if (tenant is Guid t)
        {
            await using var set = new NpgsqlCommand("SELECT set_config('app.current_tenant', @t, false)", conn);
            set.Parameters.AddWithValue("t", t.ToString());
            await set.ExecuteScalarAsync();
        }

        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<T> ScalarAsync<T>(string cs, string sql, Guid? tenant = null)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        if (tenant is Guid t)
        {
            await using var set = new NpgsqlCommand("SELECT set_config('app.current_tenant', @t, false)", conn);
            set.Parameters.AddWithValue("t", t.ToString());
            await set.ExecuteScalarAsync();
        }

        await using var cmd = new NpgsqlCommand(sql, conn);
        return (T)Convert.ChangeType((await cmd.ExecuteScalarAsync())!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>A template with its facts schema, a published v1 (one rule) and a draft v2 (one rule), seeded the PLAN-47 way.</summary>
    public static async Task<(Guid Template, Guid Published, Guid Draft, Guid PublishedRule, Guid DraftRule)> SeedTemplateAsync(
        HRCoreDbContext db, Guid tenant, PolicyTemplateScope scope, string domain)
    {
        if (!await db.PolicyFactsSchemas.IgnoreQueryFilters().AnyAsync(s => s.Domain == domain))
        {
            db.PolicyFactsSchemas.Add(new PolicyFactsSchemaEntity
            {
                TenantId = National,
                Domain = domain,
                SubjectType = "Asset",
                FieldsJson = """[{"Name":"category","Type":"string"},{"Name":"action","Type":"string"}]"""
            });
        }

        var template = new PolicyTemplate { TenantId = tenant, Domain = domain, Name = "pol047", SubjectType = "Asset", Scope = scope };
        db.PolicyTemplates.Add(template);
        var publishedRule = new PolicyRule { TenantId = tenant, RuleKey = "R-PUB", Name = "pub", Priority = 10, WhenJson = ValidWhen, ThenJson = ValidThen };
        var v1 = await PolicySeedVersionWriter.AddAsync(db, new PolicyTemplateVersion
        {
            PolicyTemplate = template,
            TenantId = tenant,
            VersionNumber = 1,
            EffectiveFrom = DateTime.UtcNow.AddDays(-1)
        }, [publishedRule], PolicyVersionStatus.Published, new PolicyPublication { TenantId = tenant, EvidenceRef = "seed" });
        var draftRule = new PolicyRule { TenantId = tenant, RuleKey = "R-DRAFT", Name = "draft", Priority = 10, WhenJson = ValidWhen, ThenJson = ValidThen };
        var v2 = await PolicySeedVersionWriter.AddAsync(db, new PolicyTemplateVersion
        {
            PolicyTemplate = template,
            TenantId = tenant,
            VersionNumber = 2,
            CreatedBy = Guid.NewGuid()
        }, [draftRule], PolicyVersionStatus.Draft, null);
        return (template.Id, v1.Id, v2.Id, publishedRule.Id, draftRule.Id);
    }

    public static IAuthorizationService Authorization()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddAuthorization(o =>
            o.AddPolicy(HrManagementPolicies.PolicyApprover, p => p.AddRequirements(new PolicyEngineRoleRequirement(PolicyEngineRoleKind.Approver))));
        services.AddSingleton<IAuthorizationHandler, PolicyEngineRoleHandler>();
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    public static PolicyActor Approver(Guid id) =>
        new(id, new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", id.ToString()), new Claim(ClaimTypes.Role, HrManagementRoles.HrManagementApprover)], "test")));

    public static PolicyPublishService PublishService(HRCoreDbContext db, IPolicyVersionPublisher publisher) =>
        new(db, new PolicyRuleValidationService(db, AllowAllPolicyDomainCapabilities.Instance), publisher, authorization: Authorization());
}
