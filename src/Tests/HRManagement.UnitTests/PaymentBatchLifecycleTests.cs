using Payroll.Domain.Enums;

namespace HRManagement.UnitTests;

/// <summary>
/// A payment batch must not be able to reach a paid state without passing through the banking
/// cycle. Previously the pay action set <c>Processed</c> directly in the database, so the system
/// reported salaries as paid while no transfer had been requested from any bank.
/// </summary>
public class PaymentBatchLifecycleTests
{
    [Fact]
    public void Pending_CannotJumpStraightToSettled()
    {
        Assert.False(PaymentBatchTransitions.CanTransition(
            PaymentBatchStatus.Pending, PaymentBatchStatus.Settled));
    }

    [Fact]
    public void Pending_CannotJumpStraightToProcessed()
    {
        Assert.False(PaymentBatchTransitions.CanTransition(
            PaymentBatchStatus.Pending, PaymentBatchStatus.Processed));
    }

    [Fact]
    public void HappyPath_FollowsSubmitAckSettle()
    {
        Assert.True(PaymentBatchTransitions.CanTransition(
            PaymentBatchStatus.Pending, PaymentBatchStatus.Submitted));
        Assert.True(PaymentBatchTransitions.CanTransition(
            PaymentBatchStatus.Submitted, PaymentBatchStatus.Acked));
        Assert.True(PaymentBatchTransitions.CanTransition(
            PaymentBatchStatus.Acked, PaymentBatchStatus.Settled));
    }

    [Theory]
    [InlineData(PaymentBatchStatus.Submitted)]
    [InlineData(PaymentBatchStatus.Acked)]
    public void ChannelMayRejectBeforeSettlement(PaymentBatchStatus from)
    {
        Assert.True(PaymentBatchTransitions.CanTransition(from, PaymentBatchStatus.Rejected));
    }

    [Fact]
    public void SettledFundsMayBeReturned()
    {
        Assert.True(PaymentBatchTransitions.CanTransition(
            PaymentBatchStatus.Settled, PaymentBatchStatus.Returned));
    }

    [Fact]
    public void ReversedIsTerminal()
    {
        foreach (var target in Enum.GetValues<PaymentBatchStatus>())
            Assert.False(PaymentBatchTransitions.CanTransition(PaymentBatchStatus.Reversed, target));
    }

    [Theory]
    [InlineData(PaymentBatchStatus.Pending, false)]
    [InlineData(PaymentBatchStatus.Submitted, false)]
    [InlineData(PaymentBatchStatus.Acked, false)]
    [InlineData(PaymentBatchStatus.Rejected, false)]
    [InlineData(PaymentBatchStatus.Returned, false)]
    [InlineData(PaymentBatchStatus.Settled, true)]
    public void OnlySettlementCountsAsPaid(PaymentBatchStatus status, bool expected)
    {
        Assert.Equal(expected, PaymentBatchTransitions.IsPaid(status));
    }

    [Fact]
    public void RejectedBatchMayBeCorrectedAndResubmitted()
    {
        Assert.True(PaymentBatchTransitions.CanTransition(
            PaymentBatchStatus.Rejected, PaymentBatchStatus.Submitted));
    }
}
