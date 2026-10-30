namespace HRManagement.UnitTests;

// PLAN-35 (P7, AC-QA-35): deliberately red. This branch only proves that a failing test turns the required "CI" check
// red on a pull request; the pull request is closed unmerged and the branch deleted.
public class Plan35NegativeCheckTests
{
    [Fact]
    public void Negative_check_must_fail_ci()
    {
        Assert.Fail("PLAN-35 negative check: this branch must never merge.");
    }
}
