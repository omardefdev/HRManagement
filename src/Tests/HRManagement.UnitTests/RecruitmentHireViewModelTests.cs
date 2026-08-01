using HR.Core.Web.ViewModels;

namespace HRManagement.UnitTests;

public class RecruitmentHireViewModelTests
{
    [Theory]
    [InlineData("أحمد", "أحمد", null, "أحمد")]
    [InlineData("أحمد علي", "أحمد", null, "علي")]
    [InlineData("أحمد محمد علي", "أحمد", "محمد", "علي")]
    public void SplitApplicantName_ParsesCorrectly(string input, string first, string? middle, string last)
    {
        var (f, m, l) = RecruitmentHireViewModel.SplitApplicantName(input);
        Assert.Equal(first, f);
        Assert.Equal(middle, m);
        Assert.Equal(last, l);
    }
}
