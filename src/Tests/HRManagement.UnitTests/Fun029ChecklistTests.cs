using HR.Contracts.Application.Features.HireCases;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HR.Contracts.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using static HRManagement.UnitTests.Fun029TestSupport;

namespace HRManagement.UnitTests;

/// <summary>FUN-029 (I, D-6): appointment decision and optional pre-employment medical in the hire checklist.</summary>
public class Fun029ChecklistTests
{
    private static async Task<ServiceTransaction> HireAsync(
        HR.Contracts.Infrastructure.Data.ContractsDbContext db,
        HireChecklistOptions? options = null)
    {
        await SeedContractTypeAsync(db, "DEFAULT");
        var employeeId = Guid.NewGuid();
        await CreateHandler(db, checklist: options).Handle(V4Command(employeeId), CancellationToken.None);
        return await db.ServiceTransactions.SingleAsync(t => t.EmployeeId == employeeId);
    }

    [Theory]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, true, true)]
    [InlineData(false, false, false, false)]
    public async Task Seeded_steps_follow_the_options(bool decision, bool medical, bool expectDecision, bool expectMedical)
    {
        await using var db = CreateDb();
        var txn = await HireAsync(db, new HireChecklistOptions { RequireAppointmentDecision = decision, RequireMedicalFitness = medical });

        var steps = await db.HireChecklists.Where(c => c.ServiceTransactionId == txn.Id).Select(c => c.Step).ToListAsync();

        Assert.Equal(expectDecision, steps.Contains(HireStep.AppointmentDecisionIssued));
        Assert.Equal(expectMedical, steps.Contains(HireStep.PreEmploymentMedical));
        Assert.All(HireChecklistDefaults.BaseSteps, s => Assert.Contains(s, steps));
    }

    [Fact]
    public void Defaults_and_governance_of_the_new_steps()
    {
        Assert.Contains(HireStep.AppointmentDecisionIssued, HireChecklistDefaults.DefaultSteps);
        Assert.DoesNotContain(HireStep.PreEmploymentMedical, HireChecklistDefaults.DefaultSteps);
        Assert.Equal(("hr-contracts", 48, true), HireChecklistDefaults.GovernanceFor(HireStep.AppointmentDecisionIssued));
        Assert.Equal(("hr-health", 120, true), HireChecklistDefaults.GovernanceFor(HireStep.PreEmploymentMedical));
        Assert.Equal(7, (int)HireStep.AppointmentDecisionIssued);
        Assert.Equal(8, (int)HireStep.PreEmploymentMedical);
    }

    [Fact]
    public async Task Issuing_the_decision_is_idempotent_and_marks_the_step()
    {
        await using var db = CreateDb();
        var txn = await HireAsync(db);
        var handler = new IssueAppointmentDecisionHandler(new ContractsRepository(db), CodeGenerationTestSupport.CreateClient());
        var actor = Guid.NewGuid();

        var first = await handler.Handle(new IssueAppointmentDecisionCommand(txn.Id, "سارة المرشحة", actor, "hr.contracts"), CancellationToken.None);
        var second = await handler.Handle(new IssueAppointmentDecisionCommand(txn.Id, "سارة المرشحة", actor, "hr.contracts"), CancellationToken.None);

        Assert.True(first.Success, first.Error);
        Assert.False(first.AlreadyIssued);
        Assert.True(second.AlreadyIssued);
        Assert.Equal(first.Decision!.Id, second.Decision!.Id);
        Assert.Equal(1, await db.AdministrativeDecisions.CountAsync());

        var decision = await db.AdministrativeDecisions.SingleAsync();
        Assert.StartsWith("قرار تعيين: سارة المرشحة — ", decision.Subject);
        Assert.Equal("hr.contracts", decision.IssuedBy);
        Assert.Equal(txn.EmployeeId, decision.RelatedEmployeeId);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), decision.IssuedDate);
        Assert.Equal(decision.Id, (await db.ServiceTransactions.SingleAsync(t => t.Id == txn.Id)).AppointmentDecisionId);

        var step = await db.HireChecklists.SingleAsync(c => c.ServiceTransactionId == txn.Id && c.Step == HireStep.AppointmentDecisionIssued);
        Assert.True(step.IsCompleted);
        Assert.Equal(decision.DecisionNumber, step.EvidenceRef);
        Assert.Equal(actor, step.CompletedBy);
    }

    [Fact]
    public async Task Completion_is_blocked_until_the_decision_and_enabled_medical_are_done()
    {
        await using var db = CreateDb();
        var txn = await HireAsync(db, new HireChecklistOptions { RequireAppointmentDecision = true, RequireMedicalFitness = true });
        txn.MoveTo(ServiceTransactionState.PendingApproval, null);
        txn.MoveTo(ServiceTransactionState.Approved, null);
        txn.MoveTo(ServiceTransactionState.Executing, null);
        var checklist = await db.HireChecklists.Where(c => c.ServiceTransactionId == txn.Id).ToListAsync();
        foreach (var item in checklist.Where(c => c.Step is not (HireStep.AppointmentDecisionIssued or HireStep.PreEmploymentMedical)))
            HireWorkflowRules.MarkStepCompleted(item, "test", "test:evidence");

        Assert.False(HireWorkflowRules.CanPublishHireCompleted(txn, checklist));
        Assert.False(HireWorkflowRules.CanAdvanceToCompleted(checklist));

        HireWorkflowRules.MarkStepCompleted(checklist.Single(c => c.Step == HireStep.AppointmentDecisionIssued), null, "DEC-APPT-2026-00001");
        Assert.False(HireWorkflowRules.CanPublishHireCompleted(txn, checklist));

        var medical = checklist.Single(c => c.Step == HireStep.PreEmploymentMedical);
        Assert.Throws<InvalidOperationException>(() => HireWorkflowRules.MarkStepCompleted(medical));
        HireWorkflowRules.MarkStepCompleted(medical, null, "MED-2026-77");
        Assert.True(HireWorkflowRules.CanPublishHireCompleted(txn, checklist));
    }

    [Fact]
    public async Task Medical_step_is_completed_with_evidence_and_the_completing_user()
    {
        await using var db = CreateDb();
        var txn = await HireAsync(db, new HireChecklistOptions { RequireMedicalFitness = true });
        var user = Guid.NewGuid();

        var (ok, error) = await new CompleteHireChecklistStepHandler(new ContractsRepository(db)).Handle(
            new CompleteHireChecklistStepCommand(txn.Id, HireStep.PreEmploymentMedical, "الفحص الطبي قبل التعيين", "MED-2026-77", user, "hr.health"),
            CancellationToken.None);

        Assert.True(ok, error);
        var step = await db.HireChecklists.SingleAsync(c => c.ServiceTransactionId == txn.Id && c.Step == HireStep.PreEmploymentMedical);
        Assert.True(step.IsCompleted);
        Assert.Equal("MED-2026-77", step.EvidenceRef);
        Assert.Equal("hr.health", step.CompletedByName);
    }
}
