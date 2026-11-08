using System.Globalization;
using HR.Core.Domain.Services.Policy;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Workflow;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-47 §1 (G-POL-06): every rule of the validator, run under ar-SA (the owner's machine) and en-US. A rule the evaluator
/// would not understand is refused with an Arabic message that names the JSON path.
/// </summary>
public sealed class Pol047RuleValidatorTests
{
    public static TheoryData<string> Cultures => new() { "ar-SA", "en-US" };

    private static PolicyRuleValidationContext Context(IPolicyDomainCapabilities? capabilities = null) => new()
    {
        Domain = PolicyDomains.AssetsCustody,
        Schema = new PolicyFactsSchemaDefinition
        {
            Domain = PolicyDomains.AssetsCustody,
            SubjectType = "Asset",
            Fields =
            [
                new() { Name = "isPrivileged", Type = "bool" },
                new() { Name = "value", Type = "decimal" },
                new() { Name = "count", Type = "int" },
                new() { Name = "category", Type = "string" },
                new() { Name = "action", Type = "string" }
            ]
        },
        PublishedWorkflowCodes = new HashSet<string> { "ASSET-ASSIGN-APPROVAL", "ID-CARD-ISSUANCE" },
        RetiredWorkflowCodes = new HashSet<string> { "HIRE-WIZARD", "TRANSFER-APPROVAL", "IT-TICKET", "SEPARATION-WIZARD", "RECRUITMENT-HIRE" },
        BoundWorkflowCodes = new HashSet<string> { "ASSET-ASSIGN-APPROVAL", "HIRE-WIZARD" },
        CodePolicyKeys = new HashSet<string> { "ASSET-PRIVILEGED" },
        Capabilities = capabilities ?? AllowAllPolicyDomainCapabilities.Instance
    };

    private static PolicyRuleInput Rule(string when, string then = """[{"effect":"RequireEvidence"}]""", string key = "R-TEST", int priority = 10, string? name = "اختبار") =>
        new(key, name, priority, when, then);

    private const string GoodWhen = """{"all":[{"field":"category","operator":"Equals","value":"أصول ثابتة"}]}""";

    private static IReadOnlyList<PolicyRuleIssue> Validate(string culture, PolicyRuleInput rule, PolicyRuleValidationContext? context = null) =>
        InCulture(culture, () => PolicyRuleValidator.ValidateRule(rule, context ?? Context()));

    private static T InCulture<T>(string culture, Func<T> action)
    {
        var (current, ui) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(culture);
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = current;
            CultureInfo.CurrentUICulture = ui;
        }
    }

    private static PolicyRuleIssue Single(IReadOnlyList<PolicyRuleIssue> issues, string code, string path)
    {
        var issue = Assert.Single(issues, i => i.Code == code);
        Assert.Equal(path, issue.JsonPath);
        Assert.Matches("[؀-ۿ]", issue.MessageAr);
        return issue;
    }

    [Theory, MemberData(nameof(Cultures))]
    public void A_well_formed_rule_passes(string culture)
    {
        Assert.Empty(Validate(culture, Rule(GoodWhen)));
        Assert.Empty(Validate(culture, Rule("""{"all":[{"field":"value","operator":"LessThanOrEqual","value":"10000.5"}]}""")));
        Assert.Empty(Validate(culture, Rule("""{"field":"isPrivileged","operator":"Equals","value":"true"}""")));
        Assert.Empty(Validate(culture, Rule("""{"any":[{"not":{"field":"count","operator":"GreaterThan","value":3}},{"field":"category","operator":"In","value":"a,b"}]}""")));
        Assert.Empty(Validate(culture, Rule(GoodWhen, """[{"effect":"TriggerWorkflow","params":{"workflowCode":"ASSET-ASSIGN-APPROVAL"}},{"effect":"Deny","params":{"reason":"سبب"}}]""")));
        Assert.Empty(Validate(culture, Rule(GoodWhen, """[{"effect":"SetField","params":{"field":"isPrivileged","value":"true"}},{"effect":"SelectCodePolicy","params":{"policyKey":"ASSET-PRIVILEGED"}}]""")));
    }

    [Theory, MemberData(nameof(Cultures))]
    public void An_empty_condition_is_refused(string culture)
    {
        Single(Validate(culture, Rule("{}")), "WHEN_EMPTY", "$.when");
        Single(Validate(culture, Rule("")), "WHEN_EMPTY", "$.when");
        Single(Validate(culture, Rule("""{"all":[]}""")), "WHEN_EMPTY_GROUP", "$.when.all");
        Single(Validate(culture, Rule("""{"any":[{}]}""")), "WHEN_EMPTY", "$.when.any[0]");
    }

    [Theory, MemberData(nameof(Cultures))]
    public void An_unknown_or_extra_property_is_refused(string culture)
    {
        Single(Validate(culture, Rule("""{"when":{"all":[{"field":"category","operator":"Equals","value":"x"}]}}""")), "WHEN_UNKNOWN_PROPERTY", "$.when.when");
        Single(Validate(culture, Rule("""{"all":[{"field":"category","operator":"Equals","value":"x","note":"y"}]}""")), "WHEN_UNKNOWN_PROPERTY", "$.when.all[0].note");
        Single(Validate(culture, Rule("""{"all":[{"field":"category","operator":"Equals","value":"x"}],"any":[{"field":"category","operator":"Equals","value":"y"}]}""")), "WHEN_MANY_GROUPS", "$.when");
        Single(Validate(culture, Rule("""{"all":[{"field":"category","operator":"Equals"}]}""")), "WHEN_MISSING_PROPERTY", "$.when.all[0].value");
        Single(Validate(culture, Rule("""{"all":[{"field":"category","operator":"Equals","value":"x","Value":"y"}]}""")), "WHEN_DUPLICATE_PROPERTY", "$.when.all[0].Value");
        Single(Validate(culture, Rule("""{"all":[1]}""")), "WHEN_NOT_OBJECT", "$.when.all[0]");
        Single(Validate(culture, Rule("""{"all":{"field":"category","operator":"Equals","value":"x"}}""")), "WHEN_GROUP_NOT_ARRAY", "$.when.all");
        Single(Validate(culture, Rule("not json")), "WHEN_JSON", "$.when");
    }

    [Theory, MemberData(nameof(Cultures))]
    public void An_unknown_operator_is_refused(string culture)
    {
        Single(Validate(culture, Rule("""{"all":[{"field":"value","operator":"LessThanOrEqualz","value":"1"}]}""")), "WHEN_OPERATOR", "$.when.all[0].operator");
        Single(Validate(culture, Rule("""{"all":[{"field":"value","operator":"equals","value":"1"}]}""")), "WHEN_OPERATOR", "$.when.all[0].operator");
    }

    [Theory, MemberData(nameof(Cultures))]
    public void A_field_outside_the_schema_is_refused(string culture)
    {
        Single(Validate(culture, Rule("""{"all":[{"field":"employeeName","operator":"Equals","value":"x"}]}""")), "WHEN_UNKNOWN_FIELD", "$.when.all[0].field");
        // Exact case: the evaluator reads facts by the exact name on the internal path.
        Single(Validate(culture, Rule("""{"all":[{"field":"Category","operator":"Equals","value":"x"}]}""")), "WHEN_UNKNOWN_FIELD", "$.when.all[0].field");

        var noSchema = new PolicyRuleValidationContext { Domain = PolicyDomains.AssetsCustody };
        Single(Validate(culture, Rule(GoodWhen), noSchema), "WHEN_NO_SCHEMA", "$.when.all[0].field");
    }

    [Theory, MemberData(nameof(Cultures))]
    public void A_number_must_be_numeric_and_invariant(string culture)
    {
        Single(Validate(culture, Rule("""{"all":[{"field":"category","operator":"GreaterThan","value":"5"}]}""")), "WHEN_FIELD_NOT_NUMERIC", "$.when.all[0].operator");
        Single(Validate(culture, Rule("""{"all":[{"field":"value","operator":"GreaterThan","value":"10,5"}]}""")), "WHEN_VALUE_NOT_NUMBER", "$.when.all[0].value");
        Single(Validate(culture, Rule("""{"all":[{"field":"value","operator":"GreaterThan","value":"10٫5"}]}""")), "WHEN_VALUE_NOT_NUMBER", "$.when.all[0].value");
        Single(Validate(culture, Rule("""{"all":[{"field":"value","operator":"GreaterThan","value":"١٠"}]}""")), "WHEN_VALUE_NOT_NUMBER", "$.when.all[0].value");
        Single(Validate(culture, Rule("""{"all":[{"field":"value","operator":"Equals","value":"abc"}]}""")), "WHEN_VALUE_NOT_NUMBER", "$.when.all[0].value");
        Single(Validate(culture, Rule("""{"all":[{"field":"count","operator":"GreaterThan","value":"14.5"}]}""")), "WHEN_VALUE_NOT_INTEGER", "$.when.all[0].value");
        Single(Validate(culture, Rule("""{"all":[{"field":"isPrivileged","operator":"Equals","value":"yes"}]}""")), "WHEN_VALUE_NOT_BOOL", "$.when.all[0].value");
        Single(Validate(culture, Rule("""{"all":[{"field":"category","operator":"Equals","value":{"x":1}}]}""")), "WHEN_VALUE", "$.when.all[0].value");
    }

    [Theory, MemberData(nameof(Cultures))]
    public void Depth_and_size_are_bounded(string culture)
    {
        var leaf = """{"field":"category","operator":"Equals","value":"x"}""";
        var deep = leaf;
        for (var i = 0; i < 6; i++)
            deep = $$"""{"not":{{deep}}}""";
        Assert.Contains(Validate(culture, Rule(deep)), i => i.Code == "WHEN_TOO_DEEP");

        var wide = $$"""{"any":[{{string.Join(",", Enumerable.Repeat(leaf, 51))}}]}""";
        Single(Validate(culture, Rule(wide)), "WHEN_TOO_MANY_LEAVES", "$.when");
        Assert.Empty(Validate(culture, Rule($$"""{"any":[{{string.Join(",", Enumerable.Repeat(leaf, 50))}}]}""")));
    }

    [Theory, MemberData(nameof(Cultures))]
    public void An_effect_is_a_name_with_its_required_parameters(string culture)
    {
        Single(Validate(culture, Rule(GoodWhen, "[]")), "THEN_EMPTY", "$.then");
        Single(Validate(culture, Rule(GoodWhen, "")), "THEN_EMPTY", "$.then");
        Single(Validate(culture, Rule(GoodWhen, """{"effect":"Deny"}""")), "THEN_NOT_ARRAY", "$.then");
        Single(Validate(culture, Rule(GoodWhen, """[{"effect":1}]""")), "THEN_EFFECT", "$.then[0].effect");
        Single(Validate(culture, Rule(GoodWhen, """[{"effect":"1"}]""")), "THEN_EFFECT", "$.then[0].effect");
        Single(Validate(culture, Rule(GoodWhen, """[{"effect":"Approve"}]""")), "THEN_EFFECT", "$.then[0].effect");
        Single(Validate(culture, Rule(GoodWhen, """[{"effect":"Deny"}]""")), "THEN_MISSING_PARAM", "$.then[0].params.reason");
        Single(Validate(culture, Rule(GoodWhen, $$$"""[{"effect":"Deny","params":{"reason":"{{{new string('x', 501)}}}"}}]""")), "THEN_REASON_LENGTH", "$.then[0].params.reason");
        Single(Validate(culture, Rule(GoodWhen, """[{"effect":"RequireEvidence","params":{"note":"x"}}]""")), "THEN_UNKNOWN_PARAM", "$.then[0].params.note");
        Single(Validate(culture, Rule(GoodWhen, """[{"effect":"SetField","params":{"field":"isPrivileged"}}]""")), "THEN_MISSING_PARAM", "$.then[0].params.value");
        Single(Validate(culture, Rule(GoodWhen, """[{"effect":"RequireEvidence","extra":true}]""")), "THEN_UNKNOWN_PROPERTY", "$.then[0].extra");
        Single(Validate(culture, Rule(GoodWhen, """[{"effect":"Deny","params":{"reason":5}}]""")), "THEN_PARAM_NOT_STRING", "$.then[0].params.reason");
    }

    [Theory, MemberData(nameof(Cultures))]
    public void A_workflow_must_be_published_bound_allowed_and_not_retired(string culture)
    {
        static string Trigger(string code) => $$$"""[{"effect":"TriggerWorkflow","params":{"workflowCode":"{{{code}}}"}}]""";

        Single(Validate(culture, Rule(GoodWhen, Trigger("NOPE"))), "THEN_WORKFLOW_UNKNOWN", "$.then[0].params.workflowCode");
        Single(Validate(culture, Rule(GoodWhen, Trigger("ID-CARD-ISSUANCE"))), "THEN_WORKFLOW_UNBOUND", "$.then[0].params.workflowCode");
        foreach (var retired in new[] { "HIRE-WIZARD", "TRANSFER-APPROVAL", "IT-TICKET", "SEPARATION-WIZARD", "RECRUITMENT-HIRE" })
            Single(Validate(culture, Rule(GoodWhen, Trigger(retired))), "THEN_WORKFLOW_RETIRED", "$.then[0].params.workflowCode");

        var denyAll = new DenyingCapabilities();
        var issues = Validate(culture, Rule(GoodWhen, Trigger("ASSET-ASSIGN-APPROVAL")), Context(denyAll));
        Single(issues, "THEN_EFFECT_NOT_ALLOWED", "$.then[0].effect");
        Single(issues, "THEN_WORKFLOW_NOT_ALLOWED", "$.then[0].params.workflowCode");
        Single(Validate(culture, Rule(GoodWhen, """[{"effect":"SetField","params":{"field":"isPrivileged","value":"true"}}]"""), Context(denyAll)),
            "THEN_SETFIELD_NOT_ALLOWED", "$.then[0].params.field");
    }

    [Theory, MemberData(nameof(Cultures))]
    public void A_code_policy_must_exist(string culture) =>
        Single(Validate(culture, Rule(GoodWhen, """[{"effect":"SelectCodePolicy","params":{"policyKey":"NOPE"}}]""")), "THEN_CODE_POLICY_UNKNOWN", "$.then[0].params.policyKey");

    [Theory, MemberData(nameof(Cultures))]
    public void Key_name_and_priority_are_bounded(string culture)
    {
        Single(Validate(culture, Rule(GoodWhen, key: "r-lower")), "RULE_KEY", "$.ruleKey");
        Single(Validate(culture, Rule(GoodWhen, key: "R1")), "RULE_KEY", "$.ruleKey");
        Single(Validate(culture, Rule(GoodWhen, key: "R-" + new string('A', 99))), "RULE_KEY", "$.ruleKey");
        Single(Validate(culture, Rule(GoodWhen, key: "policy-ministry-finance-v1:R-FIN")), "RULE_KEY", "$.ruleKey");
        Assert.Empty(Validate(culture, Rule(GoodWhen, key: "ACC-P47:CAT-1")));
        Single(Validate(culture, Rule(GoodWhen, name: new string('ن', 201))), "RULE_NAME", "$.name");
        Single(Validate(culture, Rule(GoodWhen, priority: 0)), "RULE_PRIORITY", "$.priority");
        Single(Validate(culture, Rule(GoodWhen, priority: 1001)), "RULE_PRIORITY", "$.priority");
        Assert.Empty(Validate(culture, Rule(GoodWhen, priority: 1000)));
    }

    [Theory, MemberData(nameof(Cultures))]
    public void A_version_refuses_a_key_used_twice_and_prefixes_each_path(string culture)
    {
        var issues = InCulture(culture, () => PolicyRuleValidator.ValidateVersion(
            [Rule(GoodWhen, key: "R-A"), Rule(GoodWhen, key: "R-A"), Rule("{}", key: "R-B")], Context()));

        Single(issues, "RULE_KEY_DUPLICATE", "$.rules[1].ruleKey");
        var empty = Single(issues, "WHEN_EMPTY", "$.rules[2].when");
        Assert.Contains("R-B", empty.MessageAr);
    }

    [Theory, MemberData(nameof(Cultures))]
    public void The_evaluator_reads_numbers_with_the_invariant_culture(string culture)
    {
        var le = new WorkflowCondition { Field = "value", Operator = "LessThanOrEqual", Value = "10000.5" };
        var gt = new WorkflowCondition { Field = "value", Operator = "GreaterThan", Value = "10000" };
        InCulture(culture, () =>
        {
            Assert.True(le.Evaluate(new() { ["value"] = "10000.5" }));
            Assert.True(le.Evaluate(new() { ["value"] = "-3" }));
            Assert.False(le.Evaluate(new() { ["value"] = "10001" }));
            Assert.False(le.Evaluate(new() { ["value"] = "10,000" }));
            Assert.True(gt.Evaluate(new() { ["value"] = "10000.25" }));
            Assert.False(gt.Evaluate(new() { ["value"] = "10000٫25" }));
            return 0;
        });
    }

    private sealed class DenyingCapabilities : IPolicyDomainCapabilities
    {
        public bool AllowsEffect(string domain, PolicyEffect effect) => effect == PolicyEffect.RequireEvidence;

        public bool AllowsSetField(string domain, string field) => false;

        public bool AllowsWorkflowCode(string domain, string workflowCode) => false;
    }
}
