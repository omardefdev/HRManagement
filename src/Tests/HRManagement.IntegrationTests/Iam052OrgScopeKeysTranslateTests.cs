using Admin.Logistics.Infrastructure.Data;
using Admin.Logistics.Infrastructure.OrgScope;
using HR.Contracts.Infrastructure.Data;
using HR.Contracts.Infrastructure.OrgScope;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.OrgScope;
using HR.Movements.Infrastructure.Data;
using HR.Movements.Infrastructure.OrgScope;
using HRManagement.ServiceDefaults;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-52 (§6, P-11, plan Risks row 1): every entry of every service's scope registry runs against the service's real context, on SQLite
/// and on PostgreSQL (Testcontainers): the id lookup the filter makes, the units a record has, and the list filter of
/// <c>WhereInOrgScope</c>. A key that EF cannot translate, or that names a column the schema lacks, fails here and not in production.
/// </summary>
public static class Iam052OrgScopeKeysTranslation
{
    public sealed record ServiceRegistry(string Service, Type ContextType, Action<OrgScopeKeyRegistry> Configure);

    public static readonly IReadOnlyList<ServiceRegistry> Services =
    [
        new(HrCoreOrgScopeKeys.Service, typeof(HRCoreDbContext), HrCoreOrgScopeKeys.Configure),
        new(ContractsOrgScopeKeys.Service, typeof(ContractsDbContext), ContractsOrgScopeKeys.Configure),
        new(AdminLogisticsOrgScopeKeys.Service, typeof(AdminLogisticsDbContext), AdminLogisticsOrgScopeKeys.Configure),
        new(MovementsOrgScopeKeys.Service, typeof(MovementsDbContext), MovementsOrgScopeKeys.Configure),
    ];

    public static OrgScopeKeyRegistry Registry(ServiceRegistry service)
    {
        var registry = new OrgScopeKeyRegistry(service.Service);
        service.Configure(registry);
        return registry;
    }

    /// <summary>Runs every key of <paramref name="registry"/> on <paramref name="db"/>; returns the SQL of each list filter.</summary>
    public static async Task<IReadOnlyList<string>> RunAllAsync(DbContext db, OrgScopeKeyRegistry registry)
    {
        var allowed = new HashSet<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var sql = new List<string>();
        foreach (var key in registry.Keys.Values)
        {
            Assert.True(key.ContextType.IsInstanceOfType(db), $"{registry.Service}: {key.EntityType.Name} reads {key.ContextType.Name}");
            Assert.False(await key.IsInScopeAsync(db, Guid.NewGuid(), allowed), $"{key.EntityType.Name}: a missing record is never in scope");
            Assert.Empty(await key.UnitsOfAsync(db, Guid.NewGuid()));
            var list = typeof(Iam052OrgScopeKeysTranslation).GetMethod(nameof(ListAsync), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .MakeGenericMethod(key.EntityType);
            sql.Add($"-- {registry.Service} {key.EntityType.Name}\n" + await (Task<string>)list.Invoke(null, [db, registry, allowed])!);
        }

        return sql;
    }

    private static async Task<string> ListAsync<T>(DbContext db, OrgScopeKeyRegistry registry, HashSet<Guid> allowed)
        where T : class
    {
        var query = db.Set<T>().AsNoTracking().WhereInOrgScope(registry, OrgAccessScope.Restricted(allowed), db);
        await query.Take(5).ToListAsync();
        return query.ToQueryString();
    }

    public static DbContext Create(Type contextType, DbContextOptions options) =>
        (DbContext)Activator.CreateInstance(contextType, options, null)!;

    public static DbContextOptions Options(Type contextType, Action<DbContextOptionsBuilder> configure)
    {
        var builder = (DbContextOptionsBuilder)Activator.CreateInstance(typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType))!;
        configure(builder);
        return builder.Options;
    }
}

public sealed class Iam052OrgScopeKeysTranslateSqliteTests
{
    public static TheoryData<string> Services() => new(Iam052OrgScopeKeysTranslation.Services.Select(s => s.Service));

    [Theory]
    [MemberData(nameof(Services))]
    public async Task Every_key_translates_on_SQLite(string service)
    {
        var entry = Iam052OrgScopeKeysTranslation.Services.Single(s => s.Service == service);
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = Iam052OrgScopeKeysTranslation.Options(entry.ContextType, b => b.UseSqlite(connection));
        await using var db = Iam052OrgScopeKeysTranslation.Create(entry.ContextType, options);
        await db.Database.EnsureCreatedAsync();
        var registry = Iam052OrgScopeKeysTranslation.Registry(entry);
        var sql = await Iam052OrgScopeKeysTranslation.RunAllAsync(db, registry);
        Assert.Equal(registry.Keys.Count, sql.Count);
    }
}

[Collection("PostgreSql")]
public sealed class Iam052OrgScopeKeysTranslatePostgreSqlTests(PostgreSqlFixture fixture)
{
    public static TheoryData<string> Services() => new(Iam052OrgScopeKeysTranslation.Services.Select(s => s.Service));

    [Trait("Category", "Testcontainers")]
    [SkippableTheory]
    [MemberData(nameof(Services))]
    public async Task Every_key_translates_on_PostgreSQL(string service)
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var entry = Iam052OrgScopeKeysTranslation.Services.Single(s => s.Service == service);
        var cs = await fixture.CreateDatabaseAsync(TestDatabaseNames.Unique($"iam052_tr_{service.Replace('-', '_')}_", 50));
        var options = Iam052OrgScopeKeysTranslation.Options(entry.ContextType,
            b => b.UseNpgsql(cs, npgsql => npgsql.MigrationsAssembly(entry.ContextType.Assembly.GetName().Name)));
        await using var db = Iam052OrgScopeKeysTranslation.Create(entry.ContextType, options);
        await db.Database.MigrateAsync();
        var registry = Iam052OrgScopeKeysTranslation.Registry(entry);
        var sql = await Iam052OrgScopeKeysTranslation.RunAllAsync(db, registry);
        Assert.Equal(registry.Keys.Count, sql.Count);
    }
}
