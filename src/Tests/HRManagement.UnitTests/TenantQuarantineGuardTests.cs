using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Tenancy;
using HRManagement.ServiceDefaults.Tenancy;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

public class TenantQuarantineGuardTests
{
    [Fact]
    public async Task ThrowIfQuarantined_when_open_entry_exists()
    {
        await using var db = CreateDb();
        var employeeId = Guid.NewGuid();
        db.TenantQuarantineEntries.Add(new TenantQuarantineEntry
        {
            ServiceName = TenantQuarantineGuard.ServiceName,
            TableName = "Employees",
            RecordId = employeeId,
            Reason = "orphan"
        });
        await db.SaveChangesAsync();

        var guard = new TenantQuarantineGuard(new HrCoreTenantQuarantineQuery(db));

        var ex = await Assert.ThrowsAsync<QuarantinedRecordException>(
            () => guard.ThrowIfQuarantinedAsync("Employees", employeeId));

        Assert.Equal(QuarantinedRecordException.ErrorCode, QuarantinedRecordException.ErrorCode);
        Assert.Equal(employeeId, ex.RecordId);
    }

    [Fact]
    public async Task Does_not_throw_when_resolved()
    {
        await using var db = CreateDb();
        var employeeId = Guid.NewGuid();
        db.TenantQuarantineEntries.Add(new TenantQuarantineEntry
        {
            ServiceName = TenantQuarantineGuard.ServiceName,
            TableName = "Employees",
            RecordId = employeeId,
            Reason = "orphan",
            ResolvedAt = DateTime.UtcNow,
            Resolution = "KeepPlatform: test"
        });
        await db.SaveChangesAsync();

        var guard = new TenantQuarantineGuard(new HrCoreTenantQuarantineQuery(db));
        await guard.ThrowIfQuarantinedAsync("Employees", employeeId);
    }

    private static HRCoreDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new HRCoreDbContext(options);
    }
}
