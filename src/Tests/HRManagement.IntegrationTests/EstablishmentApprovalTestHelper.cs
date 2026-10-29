using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Establishment;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-25 (G-02): an establishment change is never applied on submit any more — it waits for
/// ESTABLISHMENT-CHANGE-APPROVAL. Tests that exercise the applier submit, check the pending state, then complete the
/// workflow through the real outcome handler (the approval steps themselves are covered by the workflow tests).
/// </summary>
internal static class EstablishmentApprovalTestHelper
{
    public static async Task<(bool Success, string? Error)> SubmitAndApproveAsync(
        HRCoreDbContext db,
        EstablishmentChangeLifecycleService lifecycle,
        Guid requestId,
        string? legalSourceId,
        string? decisionReference,
        Guid? actorUserId,
        string? actorUserName)
    {
        await WorkflowPackSeeder.SeedAsync(db);

        var (submitted, submitError) = await lifecycle.SubmitForApprovalAsync(
            requestId, legalSourceId, decisionReference, actorUserId, actorUserName);
        if (!submitted)
            return (false, submitError);

        var request = await db.EstablishmentChangeRequests.SingleAsync(r => r.Id == requestId);
        Assert.Equal(EstablishmentChangeStatus.PendingApproval, request.Status);
        var instance = await db.WorkflowInstances.SingleAsync(i => i.Id == request.WorkflowInstanceId);
        Assert.Equal(WorkflowStatus.InProgress, instance.Status);

        instance.Status = WorkflowStatus.Completed;
        instance.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await new EstablishmentChangeWorkflowHandler(db, lifecycle).HandleAsync(instance, approved: true, reason: null);

        var applied = await db.EstablishmentChangeRequests.AsNoTracking().SingleAsync(r => r.Id == requestId);
        return applied.Status == EstablishmentChangeStatus.Applied
            ? (true, null)
            : (false, $"Establishment change ended {applied.Status} after approval.");
    }
}
