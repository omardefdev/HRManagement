using HRManagement.SharedKernel.Policy;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-67 (§5, G-UX-06, P-8): <see cref="PolicyRuleReadableRenderer"/> turns a rule's When and Then into one Arabic sentence: the
/// three groups, the eight operators, values by type, the eleven effects with their parameters. It never throws.
/// </summary>
public sealed class Ux067PolicyRuleReadableTests
{
    private static readonly PolicyFactsSchemaDefinition Schema = new()
    {
        Domain = PolicyDomains.CoreEstablishment,
        SubjectType = "EstablishmentChangeRequest",
        Fields =
        [
            new() { Name = "action", Type = "string", DisplayNameAr = "الإجراء" },
            new() { Name = "hasOrgScope", Type = "bool", DisplayNameAr = "نطاق فرع" },
            new() { Name = "durationDays", Type = "int", DisplayNameAr = "المدة" },
            new() { Name = "leaveType", Type = "string", DisplayNameAr = "النوع" },
        ]
    };

    [Fact]
    public void The_seeded_branch_rule_reads_as_one_arabic_sentence()
    {
        var rule = PolicyRuleReadableRenderer.Render(
            """{"all":[{"field":"action","operator":"Equals","value":"SubmitForApproval"},{"field":"hasOrgScope","operator":"Equals","value":"true"}]}""",
            """[{"effect":"Deny","params":{"reason":"تغييرات الملاك للموارد البشرية المركزية فقط"}}]""",
            Schema);

        Assert.False(rule.HasUnknown);
        Assert.Equal(
            "إذا جميع الشروط التالية: «الإجراء» يساوي «تقديم للاعتماد»، و«نطاق فرع» يساوي «نعم»، يُرفض الإجراء بسبب «تغييرات الملاك للموارد البشرية المركزية فقط».",
            rule.Sentence);
        Assert.DoesNotMatch("[A-Za-z]", rule.Sentence);
    }

    [Theory]
    [InlineData("Equals", "يساوي")]
    [InlineData("NotEquals", "محدد ولا يساوي")]
    [InlineData("GreaterThan", "أكبر من")]
    [InlineData("LessThan", "أصغر من")]
    [InlineData("GreaterThanOrEqual", "أكبر من أو يساوي")]
    [InlineData("LessThanOrEqual", "أصغر من أو يساوي")]
    [InlineData("Contains", "يحتوي")]
    [InlineData("In", "ضمن")]
    public void Each_operator_reads_in_arabic(string op, string word)
    {
        var rule = PolicyRuleReadableRenderer.Render($$"""{"all":[{"field":"durationDays","operator":"{{op}}","value":"30"}]}""", """[{"effect":"RequireApproval"}]""", Schema);
        Assert.Contains($"«المدة» {word} ", rule.Sentence, StringComparison.Ordinal);
        Assert.False(rule.HasUnknown);
    }

    [Fact]
    public void Any_not_and_in_read_as_groups()
    {
        var rule = PolicyRuleReadableRenderer.Render(
            """{"any":[{"field":"leaveType","operator":"In","value":"Annual,Sick"},{"not":{"field":"hasOrgScope","operator":"Equals","value":"false"}}]}""",
            """[{"effect":"RequireEvidence"},{"effect":"TriggerWorkflow","params":{"workflowCode":"ESTABLISHMENT-CHANGE-APPROVAL"}}]""",
            Schema);
        Assert.Equal(
            "إذا أيٌّ مما يلي: «النوع» ضمن («Annual»، «Sick»)، أو ليس («نطاق فرع» يساوي «لا»)، يتطلب إرفاق دليل، ويُطلق المسار «اعتماد تغيير الملاك».",
            rule.Sentence);
    }

