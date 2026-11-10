using System.Security.Claims;
using System.Text.Json;
using HR.Core.Domain.Entities.Policy;
using HR.Core.Domain.Services.Policy;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Data.Interceptors;
using HR.Core.Infrastructure.Services.Policy;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.ProdPolicy;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>PLAN-47: an SQLite HR.Core database with the immutability interceptor, and the policy lifecycle services over it.</summary>
internal sealed class Pol047Harness : IAsyncDisposable
{
    public static readonly Guid Tenant = TenantDefaults.DefaultTenantId;
    public const string Domain = PolicyDomains.AssetsCustody;
    public const string ValidWhen = """{"all":[{"field":"category","operator":"Equals","value":"أصول ثابتة"}]}""";
    public const string ValidThen = """[{"effect":"RequireEvidence"}]""";

    private readonly SqliteConnection _connection;

    public HRCoreDbContext Db { get; }
    public Mock<IPolicyPlatformClaimGuard> Guard { get; } = new();
    public IConfiguration Configuration { get; }
    public string Environment { get; }
    public PolicyPublishedSnapshotCache Cache { get; } = new(new MemoryCache(new MemoryCacheOptions()));

    public Pol047Harness(Dictionary<string, string?>? settings = null, string environment = "Production", bool interceptor = true)
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var builder = new DbContextOptionsBuilder<HRCoreDbContext>().UseSqlite(_connection);
        if (interceptor)
            builder.AddInterceptors(new PublishedPolicyImmutabilityInterceptor());
        Db = new HRCoreDbContext(builder.Options);
        Db.Database.EnsureCreated();
        Configuration = new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build();
        Environment = environment;
    }

    public PolicyRuleValidationService Validation => PolicyRuleValidationService.ForCatalog(Db);

    public PolicyVersionPublisher Publisher => new(Db, Cache);

    public PolicyDraftService Drafts => new(Db, Validation, Configuration);

    public PolicyPublishService PublishService
    {
        get
        {
            var env = new Mock<IHostEnvironment>();
            env.SetupGet(e => e.EnvironmentName).Returns(Environment);
            return new PolicyPublishService(Db, Validation, Publisher, Guard.Object, Authorization(Configuration), Configuration, env.Object);
        }
    }

    /// <summary>The real policy handler over the given configuration.</summary>
    public static IAuthorizationService Authorization(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddAuthorization(o =>
        {
            o.AddPolicy(HrManagementPolicies.PolicyAuthor, p => p.AddRequirements(new PolicyEngineRoleRequirement(PolicyEngineRoleKind.Author)));
            o.AddPolicy(HrManagementPolicies.PolicyApprover, p => p.AddRequirements(new PolicyEngineRoleRequirement(PolicyEngineRoleKind.Approver)));
            o.AddPolicy(HrManagementPolicies.PolicyReader, p => p.AddRequirements(new PolicyEngineRoleRequirement(PolicyEngineRoleKind.Reader)));
        });
        services.AddSingleton<IAuthorizationHandler, PolicyEngineRoleHandler>();
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    public static ClaimsPrincipal User(Guid id, params string[] roles)
    {
        var claims = new List<Claim> { new("sub", id.ToString()), new(ClaimTypes.NameIdentifier, id.ToString()) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    public static PolicyActor Actor(Guid id, params string[] roles) => new(id, User(id, roles));

    /// <summary>A national custody template with its facts schema and a published v1 (one rule).</summary>
    public async Task<(PolicyTemplate Template, PolicyTemplateVersion V1)> SeedTemplateAsync(DateTime? v1From = null)
    {
        Db.PolicyFactsSchemas.Add(new PolicyFactsSchemaEntity
        {
            TenantId = Tenant,
            Domain = Domain,
            SubjectType = "Asset",
            FieldsJson = JsonSerializer.Serialize(new List<PolicyFieldSchema>
            {
                new() { Name = "category", Type = "string" },
                new() { Name = "value", Type = "decimal" },
                new() { Name = "isPrivileged", Type = "bool" },
                new() { Name = "action", Type = "string" }
            })
        });
        var template = new PolicyTemplate
        {
            TenantId = Tenant,
            Domain = Domain,
            Name = "custody",
            SubjectType = "Asset",
            Scope = PolicyTemplateScope.National
        };
        Db.PolicyTemplates.Add(template);
        var v1 = await PolicySeedVersionWriter.AddAsync(Db, new PolicyTemplateVersion
        {
            PolicyTemplate = template,
            TenantId = Tenant,
            VersionNumber = 1,
            EffectiveFrom = v1From ?? DateTime.UtcNow.AddDays(-30)
        },
        [new PolicyRule { RuleKey = "R-V1", Name = "v1", Priority = 10, WhenJson = """{"all":[{"field":"isPrivileged","operator":"Equals","value":"true"}]}""", ThenJson = ValidThen }],
        PolicyVersionStatus.Published,
        new PolicyPublication { PublishedAt = DateTime.UtcNow.AddDays(-30), EvidenceRef = "seed" });
        return (template, v1);
    }

    /// <summary>A draft by <paramref name="author"/>: a copy of the effective version plus one added rule.</summary>
    public async Task<Guid> DraftByAsync(Guid templateId, Guid author, string ruleKey = "R-NEW")
    {
        var created = await Drafts.CreateDraftAsync(templateId, null, author);
        Assert.True(created.Succeeded, created.MessageAr);
        var added = await Drafts.AddRuleAsync(created.Id!.Value, new PolicyRuleInput(ruleKey, "new", 20, ValidWhen, ValidThen), author);
        Assert.True(added.Succeeded, added.MessageAr);
        return created.Id.Value;
    }

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
