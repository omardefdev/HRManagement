using System.Text.Json;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Workflow;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Signatures;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.SharedKernel.Signatures;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace HRManagement.UnitTests;

/// <summary>PLAN-03 W3.1: workflow approve/reject creates SignatureRecord with definition version hash.</summary>
public class WorkflowSignatureServiceTests
{
    [Fact]
    public async Task Two_step_approve_creates_two_signature_records_matching_history()
    {
        var tenantId = Guid.NewGuid();
        TenantIdFlow.Set(tenantId);
        try
        {
            await using var db = CreateDb();
            using var sig = CreateSignatureService();
            var workflowSvc = new WorkflowSignatureService(db, sig, DesignTimeTenantContext.Instance);

        var definition = new WorkflowDefinition
        {
            Id = Guid.NewGuid(),
            Code = "TEST-WF",
            Name = "Test",
            Version = 3,
            DomainEntityType = "AssetAssignment",
            StepsJson = JsonSerializer.Serialize(new[]
            {
                new WorkflowStepSnapshot { Level = 1, Name = "Step1", StepType = WorkflowStepType.Approval, RoleRequired = "hr-core" },
                new WorkflowStepSnapshot { Level = 2, Name = "Step2", StepType = WorkflowStepType.Approval, RoleRequired = "hr-management-approver" }
            }),
            IsPublished = true
        };
        db.WorkflowDefinitions.Add(definition);

        var instance = new WorkflowInstance
        {
            TenantId = tenantId,
            DefinitionId = definition.Id,
            DefinitionVersion = definition.Version,
            EntityType = "AssetAssignment",
            EntityId = Guid.NewGuid(),
            CurrentStepLevel = 1,
            Status = WorkflowStatus.InProgress,
            InitiatedBy = Guid.NewGuid()
        };
        db.WorkflowInstances.Add(instance);
        await db.SaveChangesAsync();

        var engine = new WorkflowEngine();
        var snapshot = new WorkflowDefinitionSnapshot
        {
            Id = definition.Id,
            Code = definition.Code,
            Name = definition.Name,
            Version = definition.Version,
            DomainEntityType = definition.DomainEntityType,
            Steps = JsonSerializer.Deserialize<List<WorkflowStepSnapshot>>(definition.StepsJson) ?? []
        };

        var state = new WorkflowInstanceState
        {
            Id = instance.Id,
            CurrentStepLevel = 1,
            Status = WorkflowStatus.InProgress,
            StepHistory = []
        };

        var actor1 = Guid.NewGuid();
        var r1 = engine.ApproveStep(state, snapshot, actor1, "Checker1", "ok");
        Assert.True(r1.Success);
        Assert.NotNull(r1.HistoryEntry);
        await workflowSvc.RecordStepActionAsync(instance, definition, r1.HistoryEntry!, "approve", actor1, "hr-core");

        state.CurrentStepLevel = r1.NewStepLevel;
        state.StepHistory.Add(r1.HistoryEntry!);

        var actor2 = Guid.NewGuid();
        var r2 = engine.ApproveStep(state, snapshot, actor2, "Checker2", "ok2");
        Assert.True(r2.Success);
        Assert.NotNull(r2.HistoryEntry);
        await workflowSvc.RecordStepActionAsync(instance, definition, r2.HistoryEntry!, "approve", actor2, "hr-management-approver");

        var records = await db.SignatureRecords.AsNoTracking()
            .Where(s => s.SubjectId == instance.EntityId.ToString("D"))
            .OrderBy(s => s.OccurredAtUtc)
            .ToListAsync();

        Assert.Equal(2, records.Count);
        Assert.All(records, r => Assert.Equal("approve", r.Intent));
        Assert.All(records, r => Assert.False(string.IsNullOrWhiteSpace(r.CanonicalPayloadHash)));
        Assert.All(records, r => Assert.False(string.IsNullOrWhiteSpace(r.SignatureBase64)));
        Assert.Contains(records, r => r.SignerSubject == actor1.ToString("D"));
        Assert.Contains(records, r => r.SignerSubject == actor2.ToString("D"));
        }
        finally
        {
            TenantIdFlow.Set(null);
        }
    }

    private static SoftwareSignatureService CreateSignatureService()
    {
        var options = Options.Create(new SignatureOptions());
        return new SoftwareSignatureService(options);
    }

    private static HRCoreDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new HRCoreDbContext(options, DesignTimeTenantContext.Instance);
        db.Database.EnsureCreated();
        return db;
    }
}
