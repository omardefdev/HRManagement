using IT.Infrastructure.Domain.Entities;
using IT.Infrastructure.Domain.Services;

namespace HRManagement.UnitTests;

/// <summary>DEC-001: runtime decommission workflow rules.</summary>
public class Dec001DecommissionWorkflowTests
{
    [Fact]
    public void Write_rejected_after_write_freeze()
    {
        var system = new LegacySystemRecord
        {
            SystemName = "LegacyHR",
            CutoffDate = DateTime.UtcNow.AddDays(-1),
            Phase = "WriteFrozen"
        };

        Assert.Throws<InvalidOperationException>(() => system.EnsureWritableOrThrow());
    }

    [Fact]
    public void Checklist_phase_advances_system_phase()
    {
        var system = new LegacySystemRecord
        {
            SystemName = "LegacyHR",
            CutoffDate = DateTime.UtcNow.AddDays(-1),
            Phase = "Active"
        };

        DecommissionWorkflow.ApplyChecklistPhase(system, "DC-01");
        Assert.Equal("WriteFrozen", system.Phase);

        DecommissionWorkflow.ApplyChecklistPhase(system, "DC-02");
        Assert.Equal("ReadOnly", system.Phase);

        DecommissionWorkflow.ApplyChecklistPhase(system, "DC-03");
        Assert.Equal("Archived", system.Phase);
    }

    [Fact]
    public void Cannot_regress_phase()
    {
        var system = new LegacySystemRecord
        {
            SystemName = "LegacyHR",
            CutoffDate = DateTime.UtcNow,
            Phase = "Archived"
        };

        Assert.Throws<InvalidOperationException>(() => system.AdvanceTo("Active"));
    }

    [Fact]
    public void Shadow_check_clean_when_zero_findings()
    {
        var check = new ShadowOperationCheck { FindingsCount = 0 };
        Assert.True(check.IsClean);
    }

    [Fact]
    public void Shadow_check_allows_documented_exception()
    {
        var check = new ShadowOperationCheck
        {
            FindingsCount = 1,
            Exceptions = "Signed temporary spreadsheet for 30 days"
        };
        Assert.True(check.IsClean);
    }
}
