using HR.Core.Domain.Entities.CodeGeneration;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.CodeGeneration;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-48 (§2): every tenant sees the national code policies; the agency's overlay comes first, then the national template, with
/// the same kind and policy key; a request without a key never gets a keyed template and a key that does not exist is an error;
/// a missing policy is refused (422) before any counter moves, and a fallback number exists only in Development and Testing,
/// always with a prefix.
/// </summary>
public class Ten048ResolutionTests
{
    private static readonly Guid AgencyA = Guid.Parse("a0000001-0000-4000-8000-000000000001");
    private static readonly Guid AgencyB = Guid.Parse("a0000002-0000-4000-8000-000000000001");
    private static readonly int Year = DateTime.UtcNow.Year;

    private sealed class FixedTenant(Guid? tenantId) : ITenantContext
    {
        public Guid? TenantId => tenantId;
        public bool IsPlatformOperator => false;
        public Guid RequireTenantId() => tenantId ?? throw new InvalidOperationException();
    }

    private static DbContextOptions<HRCoreDbContext> Options(string name) =>
        new DbContextOptionsBuilder<HRCoreDbContext>().UseInMemoryDatabase(name).Options;

    private static CodePolicyTemplate Template(Guid tenant, CodePolicyScope scope, CodeEntityKind kind, string? key, string segments, CodeSequenceReset reset, string prefix)
    {
        var template = new CodePolicyTemplate { TenantId = tenant, Name = prefix, EntityKind = kind, Scope = scope, PolicyKey = key };
        template.Versions.Add(new CodePolicyVersion
        {
            TenantId = tenant,
            Status = CodePolicyVersionStatus.Published,
            EffectiveFrom = DateTime.UtcNow.AddDays(-1),
            SegmentsJson = segments,
            SequenceReset = reset,
            FallbackPrefix = prefix
        });
        return template;
    }

    private static string Yearly(string prefix, int pad = 6) =>
        $$"""[{"kind":"Literal","value":"{{prefix}}-"},{"kind":"Date","value":"YYYY"},{"kind":"Literal","value":"-"},{"kind":"Sequence","padWidth":{{pad}}}]""";

    /// <summary>National AH (default) and VIP (ASSET-PRIVILEGED); agency A's default overlay AGA; the trial agency's PLATFORM overlay TRL.</summary>
    private static async Task<string> SeedAsync()
    {
        var name = Guid.NewGuid().ToString();
        await using var db = new HRCoreDbContext(Options(name));
        db.CodePolicyTemplates.AddRange(
            Template(NationalTenant.Id, CodePolicyScope.National, CodeEntityKind.AssetTag, null, Yearly("AH"), CodeSequenceReset.Yearly, "AH"),
            Template(NationalTenant.Id, CodePolicyScope.National, CodeEntityKind.AssetTag, "ASSET-PRIVILEGED", Yearly("VIP"), CodeSequenceReset.Yearly, "VIP"),
            Template(AgencyA, CodePolicyScope.TenantOverride, CodeEntityKind.AssetTag, null, Yearly("AGA"), CodeSequenceReset.Yearly, "AGA"),
            Template(NationalTenant.Id, CodePolicyScope.TenantOverride, CodeEntityKind.LegalCase, null, Yearly("TRL", 5), CodeSequenceReset.Yearly, "TRL"));
        await db.SaveChangesAsync();
        return name;
    }

    private static CodeGeneratorService Generator(HRCoreDbContext db, CodeGenerationOptions? options = null) =>
        new(db, new MemoryCache(new MemoryCacheOptions()), NullLogger<CodeGeneratorService>.Instance,
            options is null ? null : Microsoft.Extensions.Options.Options.Create(options));

    private static async Task<string> GenerateAsync(string db, Guid tenant, CodeEntityKind kind, string? key = null, Dictionary<string, string>? facts = null)
    {
        await using var context = new HRCoreDbContext(Options(db), new FixedTenant(tenant));
        var result = await Generator(context).GenerateAsync(new CodeGenerationRequest
        {
            EntityKind = kind,
            PolicyKey = key,
            Facts = facts ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        });
        return result.Code;
    }

