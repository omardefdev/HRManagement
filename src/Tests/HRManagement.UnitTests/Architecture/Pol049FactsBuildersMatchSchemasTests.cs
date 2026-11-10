using System.Reflection;
using System.Text.Json;
using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Infrastructure.Integration;
using HR.Core.Domain.Entities.Policy;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Policy;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.SharedKernel.Policy;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests.Architecture;

/// <summary>
/// PLAN-49 (G-POL-04): the facts a call site sends match the schema of its domain. For every entity type and action of the
/// catalog, the builder its call site uses sends only fields of the seeded facts schema (the strict evaluation refuses a request
/// with any other key: it refused every card and every leave request for the key employeeId); and every field a rule of an
/// effective version reads is sent by the builder of each action the rule can match (isSelfApproval was read by a rule and sent
/// by nobody; hasOrgScope of the establishment was a constant).
/// </summary>
public sealed class Pol049FactsBuildersMatchSchemasTests
{
    private static readonly Guid Tenant = Guid.Parse("a0000049-0000-4000-8000-00000000000a");

    private static Asset SampleAsset() => new() { TagNumber = "T-49", Name = "أصل", Value = 12_000m, Category = "أصول ثابتة", IsPrivileged = true };

    /// <summary>
    /// Entity type and action of the catalog → the request the call site of that action builds (the same builder, with sample
    /// values). The call sites are the files PolicyDomainCatalog lists; Pol049PolicyDomainCatalogTests checks that they exist
    /// and name their builder.
    /// </summary>
    private static readonly IReadOnlyDictionary<(string Domain, string SubjectType, string Action), Func<PolicyEnforceRequest>> Builders =
        new Dictionary<(string, string, string), Func<PolicyEnforceRequest>>
        {
            [(PolicyDomains.AssetsCustody, "Asset", "Classify")] = () => AssetPolicyFactsBuilder.ForClassify(SampleAsset(), Tenant),
            [(PolicyDomains.AssetsCustody, "Asset", "Create")] = () => AssetPolicyFactsBuilder.ForCreate(SampleAsset(), Tenant),
            [(PolicyDomains.AssetsCustody, "AssetAssignment", "Assign")] = () => AssetPolicyFactsBuilder.ForAssign(SampleAsset(), Tenant, Guid.NewGuid(), null),

            [(PolicyDomains.IDCardLifecycle, "CardRequest", "Process")] = () => PolicyDomainFactsBuilders.ForIDCardRequest(Guid.NewGuid(), Tenant, "NewEmployee", "Process"),

            [(PolicyDomains.ContractsLeave, "LeaveRequest", "Create")] = () => PolicyDomainFactsBuilders.ForLeaveRequest(Guid.NewGuid(), Tenant, 3, "Annual", "Create"),
            [(PolicyDomains.ContractsLeave, "LeaveRequest", "Update")] = () => PolicyDomainFactsBuilders.ForLeaveRequest(Guid.NewGuid(), Tenant, 4, "Sick", "Update"),
            [(PolicyDomains.ContractsLeave, "LeaveRequest", "Approve")] = () => PolicyDomainFactsBuilders.ForLeaveRequest(Guid.NewGuid(), Tenant, 3, "Annual", "Approve", isSelfApproval: false),

            [(PolicyDomains.CoreRecruitment, "RecruitmentRequisition", "SubmitForApproval")] = () => PolicyDomainFactsBuilders.ForRecruitmentRequisition(Guid.NewGuid(), Tenant, "SubmitForApproval", "Draft", true, Guid.NewGuid()),
            [(PolicyDomains.CoreRecruitment, "RecruitmentRequisition", "Publish")] = () => PolicyDomainFactsBuilders.ForRecruitmentRequisition(Guid.NewGuid(), Tenant, "Publish", "Approved", true, Guid.NewGuid()),
            [(PolicyDomains.CoreRecruitment, "RecruitmentRequisition", "PublishInternal")] = () => PolicyDomainFactsBuilders.ForRecruitmentRequisition(Guid.NewGuid(), Tenant, "PublishInternal", "Approved", true, Guid.NewGuid()),
            [(PolicyDomains.CoreRecruitment, "RecruitmentApplication", "Hire")] = () => PolicyDomainFactsBuilders.ForRecruitmentApplication(Guid.NewGuid(), Tenant, "Hire", true, true, true, "Approved", true),
            [(PolicyDomains.CoreRecruitment, "OfferLetter", "Issue")] = () => PolicyDomainFactsBuilders.ForOfferLetter(Guid.NewGuid(), Tenant, "Issue", Guid.NewGuid()),
            [(PolicyDomains.CoreRecruitment, "SelectionCommitteeSession", "SubmitCommittee")] = () => PolicyDomainFactsBuilders.ForGeneric(PolicyDomains.CoreRecruitment, "SelectionCommitteeSession", "SubmitCommittee", Guid.NewGuid(), Tenant),
            [(PolicyDomains.CoreRecruitment, "RecruitmentAppeal", "FileAppeal")] = () => PolicyDomainFactsBuilders.ForGeneric(PolicyDomains.CoreRecruitment, "RecruitmentAppeal", "FileAppeal", Guid.NewGuid(), Tenant),

            [(PolicyDomains.CoreEstablishment, "EstablishmentChangeRequest", "SubmitForApproval")] = () => PolicyDomainFactsBuilders.ForEstablishmentChange(Guid.NewGuid(), Tenant, "SubmitForApproval", "CreateJob", "Draft", actorIsBranchScoped: true)
        };

