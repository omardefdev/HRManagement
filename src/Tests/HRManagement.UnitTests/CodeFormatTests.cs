using System.Globalization;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.CodeGeneration;
using HRManagement.SharedKernel.CodeGeneration;

namespace HRManagement.UnitTests;

/// <summary>PLAN-48 (§3): a code composed by a format is always read back by it, the same scope values, sequence and year.</summary>
public class CodeFormatTests
{
    private static readonly DateTime At2026 = new(2026, 11, 9, 10, 0, 0, DateTimeKind.Utc);

    private static readonly Dictionary<string, string> Facts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ParentCode"] = "01.02",
        ["OrgUnitCode"] = "01.02",
        ["GradeCode"] = "GR-05",
        ["BudgetYear"] = "2027",
        ["TypeCode"] = "LEAV",
        ["EntityPrefix"] = "HIRE",
        ["JobClassificationCode"] = "GEN"
    };

    public static TheoryData<int> SeededPolicies()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < CodeGenerationSeeder.NationalPolicies.Count; i++)
            data.Add(i);
        return data;
    }

    [Theory]
    [MemberData(nameof(SeededPolicies))]
    public void Every_seeded_policy_reads_back_what_it_composes(int index)
    {
        var policy = CodeGenerationSeeder.NationalPolicies[index];
        var format = policy.Format;
        var code = format.Compose(Facts, 37, At2026);
        if (!format.HasSequence)
        {
            Assert.Equal("01.02", code);
            Assert.False(format.TryParse(code, out _, out _, out _));
            return;
        }

        Assert.True(format.TryParse(code, out var scope, out var seq, out var year, Facts), $"{policy.Kind}: {code}");
        Assert.Equal(37, seq);
        Assert.Equal(format.ScopeValues(Facts), scope);
        Assert.Equal(format.ShowsYear ? 2026 : (int?)null, year);
    }

    [Fact]
    public void The_corrected_numbers_look_as_the_decisions_say()
    {
        var position = CodePolicyUpgrades.FundedPositionV2Json;
        Assert.Equal("01.02-GR-05-003", CodeFormat.FromJson(position, CodeSequenceReset.PerScope).Compose(Facts, 3, At2026));
        Assert.Equal("REQ-2027-00012", CodeFormat.FromJson(CodePolicyUpgrades.RecruitmentRequisitionV2Json, CodeSequenceReset.PerScope).Compose(Facts, 12, At2026));
    }

    [Fact]
    public void Known_facts_remove_the_ambiguity_of_a_field_that_holds_a_separator()
    {
        var format = CodeFormat.FromJson(CodePolicyUpgrades.FundedPositionV2Json, CodeSequenceReset.PerScope);
        var known = new Dictionary<string, string> { ["OrgUnitCode"] = "A-B", ["GradeCode"] = "GR-05" };
        Assert.True(format.TryParse("A-B-GR-05-004", out var scope, out var seq, out _, known));
        Assert.Equal(new[] { "A-B", "GR-05" }, scope);
        Assert.Equal(4, seq);

        // Without the facts the first field is read shortest-first: a different scope, which is why the reseeder passes them.
        Assert.True(format.TryParse("A-B-GR-05-004", out var guessed, out _, out _));
        Assert.Equal(new[] { "A", "B-GR-05" }, guessed);
    }

    [Theory]
    [InlineData("FP-0001")]
    [InlineData("POS-00012")]
    [InlineData("-2026-00031")]
    [InlineData("")]
    public void A_code_of_another_shape_is_not_read_as_a_position(string code)
    {
        var format = CodeFormat.FromJson(CodePolicyUpgrades.FundedPositionV2Json, CodeSequenceReset.PerScope);
        Assert.False(format.TryParse(code, out _, out _, out _));
    }

    [Theory]
    [InlineData("REQ-2026-A0001")]
    [InlineData("REQ-2026-A-CMT1")]
    [InlineData("REQ-2026-0012")]
    public void Seeded_acceptance_requisitions_are_not_read_as_generated_ones(string code)
    {
        var format = CodeFormat.FromJson(CodePolicyUpgrades.RecruitmentRequisitionV2Json, CodeSequenceReset.PerScope);
        Assert.False(format.TryParse(code, out _, out _, out _));
    }

    [Fact]
    public void A_missing_fact_is_refused_never_an_empty_segment()
    {
        var format = CodeFormat.FromJson(CodePolicyUpgrades.RecruitmentRequisitionV2Json, CodeSequenceReset.PerScope);
        var ex = Assert.Throws<CodeFactMissingException>(() => format.Compose(new Dictionary<string, string>(), 1, At2026));
        Assert.Equal("BudgetYear", ex.FactName);
        Assert.Throws<CodeFactMissingException>(() => format.Compose(new Dictionary<string, string> { ["BudgetYear"] = "  " }, 1, At2026));
    }

    [Fact]
    public void Digits_and_years_are_invariant_under_the_ar_SA_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            var format = CodeFormat.FromJson("""[{"kind":"Literal","value":"AH-"},{"kind":"Date","value":"YYYY"},{"kind":"Literal","value":"-"},{"kind":"Sequence","padWidth":6}]""", CodeSequenceReset.Yearly);
            Assert.Equal("AH-2026-000042", format.Compose(new Dictionary<string, string>(), 42, At2026));
            Assert.True(format.TryParse("AH-2026-000042", out _, out var seq, out var year));
            Assert.Equal(42, seq);
            Assert.Equal(2026, year);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void A_two_digit_year_reads_as_this_century()
    {
        var format = CodeFormat.FromJson("""[{"kind":"Literal","value":"LC"},{"kind":"Date","value":"YY"},{"kind":"Literal","value":"/"},{"kind":"Sequence","padWidth":4}]""", CodeSequenceReset.Yearly);
        Assert.Equal("LC26/0007", format.Compose(new Dictionary<string, string>(), 7, At2026));
        Assert.True(format.TryParse("LC26/0007", out _, out var seq, out var year));
        Assert.Equal((7L, 2026), (seq, year!.Value));
    }
}
