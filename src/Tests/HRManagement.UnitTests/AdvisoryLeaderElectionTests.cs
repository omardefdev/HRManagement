using HR.Contracts.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

public class AdvisoryLeaderElectionTests
{
    [Fact]
    public async Task NonNpgsql_runs_action_without_lock()
    {
        var executed = false;
        await using var db = new ContractsDbContext(new DbContextOptionsBuilder<ContractsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        await db.Database.EnsureCreatedAsync();

        var ok = await HRManagement.ServiceDefaults.AdvisoryLeaderElection.TryRunAsLeaderAsync(
            db,
            lockKey: 42,
            _ =>
            {
                executed = true;
                return Task.CompletedTask;
            });
        Assert.True(ok);
        Assert.True(executed);
    }
}
