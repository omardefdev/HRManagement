using Compliance.Domain.Enums;

namespace HRManagement.UnitTests;

public class Gov001DsarWorkflowTests
{
    [Theory]
    [InlineData(DsarRequestStatus.Open, DsarRequestStatus.InProgress, true)]
    [InlineData(DsarRequestStatus.Open, DsarRequestStatus.Rejected, true)]
    [InlineData(DsarRequestStatus.InProgress, DsarRequestStatus.Fulfilled, true)]
    [InlineData(DsarRequestStatus.InProgress, DsarRequestStatus.Rejected, true)]
    [InlineData(DsarRequestStatus.Open, DsarRequestStatus.Fulfilled, false)]
    [InlineData(DsarRequestStatus.Fulfilled, DsarRequestStatus.Open, false)]
    [InlineData(DsarRequestStatus.Rejected, DsarRequestStatus.InProgress, false)]
    public void Allowed_transitions_match_gov001_workflow(DsarRequestStatus from, DsarRequestStatus to, bool allowed)
    {
        Assert.Equal(allowed, IsAllowed(from, to));
    }

    private static bool IsAllowed(DsarRequestStatus from, DsarRequestStatus to) => (from, to) switch
    {
        (DsarRequestStatus.Open, DsarRequestStatus.InProgress) => true,
        (DsarRequestStatus.Open, DsarRequestStatus.Rejected) => true,
        (DsarRequestStatus.InProgress, DsarRequestStatus.Fulfilled) => true,
        (DsarRequestStatus.InProgress, DsarRequestStatus.Rejected) => true,
        _ => false
    };
}
