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

    // PLAN-60 (P-9): the matrix and the code agree both ways — every SeparationStep has its item with the same mode, party, owner and SLA,
    // and every item that names a step names one that exists.
    [Fact]
    public void Matrix_and_checklist_defaults_agree_both_ways()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(ResolveMatrixPath()));
        var items = doc.RootElement.GetProperty("steps").EnumerateArray()
            .Where(s => s.GetProperty("separationStep").ValueKind == JsonValueKind.String)
            .ToDictionary(s => s.GetProperty("separationStep").GetString()!, s => s);

        foreach (var step in Enum.GetValues<SeparationStep>())
        {
            Assert.True(items.TryGetValue(step.ToString(), out var item), $"{step} has no item in the matrix.");
            Assert.Equal(SeparationChecklistDefaults.ModeFor(step).ToString(), item.GetProperty("completionMode").GetString());
            Assert.Equal(SeparationChecklistDefaults.PartyFor(step),
                item.GetProperty("party").ValueKind == JsonValueKind.Null ? null : item.GetProperty("party").GetString());
            var (owner, sla) = SeparationChecklistDefaults.GovernanceFor(step);
            Assert.Equal((owner, sla), (item.GetProperty("ownerRole").GetString()!, item.GetProperty("slaHours").GetInt32()));
        }

        Assert.All(items.Keys, key => Assert.True(Enum.TryParse<SeparationStep>(key, out _), $"{key} is not a SeparationStep."));
        Assert.Equal(Enum.GetValues<SeparationStep>().Length, items.Count);
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
        // PLAN-60: a cancellation is a transition with a reason; the record stays, with its flag, and moves no further.
        Assert.True(SeparationWorkflowRules.IsCancellable(record.Status));
        SeparationWorkflowRules.Transition(record, SeparationStatus.Cancelled, Guid.NewGuid(), "اختبار");
        Assert.True(record.IsCancelled);
        Assert.Equal("اختبار", record.CancellationReason);
        Assert.False(SeparationWorkflowRules.IsCancellable(record.Status));
        Assert.Empty(SeparationWorkflowRules.NextStatuses(record.Status));
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
        Assert.False(SeparationWorkflowRules.IsEffectiveDateReached(record, DateOnly.FromDateTime(DateTime.UtcNow)));
        Assert.True(SeparationWorkflowRules.IsEffectiveDateReached(record, record.EffectiveDate));
    }

    [Fact]
    public void Compensate_contract_exists_on_separation_party()
    {
        Assert.True(typeof(IExecuteSeparationParty).GetProperty(nameof(IExecuteSeparationParty.Compensate)) is not null);
        Assert.Equal("Auth.Directory", ProcessParties.AuthDirectory);
    }
}