    private static async Task<Pol047Harness> SeededAsync()
    {
        var h = new Pol047Harness();
        await WorkflowPackSeeder.SeedAsync(h.Db);
        await PolicyEngineSeeder.SeedAsync(h.Db);
        h.Db.ChangeTracker.Clear();
        return h;
    }

    private static async Task<Dictionary<string, PolicyFactsSchemaDefinition>> SchemasAsync(Pol047Harness h) =>
        (await h.Db.PolicyFactsSchemas.AsNoTracking().ToListAsync()).ToDictionary(
            s => s.Domain,
            s => new PolicyFactsSchemaDefinition
            {
                Domain = s.Domain,
                SubjectType = s.SubjectType,
                Fields = JsonSerializer.Deserialize<List<PolicyFieldSchema>>(s.FieldsJson) ?? []
            },
            StringComparer.Ordinal);

    [Fact]
    public void The_table_holds_a_builder_for_every_entity_type_and_action_of_the_catalog_and_for_nothing_else()
    {
        var catalog = PolicyDomainCatalog.Governed
            .SelectMany(d => d.SubjectActions.SelectMany(kv => kv.Value.Select(action => (d.Domain, SubjectType: kv.Key, Action: action))))
            .OrderBy(k => k.Domain, StringComparer.Ordinal).ThenBy(k => k.SubjectType, StringComparer.Ordinal).ThenBy(k => k.Action, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(catalog, Builders.Keys.OrderBy(k => k.Domain, StringComparer.Ordinal).ThenBy(k => k.SubjectType, StringComparer.Ordinal).ThenBy(k => k.Action, StringComparer.Ordinal));
        Assert.Equal(15, catalog.Count);
    }

    [Fact]
    public async Task Every_builder_sends_its_own_domain_type_and_action_and_only_fields_of_the_seeded_schema()
    {
        await using var h = await SeededAsync();
        var schemas = await SchemasAsync(h);

        foreach (var ((domain, subjectType, action), build) in Builders)
        {
            var request = build();
            Assert.Equal(domain, request.Domain);
            Assert.Equal(subjectType, request.SubjectType);
            Assert.Equal(action, request.Action);
            Assert.Equal(action, request.Facts["action"]);
            Assert.Equal(Tenant, request.TenantId);

            Assert.True(schemas.ContainsKey(domain), $"{domain}: no facts schema is seeded.");
            var errors = PolicyFactsValidator.Validate(schemas[domain], request.Facts);
            Assert.True(errors.Count == 0, $"{domain} {subjectType}/{action}: {string.Join("; ", errors)}");

            // The descriptor accepts the pair, as the internal endpoint asks it.
            Assert.True(PolicyDomainCatalog.Find(domain)!.AllowsAction(subjectType, action));
        }
    }

    [Fact]
    public async Task The_engine_takes_every_request_without_a_schema_refusal()
    {
        // The same, through the orchestrator on the seeded database: no decision carries the schema's «حقل غير مسموح».
        await using var h = await SeededAsync();
        var orchestrator = Ten048PolicyHarness.Orchestrator(h.Db);

        foreach (var (key, build) in Builders)
        {
            var request = build();
            request.TenantId = Pol047Harness.Tenant;
            var decision = await orchestrator.EvaluateAsync(request);

            Assert.DoesNotContain(decision.Explanations, e => e.Contains("حقل غير مسموح", StringComparison.Ordinal));
            Assert.DoesNotContain(decision.Explanations, e => e.Contains("حقل مطلوب", StringComparison.Ordinal));
            Assert.True(decision.Allowed || decision.MatchedRuleIds.Count > 0, $"{key}: refused with no rule: {string.Join("; ", decision.Explanations)}");
        }
    }

    [Fact]
    public async Task Every_field_an_effective_rule_reads_is_sent_for_each_action_the_rule_can_match()
    {
        await using var h = await SeededAsync();
        var templates = await h.Db.PolicyTemplates.AsNoTracking().Include(t => t.Versions).ThenInclude(v => v.Rules).ToListAsync();

        var checkedRules = 0;
        foreach (var template in templates)
        {
            var descriptor = PolicyDomainCatalog.Find(template.Domain)!;
            var effective = PolicyDraftService.EffectiveVersion(template, DateTime.UtcNow);
            foreach (var rule in effective?.Rules ?? [])
            {
                checkedRules++;
                var issues = new List<PolicyRuleIssue>();
                var when = PolicyRuleJson.ParseWhenStrict(rule.WhenJson, issues);
                Assert.True(when is not null && issues.Count == 0, rule.RuleKey);
                var fields = PolicyRuleJson.Leaves(when!).Select(l => l.Leaf.Field).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                foreach (var action in PolicyRuleActionScope.PossibleActions(when!, descriptor.Actions))
                {
                    foreach (var subjectType in descriptor.SubjectTypesOf(action))
                    {
                        var sent = Builders[(template.Domain, subjectType, action)]().Facts;
                        Assert.All(fields, field => Assert.True(sent.ContainsKey(field),
                            $"{template.Domain} {rule.RuleKey} reads \"{field}\", which the call site of {subjectType}/{action} does not send."));
                    }
                }
            }
        }

        Assert.True(checkedRules >= 19, $"only {checkedRules} effective rules were checked");
    }

    [Fact]
    public void The_approval_is_the_only_leave_action_that_states_who_approves_and_no_builder_names_the_employee()
    {
        Assert.False(Builders[(PolicyDomains.ContractsLeave, "LeaveRequest", "Create")]().Facts.ContainsKey("isSelfApproval"));
        Assert.False(Builders[(PolicyDomains.ContractsLeave, "LeaveRequest", "Update")]().Facts.ContainsKey("isSelfApproval"));
        Assert.Equal("false", Builders[(PolicyDomains.ContractsLeave, "LeaveRequest", "Approve")]().Facts["isSelfApproval"]);
        Assert.Equal("true", PolicyDomainFactsBuilders.ForLeaveRequest(Guid.NewGuid(), Tenant, 1, "Annual", "Approve", isSelfApproval: true).Facts["isSelfApproval"]);

        // The establishment fact is the actor's scope, either way.
        Assert.Equal("true", PolicyDomainFactsBuilders.ForEstablishmentChange(Guid.NewGuid(), Tenant, "SubmitForApproval", "X", "Draft", actorIsBranchScoped: true).Facts["hasOrgScope"]);
        Assert.Equal("false", PolicyDomainFactsBuilders.ForEstablishmentChange(Guid.NewGuid(), Tenant, "SubmitForApproval", "X", "Draft", actorIsBranchScoped: false).Facts["hasOrgScope"]);
        var establishment = typeof(PolicyDomainFactsBuilders).GetMethod(nameof(PolicyDomainFactsBuilders.ForEstablishmentChange))!;
        Assert.False(establishment.GetParameters().Single(p => p.Name == "actorIsBranchScoped").HasDefaultValue);

        foreach (var build in Builders.Values)
            Assert.DoesNotContain(build().Facts.Keys, k => k.Contains("employee", StringComparison.OrdinalIgnoreCase));

        // The advance builder (a domain out of the engine's scope, never called) is gone, and nothing in the product names it.
        Assert.DoesNotContain(typeof(PolicyDomainFactsBuilders).GetMethods(BindingFlags.Public | BindingFlags.Static), m => m.Name == "ForAdvanceRequest");
        var source = global::System.IO.File.ReadAllText(HRManagement.TestSupport.RepoPaths.Resolve(
            "src", "Shared", "HRManagement.ServiceDefaults", "Policy", "PolicyDomainFactsBuilders.cs"));
        Assert.DoesNotContain("employeeId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ForAdvanceRequest", source, StringComparison.Ordinal);
        foreach (var d in PolicyDomainCatalog.OutOfScopeDomains)
            Assert.DoesNotContain($"\"{d.Domain}\"", source, StringComparison.Ordinal);
    }
}
