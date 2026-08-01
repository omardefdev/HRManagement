using Payroll.Domain.Services;

namespace HRManagement.UnitTests;

public class MedicalCompensationRulesTests
{
    [Fact]
    public void IsCompensationRecommendation_RecognizesType() =>
        Assert.True(MedicalCompensationRules.IsCompensationRecommendation("Compensation"));

    [Fact]
    public void ResolveAmount_ParsesFromDetails() =>
        Assert.Equal(1500m, MedicalCompensationRules.ResolveAmount("تعويض 1500 دينار", 100m));

    [Fact]
    public void ResolveAmount_FallsBackToDefault() =>
        Assert.Equal(100m, MedicalCompensationRules.ResolveAmount(null, 100m));

    [Fact]
    public void BuildAdjustmentReason_ContainsRecommendationId()
    {
        var id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var reason = MedicalCompensationRules.BuildAdjustmentReason(id, "test");
        Assert.Contains(id.ToString(), reason);
    }
}