    public static TheoryData<PolicyEffect> AllEffects()
    {
        var data = new TheoryData<PolicyEffect>();
        foreach (var e in Enum.GetValues<PolicyEffect>())
            data.Add(e);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllEffects))]
    public void Each_of_the_eleven_effects_reads_as_a_verb(PolicyEffect effect)
    {
        var parameters = effect switch
        {
            PolicyEffect.Deny => ""","params":{"reason":"سبب"}""",
            PolicyEffect.TriggerWorkflow => ""","params":{"workflowCode":"RECRUITMENT-REQUISITION-APPROVAL"}""",
            PolicyEffect.SetField => ""","params":{"field":"leaveType","value":"Annual"}""",
            PolicyEffect.SelectCodePolicy => ""","params":{"policyKey":"REQ-2026"}""",
            _ => ""
        };
        var rule = PolicyRuleReadableRenderer.Render("""{"all":[{"field":"action","operator":"Equals","value":"Create"}]}""", $$"""[{"effect":"{{effect}}"{{parameters}}}]""", Schema);
        Assert.False(rule.HasUnknown);
        Assert.Contains(PolicyVocabularyAr.Effects[effect], rule.Sentence, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"all":[{"field":"x","operator":"Wobbles","value":"1"}]}""", """[{"effect":"Allow"}]""")]
    [InlineData("""{"weird":[]}""", """[{"effect":"Allow"}]""")]
    [InlineData("not json", """[{"effect":"Allow"}]""")]
    [InlineData("""{"all":[]}""", """[{"effect":"Allow"}]""")]
    [InlineData(null, null)]
    [InlineData("""{"all":[{"field":"action","operator":"Equals","value":"Create"}]}""", """[{"effect":"Teleport"}]""")]
    [InlineData("""{"all":[{"field":"action","operator":"Equals","value":"Create"}]}""", "[42]")]
    public void An_unreadable_node_says_so_and_never_throws(string? when, string? then)
    {
        var rule = PolicyRuleReadableRenderer.Render(when, then, Schema);
        Assert.True(rule.HasUnknown);
        Assert.Contains("انظر العرض التقني", rule.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void A_field_missing_from_the_schema_is_shown_by_its_key()
    {
        var rule = PolicyRuleReadableRenderer.Render("""{"all":[{"field":"environment","operator":"Equals","value":"production"}]}""", """[{"effect":"RequireApproval"}]""", Schema);
        Assert.Contains("«environment» يساوي «production»", rule.Sentence, StringComparison.Ordinal);
        Assert.False(rule.HasUnknown);
    }

    [Fact]
    public void Deep_or_random_input_never_throws()
    {
        var deep = string.Concat(Enumerable.Repeat("""{"not":""", 40)) + """{"field":"a","operator":"Equals","value":"1"}""" + new string('}', 40);
        Assert.True(PolicyRuleReadableRenderer.Render(deep, "[]", null).HasUnknown);
        const string alphabet = "{}[]\":,afield operator value all any not 1";
        var random = new Random(67);
        for (var i = 0; i < 200; i++)
        {
            var bytes = new char[random.Next(0, 40)];
            for (var j = 0; j < bytes.Length; j++)
                bytes[j] = alphabet[random.Next(0, alphabet.Length)];
            _ = PolicyRuleReadableRenderer.Render(new string(bytes), new string(bytes), Schema);
        }
    }

    [Theory]
    [InlineData("""{"all":[{"field":"action","operator":"Equals","value":"SubmitForApproval"}]}""", false)]
    [InlineData("""{"all":[{"field":"action","operator":"equals","value":"SubmitForApproval"}]}""", true)]
    [InlineData("""{"all":[{"field":"durationDays","operator":"GreaterThan","value":"many"}]}""", true)]
    public void A_rule_the_engine_refuses_is_marked_invalid_beside_its_sentence(string when, bool invalid)
    {
        // Review (second pass): the engine's strict reading turns such a stored rule into one that never matches.
        var readable = PolicyRuleReadableRenderer.Render(when, """[{"effect":"Deny","params":{"reason":"x"}}]""", Schema);
        Assert.Equal(invalid, readable.IsInvalid);
    }
}
