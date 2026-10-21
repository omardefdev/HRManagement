using System.Security.Claims;
using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HR.Core.Web.ViewModels;

namespace HR.Core.Web.Services;

public sealed class EstablishmentChangeWorkflowUiService(
    HRCoreDbContext db,
    EstablishmentScopeService establishmentScope)
{
    public async Task<EstablishmentChangeDetailsViewModel> BuildDetailsAsync(
        EstablishmentChangeRequest request,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var (instance, definition) = await WorkflowPanelDataLoader.LoadAsync(db, request.WorkflowInstanceId, ct);
        var userId = WorkflowPanelBuilder.ParseUserId(user);
        var accessScope = await establishmentScope.GetScopeAsync(user, ct);

        var workflowPanel = WorkflowPanelBuilder.Build(
            instance,
            definition,
            user,
            userId,
            new WorkflowPanelBuildOptions
            {
                IsAwaitingApproval = request.Status == EstablishmentChangeStatus.PendingApproval,
                EntityCreatedBy = request.CreatedBy,
                MissingWorkflowWarning = request.Status == EstablishmentChangeStatus.PendingApproval
                                         && request.WorkflowInstanceId is null
                    ? "لم يُنشأ مسار اعتماد لهذا الطلب. فعّل PolicyEngine:Core.Establishment أو استخدم بيئة المختبر (IsLabScale)."
                    : null,
                AdditionalBlockReason = (_, _, _) => WorkflowInboxOrgScope.GetBlockReason(accessScope, entityOrgUnitId: null)
            });

        return new EstablishmentChangeDetailsViewModel
        {
            Request = request,
            Workflow = workflowPanel
        };
    }
}