    [Fact]
    public async Task The_agency_overlay_comes_first_and_an_agency_without_one_gets_the_national_template()
    {
        var db = await SeedAsync();
        Assert.Equal($"AGA-{Year}-000001", await GenerateAsync(db, AgencyA, CodeEntityKind.AssetTag));
        Assert.Equal($"AH-{Year}-000001", await GenerateAsync(db, AgencyB, CodeEntityKind.AssetTag));
        Assert.Equal($"AH-{Year}-000001", await GenerateAsync(db, NationalTenant.Id, CodeEntityKind.AssetTag));
    }

    [Fact]
    public async Task A_key_reaches_only_its_template_and_a_request_without_a_key_never_gets_a_keyed_one()
    {
        var db = await SeedAsync();
        Assert.Equal($"VIP-{Year}-000001", await GenerateAsync(db, AgencyB, CodeEntityKind.AssetTag, "ASSET-PRIVILEGED"));
        Assert.Equal($"VIP-{Year}-000001", await GenerateAsync(db, AgencyA, CodeEntityKind.AssetTag, "ASSET-PRIVILEGED"));
        for (var i = 0; i < 5; i++)
            Assert.StartsWith("AH-", await GenerateAsync(db, AgencyB, CodeEntityKind.AssetTag));

        // The key is part of the counter: VIP and AH count apart.
        Assert.Equal($"VIP-{Year}-000002", await GenerateAsync(db, AgencyB, CodeEntityKind.AssetTag, "ASSET-PRIVILEGED"));
        Assert.Equal($"AH-{Year}-000006", await GenerateAsync(db, AgencyB, CodeEntityKind.AssetTag));

        var missing = await Assert.ThrowsAsync<CodePolicyMissingException>(() => GenerateAsync(db, AgencyB, CodeEntityKind.AssetTag, "NO-SUCH-KEY"));
        Assert.Equal("NO-SUCH-KEY", missing.PolicyKey);
    }

    [Fact]
    public async Task An_agency_never_numbers_with_the_trial_agencys_override_in_the_national_tenant()
    {
        var db = await SeedAsync();
        // Only a Scope = National template is national; PLATFORM's own override numbers PLATFORM only.
        await Assert.ThrowsAsync<CodePolicyMissingException>(() => GenerateAsync(db, AgencyB, CodeEntityKind.LegalCase));
        Assert.Equal($"TRL-{Year}-00001", await GenerateAsync(db, NationalTenant.Id, CodeEntityKind.LegalCase));
    }

    [Fact]
    public async Task A_missing_policy_is_refused_before_any_counter_moves_and_burns_no_number()
    {
        var db = await SeedAsync();
        var refused = await Assert.ThrowsAsync<CodePolicyMissingException>(() => GenerateAsync(db, AgencyB, CodeEntityKind.ForensicCase));
        Assert.Equal(CodeEntityKind.ForensicCase, refused.Kind);
        Assert.Equal(AgencyB, refused.TenantId);
        Assert.Contains("لا توجد سياسة ترميز منشورة", refused.MessageAr);

        await using var check = new HRCoreDbContext(Options(db));
        Assert.False(await check.CodeSequenceCounters.AnyAsync(c => c.EntityKind == CodeEntityKind.ForensicCase));
        Assert.False(await check.CodeIssuances.AnyAsync(i => i.EntityKind == CodeEntityKind.ForensicCase));
    }

    [Fact]
    public async Task Development_and_Testing_fall_back_with_a_prefix_never_an_empty_one()
    {
        var db = await SeedAsync();
        await using var context = new HRCoreDbContext(Options(db), new FixedTenant(AgencyB));
        var options = new CodeGenerationOptions { MissingPolicy = CodeMissingPolicyMode.Fallback };
        var result = await Generator(context, options).GenerateAsync(new CodeGenerationRequest { EntityKind = CodeEntityKind.ForensicCase });
        Assert.Equal($"FOR-{Year}-00001", result.Code);
        Assert.Null(result.PolicyVersionId);
        Assert.DoesNotMatch("^-", result.Code);
    }

