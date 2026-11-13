using System.Net;
using System.Text.RegularExpressions;
using HR.Core.Domain.Entities.Policy;
using HR.Core.Domain.Services.Policy;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Policy;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-67 (§5, G-UX-06, P-8): on HR.Core with the seeded policy engine (every domain's first version, the bindings and the seed change
/// sets), every seeded and published rule reads as an Arabic sentence that names each field, operator, value and effect of its JSON;
/// the policy page shows the sentences, and the JSON under «عرض تقني» to the policy roles only (D-67-4); the simulation form gives the
/// decision the JSON input gives, and an unknown fact is refused as before.
/// </summary>
public sealed class Ux067PolicyPagesTests : IClassFixture<Hcm025ScopeAndPolicyTestHost>, IAsyncLifetime
{
    private readonly Hcm025ScopeAndPolicyTestHost _host;
    private static readonly Regex Token = new("name=\"__RequestVerificationToken\"[^>]*value=\"(?<t>[^\"]+)\"", RegexOptions.CultureInvariant);

    public Ux067PolicyPagesTests(Hcm025ScopeAndPolicyTestHost host) => _host = host;

    public Task InitializeAsync() =>
        _host.WithDbAsync(async db =>
        {
            if (!await db.PolicyTemplates.IgnoreQueryFilters().AnyAsync())
                await PolicyEngineSeeder.SeedAsync(db);
            return true;
        });

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(List<PolicyRule> Rules, Dictionary<string, PolicyFactsSchemaDefinition?> Schemas, Dictionary<Guid, string> DomainOfVersion)> PublishedRulesAsync() =>
        await _host.WithDbAsync(async db =>
        {
            var versions = await db.PolicyTemplateVersions.IgnoreQueryFilters().AsNoTracking()
                .Include(v => v.PolicyTemplate).Include(v => v.Rules)
                .Where(v => v.Status == PolicyVersionStatus.Published || v.Status == PolicyVersionStatus.Archived)
                .ToListAsync();
            var schemas = (await db.PolicyFactsSchemas.IgnoreQueryFilters().AsNoTracking().ToListAsync())
                .GroupBy(s => s.Domain).ToDictionary(g => g.Key, g => PolicyEntityMapper.ParseSchema(g.First()));
            return (versions.SelectMany(v => v.Rules).ToList(), schemas, versions.ToDictionary(v => v.Id, v => v.PolicyTemplate.Domain));
        });

    [Fact]
    public async Task Every_seeded_and_published_rule_names_each_field_operator_value_and_effect_of_its_json()
    {
        var (rules, schemas, domainOf) = await PublishedRulesAsync();
        Assert.NotEmpty(rules);
        foreach (var rule in rules)
        {
            var schema = schemas.GetValueOrDefault(domainOf[rule.PolicyTemplateVersionId]);
            var readable = PolicyRuleReadableRenderer.Render(rule.WhenJson, rule.ThenJson, schema);
            var sentence = readable.Sentence;
            Assert.False(readable.HasUnknown, $"{rule.RuleKey}: {sentence}");

            var issues = new List<PolicyRuleIssue>();
            var when = PolicyRuleJson.ParseWhenStrict(rule.WhenJson, issues);
            Assert.True(when is not null, $"{rule.RuleKey}: the seeded When does not parse");
            foreach (var (leaf, _) in PolicyRuleJson.Leaves(when!))
            {
                var field = schema?.Fields.FirstOrDefault(f => string.Equals(f.Name, leaf.Field, StringComparison.OrdinalIgnoreCase));
                Assert.Contains($"«{field?.DisplayNameAr ?? leaf.Field}»", sentence, StringComparison.Ordinal);
                Assert.Contains(PolicyVocabularyAr.Operators[leaf.Operator], sentence, StringComparison.Ordinal);
                var value = field?.Type is "bool" && bool.TryParse(leaf.Value, out var flag) ? (flag ? "نعم" : "لا")
                    : string.Equals(leaf.Field, "action", StringComparison.OrdinalIgnoreCase) ? PolicyVocabularyAr.Action(leaf.Value)
                    : leaf.Value;
                foreach (var part in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    Assert.Contains(part, sentence, StringComparison.Ordinal);
            }

            var effects = PolicyRuleJson.ParseThenStrict(rule.ThenJson, issues);
            Assert.True(effects is { Count: > 0 }, $"{rule.RuleKey}: the seeded Then does not parse");
            foreach (var effect in effects!)
            {
                Assert.Contains(PolicyVocabularyAr.Effects[effect.Effect], sentence, StringComparison.Ordinal);
                var parameters = effect.Params ?? new Dictionary<string, string>();
                var target = parameters.TryGetValue("field", out var targetKey)
                    ? schema?.Fields.FirstOrDefault(f => string.Equals(f.Name, targetKey, StringComparison.OrdinalIgnoreCase))
                    : null;
                foreach (var (key, value) in parameters)
                {
                    var shown = key switch
                    {
                        "workflowCode" => PolicyVocabularyAr.Workflow(value),
                        "field" => target?.DisplayNameAr ?? value,
                        "value" when target?.Type is "bool" && bool.TryParse(value, out var b) => b ? "نعم" : "لا",
                        _ => value
                    };
                    Assert.Contains(shown, sentence, StringComparison.Ordinal);
                }
            }
        }
    }

    private async Task<(Guid TemplateId, Guid VersionId)> EstablishmentPolicyAsync() =>
        await _host.WithDbAsync(async db =>
        {
            var template = await db.PolicyTemplates.IgnoreQueryFilters().AsNoTracking().Include(t => t.Versions)
                .FirstAsync(t => t.Domain == PolicyDomains.CoreEstablishment);
            return (template.Id, template.Versions.Where(v => v.Status == PolicyVersionStatus.Published).OrderByDescending(v => v.VersionNumber).First().Id);
        });

    [Theory]
    [InlineData("hr-admin", true)]
    [InlineData("policy-approver", true)]
    [InlineData("hr-core-supervisory", false)]
    public async Task The_policy_page_shows_sentences_and_the_json_only_to_the_policy_roles(string role, bool seesJson)
    {
        var (templateId, _) = await EstablishmentPolicyAsync();
        using var client = _host.Client(Guid.NewGuid(), role);
        var response = await client.GetAsync($"/PolicyTemplates/Details/{templateId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Contains("data-testid=\"rule-sentence\"", page, StringComparison.Ordinal);
        Assert.Contains("يُرفض الإجراء", page, StringComparison.Ordinal);
        Assert.Equal(seesJson, page.Contains("data-testid=\"technical-json\"", StringComparison.Ordinal));
        Assert.Equal(seesJson, page.Contains("\"field\"", StringComparison.Ordinal));
    }

    private static async Task<(bool Allowed, string Effects, string Page)> DecisionAsync(HttpResponseMessage response)
    {
        var page = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        var allowed = Regex.Match(page, "data-allowed=\"(?<a>true|false)\"").Groups["a"].Value == "true";
        var effects = Regex.Match(page, "data-effects=\"(?<e>[^\"]*)\"").Groups["e"].Value;
        return (allowed, effects, page);
    }

    [Theory]
    [InlineData("true", false)]
    [InlineData("false", true)]
    public async Task The_simulation_form_gives_the_decision_of_the_json_input(string hasOrgScope, bool expectAllowed)
    {
        var (_, versionId) = await EstablishmentPolicyAsync();
        using var client = _host.Client(Guid.NewGuid(), "hr-admin");
        var formPage = await client.GetStringAsync($"/PolicyTemplates/Simulate?versionId={versionId}");
        Assert.Contains("data-testid=\"fact-field\"", formPage, StringComparison.Ordinal);
        Assert.Contains("name=\"fact[hasOrgScope]\"", formPage, StringComparison.Ordinal);
        var token = WebUtility.HtmlDecode(Token.Match(formPage).Groups["t"].Value);

        var byForm = await DecisionAsync(await Hcm025ScopeAndPolicyTestHost.PostAsync(client, "/PolicyTemplates/Simulate", token,
            ("versionId", versionId.ToString("D")), ("mode", "form"), ("fact[action]", "SubmitForApproval"), ("fact[hasOrgScope]", hasOrgScope)));
        var byJson = await DecisionAsync(await Hcm025ScopeAndPolicyTestHost.PostAsync(client, "/PolicyTemplates/Simulate", token,
            ("versionId", versionId.ToString("D")), ("mode", "json"), ("factsJson", $$"""{"action":"SubmitForApproval","hasOrgScope":"{{hasOrgScope}}"}""")));

        Assert.Equal(expectAllowed, byForm.Allowed);
        Assert.Equal((byJson.Allowed, byJson.Effects), (byForm.Allowed, byForm.Effects));
    }

    [Fact]
    public async Task An_unknown_fact_is_refused_as_before()
    {
        var (_, versionId) = await EstablishmentPolicyAsync();
        using var client = _host.Client(Guid.NewGuid(), "hr-admin");
        var formPage = await client.GetStringAsync($"/PolicyTemplates/Simulate?versionId={versionId}");
        var token = WebUtility.HtmlDecode(Token.Match(formPage).Groups["t"].Value);

        var (allowed, _, page) = await DecisionAsync(await Hcm025ScopeAndPolicyTestHost.PostAsync(client, "/PolicyTemplates/Simulate", token,
            ("versionId", versionId.ToString("D")), ("mode", "json"), ("factsJson", """{"action":"SubmitForApproval","environment":"production"}""")));
        Assert.False(allowed);
        Assert.Contains("حقل غير معروف في مخطط الحقائق: environment", page, StringComparison.Ordinal);
    }
}
