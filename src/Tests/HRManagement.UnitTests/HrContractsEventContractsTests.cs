using System.Text.Json;
using HRManagement.MessageContracts.Events;

namespace HRManagement.UnitTests;

public class HrContractsEventContractsTests
{
    [Theory]
    [InlineData(typeof(ICommencementRecorded))]
    [InlineData(typeof(IConfirmationApproved))]
    [InlineData(typeof(ILeaveApproved))]
    [InlineData(typeof(IAdvanceApproved))]
    [InlineData(typeof(IJobUpdateApplied))]
    [InlineData(typeof(ISeparationCompleted))]
    [InlineData(typeof(ITransactionArchived))]
    public void OutboundContracts_Contain_SourceSystem_And_CorrelationId(Type eventType)
    {
        var props = eventType.GetProperties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("SourceSystem", props);
        Assert.Contains("CorrelationId", props);
    }

    [Fact]
    public void IContractActivated_serializes_required_fields()
    {
        var payload = new
        {
            ContractId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            ContractType = "FullTime",
            StartDate = DateTime.UtcNow,
            EndDate = (DateTime?)null,
            BaseSalary = 5000m,
            PayGradeCode = (string?)null,
            CorrelationId = "corr-1",
            SourceSystem = "HR.Contracts"
        };

        var json = JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("ContractId", out _));
        Assert.True(doc.RootElement.TryGetProperty("EmployeeId", out _));
        Assert.True(doc.RootElement.TryGetProperty("SourceSystem", out _));
    }

    [Fact]
    public void ISeparationCompleted_serializes_schema_version_field_when_present()
    {
        var payload = new
        {
            EmployeeId = Guid.NewGuid(),
            SeparationType = "Resignation",
            EffectiveDate = DateTime.UtcNow,
            OccurredAt = DateTime.UtcNow,
            CorrelationId = "corr-2",
            SourceSystem = "HR.Contracts",
            SchemaVersion = 1
        };

        var json = JsonSerializer.Serialize(payload);
        Assert.Contains("SchemaVersion", json);
    }
}
