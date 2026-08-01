using HRManagement.ServiceDefaults;

namespace HRManagement.UnitTests;

public class CorrelationIdFlowTests
{
    [Fact]
    public void Set_and_Get_round_trips()
    {
        CorrelationIdFlow.Set("test-123");
        Assert.Equal("test-123", CorrelationIdFlow.Get());
    }

    [Fact]
    public void Get_returns_null_when_not_set()
    {
        CorrelationIdFlow.Set(null);
        Assert.Null(CorrelationIdFlow.Get());
    }

    [Fact]
    public async Task Values_are_isolated_across_async_contexts()
    {
        CorrelationIdFlow.Set("parent");

        string? childValue = null;
        await Task.Run(() =>
        {
            childValue = CorrelationIdFlow.Get();
            CorrelationIdFlow.Set("child");
        });

        Assert.Equal("parent", childValue);
        Assert.Equal("parent", CorrelationIdFlow.Get());
    }
}
