namespace HRManagement.UnitTests;

/// <summary>NFR-002: out-of-order event application must converge (last-write-wins by sequence).</summary>
public class Nfr002OutOfOrderTests
{
    private sealed class Projection
    {
        public int AppliedSequence { get; private set; } = -1;
        public string State { get; private set; } = "";

        public bool TryApply(int sequence, string value)
        {
            // Ignore stale / out-of-order older sequences (idempotent convergence).
            if (sequence < AppliedSequence)
                return false;
            if (sequence == AppliedSequence)
                return State == value; // duplicate
            AppliedSequence = sequence;
            State = value;
            return true;
        }
    }

    [Fact]
    public void OutOfOrder_OlderEvents_AreIgnored_FinalStateFromHighestSequence()
    {
        var p = new Projection();
        Assert.True(p.TryApply(2, "second"));
        Assert.False(p.TryApply(1, "first")); // out of order
        Assert.True(p.TryApply(3, "third"));
        Assert.Equal(3, p.AppliedSequence);
        Assert.Equal("third", p.State);
    }

    [Fact]
    public void DuplicateSequence_SamePayload_IsIdempotent()
    {
        var p = new Projection();
        Assert.True(p.TryApply(1, "a"));
        Assert.True(p.TryApply(1, "a"));
        Assert.Equal("a", p.State);
    }

    [Fact]
    public void DuplicateSequence_ConflictingPayload_Rejected()
    {
        var p = new Projection();
        Assert.True(p.TryApply(1, "a"));
        Assert.False(p.TryApply(1, "b"));
        Assert.Equal("a", p.State);
    }
}
