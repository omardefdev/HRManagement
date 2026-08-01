using System.Text.Json;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HRManagement.MessageContracts.Commands;
using HRManagement.ServiceDefaults;

namespace HRManagement.UnitTests;

public class Fun002JourneyMatrixTests
{
    private static string ResolveMatrixPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "fun-separation", "journey-matrix.json");
            if (System.IO.File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("docs/fun-separation/journey-matrix.json");
    }

    [Fact]
    public void Matrix_includes_mandatory_payroll_and_auth()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(ResolveMatrixPath()));
        var steps = doc.RootElement.GetProperty("steps").EnumerateArray().ToList();
        Assert.Contains(steps, s => s.GetProperty("id").GetString() == "PayrollSettled"
                                    && s.GetProperty("mandatory").GetBoolean());
        Assert.Contains(steps, s => s.GetProperty("id").GetString() == "AccountDisabled"
                                    && s.GetProperty("service").GetString() == "Auth.Directory");
    }
}

public class Fun002ChecklistGovernanceTests
{
    [Fact]
    public void Defaults_set_owner_and_sla()
    {
        var item = SeparationChecklistDefaults.CreateForRecord(Guid.NewGuid())
            .First(c => c.Step == SeparationStep.PayrollSettled);
        Assert.Equal("payroll", item.OwnerRole);
        Assert.Equal(72, item.SlaHours);
    }

    [Fact]
    public void SoftCancel_retains_record_flags()
    {
        var record = new SeparationRecord
        {
            EmployeeId = Guid.NewGuid(),
            EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            SeparationType = SeparationType.Resignation
        };
        Assert.True(SeparationWorkflowRules.CanCancel(record));
        SeparationWorkflowRules.SoftCancel(record, "اختبار");
        Assert.True(record.IsCancelled);
        Assert.False(SeparationWorkflowRules.CanCancel(record));
        Assert.False(SeparationWorkflowRules.CanComplete(record, []));
    }

    [Fact]
    public void Future_effective_date_blocks_publish_gate()
    {
        var record = new SeparationRecord
        {
            EmployeeId = Guid.NewGuid(),
            EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)),
            SeparationType = SeparationType.Resignation
        };
        Assert.False(SeparationWorkflowRules.IsEffectiveDateReached(record));
        Assert.True(SeparationWorkflowRules.IsEffectiveDateReached(record, record.EffectiveDate));
    }

    [Fact]
    public void Compensate_contract_exists_on_separation_party()
    {
        Assert.True(typeof(IExecuteSeparationParty).GetProperty(nameof(IExecuteSeparationParty.Compensate)) is not null);
        Assert.Equal("Auth.Directory", ProcessParties.AuthDirectory);
    }
}
