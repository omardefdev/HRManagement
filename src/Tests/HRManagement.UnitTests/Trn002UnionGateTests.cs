using Compliance.Domain.Services;

namespace HRManagement.UnitTests;

/// <summary>TRN-002: runtime wave gate and collective grievance rules.</summary>
public class Trn002UnionGateTests
{
    [Fact]
    public void Wave_blocked_without_engagement_or_exemption()
    {
        Assert.False(UnionEngagementGate.CanStartWave(false, false, []));
    }

    [Fact]
    public void Wave_allowed_with_engagement_and_no_open_objections()
    {
        Assert.True(UnionEngagementGate.CanStartWave(true, false, ["Agreed"]));
    }

    [Fact]
    public void Wave_blocked_when_objection_pending()
    {
        Assert.False(UnionEngagementGate.CanStartWave(true, false, ["Pending"]));
    }

    [Fact]
    public void Wave_allowed_with_signed_exemption()
    {
        Assert.True(UnionEngagementGate.CanStartWave(false, true, []));
    }

    [Fact]
    public void Collective_grievance_cannot_route_to_individual()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            UnionEngagementGate.EnsureCollectiveNotRoutedToIndividual(true));
        Assert.Contains("TRN-002", ex.Message);
    }
}