    [Theory]
    [InlineData("Production", null, CodeMissingPolicyMode.Refuse)]
    [InlineData("Production", "Fallback", CodeMissingPolicyMode.Refuse)]
    [InlineData("Acceptance", "Fallback", CodeMissingPolicyMode.Refuse)]
    [InlineData("Development", null, CodeMissingPolicyMode.Fallback)]
    [InlineData("Testing", null, CodeMissingPolicyMode.Fallback)]
    [InlineData("Development", "Refuse", CodeMissingPolicyMode.Refuse)]
    public void The_missing_policy_mode_follows_D_TEN_9(string environment, string? configured, CodeMissingPolicyMode expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configured is null ? [] : [new KeyValuePair<string, string?>("CodeGeneration:MissingPolicy", configured)])
            .Build();
        var warnings = new List<string>();
        var options = CodeGenerationOptions.Resolve(configuration, new FakeEnvironment(environment), warnings.Add);
        Assert.Equal(expected, options.MissingPolicy);
        Assert.Equal(configured == "Fallback" && expected == CodeMissingPolicyMode.Refuse, warnings.Count == 1);
        Assert.Equal(CodeMissingPolicyMode.Refuse, CodeGenerationOptions.Resolve(null, null).MissingPolicy);
    }

    [Fact]
    public async Task A_published_policy_the_validator_refuses_is_never_used()
    {
        var name = Guid.NewGuid().ToString();
        await using (var seed = new HRCoreDbContext(Options(name)))
        {
            // Yearly reset with no year in the number: it would repeat every January.
            seed.CodePolicyTemplates.Add(Template(NationalTenant.Id, CodePolicyScope.National, CodeEntityKind.LegalCase, null,
                """[{"kind":"Literal","value":"LC-"},{"kind":"Sequence","padWidth":5}]""", CodeSequenceReset.Yearly, "LC"));
            await seed.SaveChangesAsync();
        }

        var invalid = await Assert.ThrowsAsync<CodePolicyInvalidException>(() => GenerateAsync(name, AgencyB, CodeEntityKind.LegalCase));
        Assert.Contains(invalid.Issues, i => i.Code == "YEARLY_WITHOUT_YEAR");
        await using var check = new HRCoreDbContext(Options(name));
        Assert.False(await check.CodeSequenceCounters.AnyAsync());
    }

    [Fact]
    public async Task A_fact_the_number_shows_is_required_before_the_counter_moves()
    {
        var name = Guid.NewGuid().ToString();
        await using (var seed = new HRCoreDbContext(Options(name)))
        {
            seed.CodePolicyTemplates.Add(Template(NationalTenant.Id, CodePolicyScope.National, CodeEntityKind.ServiceTransaction, null,
                """[{"kind":"Literal","value":"SRV-"},{"kind":"Field","value":"TypeCode"},{"kind":"Literal","value":"-"},{"kind":"Date","value":"YYYY"},{"kind":"Literal","value":"-"},{"kind":"Sequence","padWidth":5,"scopeFields":["TypeCode"]}]""",
                CodeSequenceReset.Yearly, "SRV"));
            await seed.SaveChangesAsync();
        }

        var missing = await Assert.ThrowsAsync<CodeFactMissingException>(() => GenerateAsync(name, AgencyB, CodeEntityKind.ServiceTransaction));
        Assert.Equal("TypeCode", missing.FactName);
        await using (var check = new HRCoreDbContext(Options(name)))
            Assert.False(await check.CodeSequenceCounters.AnyAsync());

        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["TypeCode"] = "LEAV" };
        Assert.Equal($"SRV-LEAV-{Year}-00001", await GenerateAsync(name, AgencyB, CodeEntityKind.ServiceTransaction, facts: facts));
    }

    [Fact]
    public async Task The_snapshot_cache_keeps_the_key_apart()
    {
        var db = await SeedAsync();
        await using var context = new HRCoreDbContext(Options(db), new FixedTenant(AgencyB));
        var generator = Generator(context);
        var plain = await generator.GetPublishedSnapshotAsync(CodeEntityKind.AssetTag, AgencyB);
        var keyed = await generator.GetPublishedSnapshotAsync(CodeEntityKind.AssetTag, AgencyB, default, "ASSET-PRIVILEGED");
        Assert.Equal("AH", plain!.FallbackPrefix);
        Assert.Equal("VIP", keyed!.FallbackPrefix);
        Assert.Equal("AH", (await generator.GetPublishedSnapshotAsync(CodeEntityKind.AssetTag, AgencyB))!.FallbackPrefix);
    }

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
