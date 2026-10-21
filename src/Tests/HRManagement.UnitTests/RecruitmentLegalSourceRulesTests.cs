using HR.Core.Domain.Services;

namespace HRManagement.UnitTests;

public class RecruitmentLegalSourceRulesTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("SYR-LAW-50-2004", false)]
    public void IsEmpty_matches_expected(string? value, bool expected) =>
        Assert.Equal(expected, RecruitmentLegalSourceRules.IsEmpty(value));

    [Fact]
    public void LineageMatches_accepts_matching_or_empty_requisition()
    {
        Assert.True(RecruitmentLegalSourceRules.LineageMatches(
            "SYR-LAW-50-2004",
            "SYR-LAW-50-2004"));
        Assert.True(RecruitmentLegalSourceRules.LineageMatches(null, "SYR-LAW-50-2004"));
    }

    [Fact]
    public void LineageMatches_rejects_mismatch() =>
        Assert.False(RecruitmentLegalSourceRules.LineageMatches(
            "SYR-LAW-50-2004",
            "SYR-LAW-53-2021"));
}
