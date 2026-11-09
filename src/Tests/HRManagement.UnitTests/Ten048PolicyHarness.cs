using HR.Core.Domain.Entities.Policy;
using HR.Core.Domain.Services.Policy;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Data.Interceptors;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Workflow;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.ProdPolicy;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-48 (§4): an SQLite HR.Core database with the national seed (workflow packs and policy templates in the national tenant),
/// and sessions per tenant with HR.Core's save pipeline in the host's order: the immutability check, the national write guard,
/// then the tenant stamp.
/// </summary>
internal sealed class Ten048PolicyHarness : IAsyncDisposable
{
    public static readonly Guid National = NationalTenant.Id;
    public static readonly Guid AgencyA = Guid.Parse("a0000048-0000-4000-8000-00000000000a");
    public static readonly Guid AgencyB = Guid.Parse("b0000048-0000-4000-8000-00000000000b");
    public static readonly Guid Author = Guid.Parse("00000048-0000-4000-8000-0000000000a1");
    public static readonly Guid Approver = Guid.Parse("00000048-0000-4000-8000-0000000000a2");

    public const string Domain = PolicyDomains.AssetsCustody;
    public const string PrivilegedWhen = """{"all":[{"field":"isPrivileged","operator":"Equals","value":"true"}]}""";

    private readonly SqliteConnection _connection;

    public IConfiguration Configuration { get; } = new ConfigurationBuilder().Build();
    public PolicyPublishedSnapshotCache Cache { get; } = new(new MemoryCache(new MemoryCacheOptions()));
    public Mock<IPolicyPlatformClaimGuard> Guard { get; } = new();

    private Ten048PolicyHarness()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    public static async Task<Ten048PolicyHarness> CreateAsync()
    {
        var harness = new Ten048PolicyHarness();
        await using var db = harness.DesignTime();
        await db.Database.EnsureCreatedAsync();
        await WorkflowPackSeeder.SeedAsync(db);
        await PolicyEngineSeeder.SeedAsync(db);
        return harness;
    }

    /// <summary>No tenant context: what the seeders and the assertions use (it reads every tenant, as its EF filter does).</summary>
    public HRCoreDbContext DesignTime() =>
        new(new DbContextOptionsBuilder<HRCoreDbContext>().UseSqlite(_connection).AddInterceptors(new PublishedPolicyImmutabilityInterceptor()).Options);

    /// <summary>A session of <paramref name="tenant"/>, with HR.Core's interceptors in the order Program.cs registers them.</summary>
    public HRCoreDbContext Session(Guid tenant)
    {
        var context = new FixedTenant(tenant);
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new PublishedPolicyImmutabilityInterceptor(), new NationalSharedWriteGuard(context), new TenantIdStampInterceptor(context))
            .Options;
        return new HRCoreDbContext(options, context);
    }

    public static PolicyRuleValidationService Validation(HRCoreDbContext db) => new(db, AllowAllPolicyDomainCapabilities.Instance);

    public PolicyDraftService Drafts(HRCoreDbContext db) => new(db, Validation(db), Configuration);

    public PolicyPublishService Publish(HRCoreDbContext db)
    {
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns("Production");
        return new PolicyPublishService(db, Validation(db), new PolicyVersionPublisher(db, Cache), Guard.Object,
            Pol047Harness.Authorization(Configuration), Configuration, env.Object);
    }

    public static PolicyOrchestrator Orchestrator(HRCoreDbContext db) =>
        new(db, Mock.Of<IWorkflowStarter>(), new PolicyPublishedSnapshotCache(new MemoryCache(new MemoryCacheOptions())), NullLogger<PolicyOrchestrator>.Instance);

    public static PolicyActor ApproverActor => Pol047Harness.Actor(Approver, HrManagementRoles.HrManagementApprover);

    public async Task<PolicyTemplate> NationalTemplateAsync(string domain = Domain)
    {
        await using var db = DesignTime();
        return await db.PolicyTemplates.AsNoTracking()
            .SingleAsync(t => t.TenantId == National && t.Domain == domain && t.Scope == PolicyTemplateScope.National);
    }

    /// <summary>An agency override written before the ceiling (an "old layer"): published as it is, with no validation.</summary>
    public async Task<(PolicyTemplate Template, PolicyTemplateVersion Version)> SeedOldOverrideAsync(Guid tenant, params PolicyRule[] rules)
    {
        await using var db = DesignTime();
        var template = new PolicyTemplate
        {
            TenantId = tenant,
            Domain = Domain,
            Name = "old overlay",
            SubjectType = "AgencyOverlay",
            Scope = PolicyTemplateScope.TenantOverride
        };
        db.PolicyTemplates.Add(template);
        foreach (var rule in rules)
            rule.TenantId = tenant;
        var version = await PolicySeedVersionWriter.AddAsync(db, new PolicyTemplateVersion
        {
            PolicyTemplate = template,
            TenantId = tenant,
            VersionNumber = 1,
            EffectiveFrom = DateTime.UtcNow.AddDays(-10)
        }, rules.ToList(), PolicyVersionStatus.Published,
            new PolicyPublication { TenantId = tenant, PublishedAt = DateTime.UtcNow.AddDays(-10), EvidenceRef = "old-overlay" });
        return (template, version);
    }

    public static PolicyRule Rule(string key, int priority, string thenJson, string whenJson = PrivilegedWhen) =>
        new() { RuleKey = key, Name = key, Priority = priority, WhenJson = whenJson, ThenJson = thenJson };

    public static Dictionary<string, string> PrivilegedFacts(string action = "Assign") => new(StringComparer.OrdinalIgnoreCase)
    {
        ["isPrivileged"] = "true",
        ["value"] = "500",
        ["category"] = "أثاث",
        ["hasOriginReceipt"] = "true",
        ["isLegacyEntry"] = "false",
        ["action"] = action
    };

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public bool IsPlatformOperator => false;
        public Guid RequireTenantId() => tenantId;
    }
}
