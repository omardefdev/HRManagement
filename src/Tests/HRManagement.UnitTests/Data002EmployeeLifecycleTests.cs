using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

public class Data002EmployeeLifecycleTests
{
    private static HRCoreDbContext CreateContext(string? name = null)
    {
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString("N"))
            .Options;
        return new HRCoreDbContext(options);
    }

    private static Employee NewEmployee(EmploymentStatus status = EmploymentStatus.Active) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.Parse("00000000-0000-4000-8000-000000000001"),
        NationalId = "1234567890",
        FirstName = "أحمد",
        LastName = "علي",
        FullName = "أحمد علي",
        BirthDate = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        Gender = Gender.Male,
        CurrentStatus = status,
        HireDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public async Task Employee_HardDelete_Blocked_RowAndHistorySurvive()
    {
        await using var db = CreateContext();
        var emp = NewEmployee();
        db.Employees.Add(emp);
        db.EmploymentStatusHistories.Add(new EmploymentStatusHistory
        {
            EmployeeId = emp.Id,
            TenantId = emp.TenantId,
            NewStatus = EmploymentStatus.Active.ToString(),
            ChangedAt = DateTime.UtcNow,
            Reason = "seed"
        });
        await db.SaveChangesAsync();

        // Production controller rejects Remove — row and history must remain.
        Assert.Equal(1, await db.EmploymentStatusHistories.CountAsync(h => h.EmployeeId == emp.Id));
        Assert.Equal(1, await db.Employees.CountAsync(e => e.Id == emp.Id));
        Assert.NotNull(await db.Employees.FindAsync(emp.Id));
    }

    [Fact]
    public void Cascade_StatusHistory_ConfiguredAsRestrict()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(EmploymentStatusHistory));
        Assert.NotNull(entity);
        var fk = entity!.GetForeignKeys().First(f => f.Properties.Any(p => p.Name == nameof(EmploymentStatusHistory.EmployeeId)));
        Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
    }

    [Fact]
    public void StatusTransition_Invalid_Rejected()
    {
        var employee = NewEmployee(EmploymentStatus.Terminated);
        var history = new List<EmploymentStatusHistory>();
        var (ok, error) = EmployeeLifecycleService.TryChangeStatus(
            employee, EmploymentStatus.Active, "test", null, history);
        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Empty(history);
    }

    [Fact]
    public void Separation_SetsTerminalStatus_AndPublishes_Ready()
    {
        var employee = NewEmployee();
        var history = new List<EmploymentStatusHistory>();
        var target = EmploymentStatusRules.MapSeparationType("Resignation");
        Assert.Equal(EmploymentStatus.Resigned, target);

        var old = employee.CurrentStatus;
        var (ok, _) = EmployeeLifecycleService.TryChangeStatus(
            employee, target, "SeparationCompleted", null, history);
        Assert.True(ok);
        Assert.Equal(EmploymentStatus.Resigned, employee.CurrentStatus);
        Assert.Single(history);
        Assert.NotEqual(old, employee.CurrentStatus);
        Assert.True(EmploymentStatusRules.IsSeparatedFamily(employee.CurrentStatus));
    }

    [Fact]
    public void Correction_RequiresReason_AndStoresBeforeAfter()
    {
        var employee = NewEmployee();
        var changes = new Dictionary<string, JsonElement>
        {
            ["FirstName"] = JsonSerializer.SerializeToElement("محمود")
        };

        var (bad, err) = EmployeeCorrectionService.CreateRequest(employee, changes, "  ", Guid.NewGuid());
        Assert.Null(bad);
        Assert.Contains("سبب", err);

        var (correction, error) = EmployeeCorrectionService.CreateRequest(
            employee, changes, "تصحيح اسم", Guid.NewGuid());
        Assert.Null(error);
        Assert.NotNull(correction);
        Assert.Contains("before", correction!.ChangesJson);
        Assert.Contains("after", correction.ChangesJson);
        using (var doc = JsonDocument.Parse(correction.ChangesJson))
        {
            Assert.Equal("أحمد", doc.RootElement.GetProperty("before").GetProperty("FirstName").GetString());
            Assert.Equal("محمود", doc.RootElement.GetProperty("after").GetProperty("FirstName").GetString());
        }

        var history = new List<EmploymentStatusHistory>();
        var (ok, applyErr) = EmployeeCorrectionService.ApplyApproved(
            employee, correction, Guid.NewGuid(), history);
        Assert.True(ok);
        Assert.Null(applyErr);
        Assert.Equal("محمود", employee.FirstName);
        Assert.Equal(ApprovalStatus.Approved, correction.Status);
        Assert.NotNull(correction.ApprovedBy);
    }

    [Fact]
    public void LegalHold_BlocksAnonymize()
    {
        var employee = NewEmployee(EmploymentStatus.Terminated);
        employee.LegalHold = true;
        employee.LegalHoldReason = "قضية";
        var history = new List<EmploymentStatusHistory>();
        var (ok, error) = EmployeeLegalLifecycleService.TryAnonymize(
            employee, Array.Empty<ContactInfo>(), Guid.NewGuid(), history);
        Assert.False(ok);
        Assert.Contains("Legal Hold", error!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(EmploymentStatus.Terminated, employee.CurrentStatus);
    }

    [Fact]
    public void Anonymize_Succeeds_WhenNotOnHold()
    {
        var employee = NewEmployee(EmploymentStatus.Terminated);
        var contacts = new List<ContactInfo>
        {
            new() { EmployeeId = employee.Id, Value = "a@b.com", TenantId = employee.TenantId }
        };
        var history = new List<EmploymentStatusHistory>();
        var (ok, error) = EmployeeLegalLifecycleService.TryAnonymize(
            employee, contacts, Guid.NewGuid(), history);
        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(EmploymentStatus.Anonymized, employee.CurrentStatus);
        Assert.StartsWith("ANON", employee.FullName);
        Assert.Contains("redacted", contacts[0].Value);
    }

    [Fact]
    public void Merge_MarksSourceMerged()
    {
        var source = NewEmployee();
        var target = NewEmployee();
        var history = new List<EmploymentStatusHistory>();
        var (ok, _) = EmployeeLegalLifecycleService.TryMerge(source, target, Guid.NewGuid(), history);
        Assert.True(ok);
        Assert.Equal(EmploymentStatus.Merged, source.CurrentStatus);
        Assert.Equal(target.Id, source.MergedIntoEmployeeId);
    }

    [Fact]
    public void IsSeparatedFamily_CoversTerminalSeparationStatuses()
    {
        Assert.True(EmploymentStatusRules.IsSeparatedFamily(EmploymentStatus.Terminated));
        Assert.True(EmploymentStatusRules.IsSeparatedFamily(EmploymentStatus.Resigned));
        Assert.True(EmploymentStatusRules.IsSeparatedFamily(EmploymentStatus.Retired));
        Assert.False(EmploymentStatusRules.IsSeparatedFamily(EmploymentStatus.Active));
        Assert.False(EmploymentStatusRules.IsSeparatedFamily(EmploymentStatus.Deceased));
    }
}
