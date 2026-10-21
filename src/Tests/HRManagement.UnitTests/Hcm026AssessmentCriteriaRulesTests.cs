using HR.Core.Domain.Enums;
using HR.Core.Domain.Recruitment;
using HR.Core.Domain.Services;

namespace HRManagement.UnitTests;

public class Hcm026AssessmentCriteriaRulesTests
{
    private static AssessmentCriteriaItem Item(string key, decimal weight, decimal max = 100m) =>
        new(key, $"بند {key}", weight, max, AssessmentStage.Interview);

    [Fact]
    public void Valid_set_passes()
    {
        var items = new[] { Item("a", 40m), Item("b", 35.5m, 50m), Item("c", 24.5m, 10m) };
        Assert.Null(AssessmentCriteriaRules.Validate(items));
    }

    [Fact]
    public void Weights_not_summing_to_100_rejected()
    {
        var error = AssessmentCriteriaRules.Validate([Item("a", 40m), Item("b", 50m)]);
        Assert.NotNull(error);
        Assert.Contains("100", error);
    }

    [Fact]
    public void Weight_sum_within_tolerance_accepted()
    {
        Assert.Null(AssessmentCriteriaRules.Validate([Item("a", 33.33m), Item("b", 33.33m), Item("c", 33.33m)]));
    }

    [Fact]
    public void Duplicate_keys_rejected_case_insensitive()
    {
        var error = AssessmentCriteriaRules.Validate([Item("Exp", 50m), Item("exp", 50m)]);
        Assert.NotNull(error);
        Assert.Contains("مكرر", error);
    }

    [Fact]
    public void MaxScore_zero_rejected()
    {
        Assert.NotNull(AssessmentCriteriaRules.Validate([Item("a", 100m, 0m)]));
    }

    [Fact]
    public void Empty_list_rejected()
    {
        Assert.NotNull(AssessmentCriteriaRules.Validate([]));
        Assert.NotNull(AssessmentCriteriaRules.Validate(null));
    }

    [Fact]
    public void Zero_weight_and_long_key_rejected()
    {
        Assert.NotNull(AssessmentCriteriaRules.Validate([Item("a", 0m), Item("b", 100m)]));
        Assert.NotNull(AssessmentCriteriaRules.Validate([Item(new string('k', 65), 100m)]));
    }

    [Fact]
    public void Legacy_json_is_parsed()
    {
        var items = AssessmentCriteriaSerializer.Deserialize(
            """[{"name":"المقابلة","weight":40},{"name":"الخبرة","weight":60}]""");

        Assert.Equal(2, items.Count);
        Assert.Equal(new AssessmentCriteriaItem("item-1", "المقابلة", 40m, 100m, AssessmentStage.Other), items[0]);
        Assert.Equal(new AssessmentCriteriaItem("item-2", "الخبرة", 60m, 100m, AssessmentStage.Other), items[1]);
        Assert.Null(AssessmentCriteriaRules.Validate(items));
    }

    [Fact]
    public void Current_format_round_trips()
    {
        AssessmentCriteriaItem[] items =
        [
            new("interview", "المقابلة", 40m, 100m, AssessmentStage.Interview, true, "وصف"),
            new("exp", "الخبرة", 60m, 50m, AssessmentStage.Experience, false)
        ];

        var json = AssessmentCriteriaSerializer.Serialize(items);
        Assert.Contains("\"nameAr\"", json);
        Assert.Contains("\"Interview\"", json);
        Assert.Contains("المقابلة", json);

        var parsed = AssessmentCriteriaSerializer.Deserialize(json);
        Assert.Equal(items, parsed);
    }

    [Fact]
    public void Malformed_json_returns_empty()
    {
        Assert.Empty(AssessmentCriteriaSerializer.Deserialize("not json"));
        Assert.Empty(AssessmentCriteriaSerializer.Deserialize("{}"));
        Assert.Empty(AssessmentCriteriaSerializer.Deserialize(null));
    }
}
