using System.Security.Claims;
using HR.Core.Domain.Recruitment;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR.Core.Web.Controllers;

/// <summary>HCM-026 (G-03): versioned assessment criteria per requisition.</summary>
[Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]
public class RecruitmentAssessmentCriteriaController(
    RecruitmentAssessmentCriteriaService criteriaService,
    IAuthorizationService authorization) : Controller
{
    public IActionResult Index(Guid requisitionId) =>
        RedirectToAction("Details", "RecruitmentRequisitions", new { id = requisitionId }, "assessment-criteria");

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Create(AssessmentCriteriaCreateForm form)
    {
        var items = new List<AssessmentCriteriaItem>();
        var index = 0;
        foreach (var row in form.Items)
        {
            index++;
            if (!InvariantDecimal.TryParse(row.Weight, out var weight)
                || !InvariantDecimal.TryParse(row.MaxScore, out var maxScore))
            {
                TempData["Error"] = $"قيمة الوزن أو الحد الأعلى غير صالحة في البند رقم {index}.";
                return RedirectToRequisition(form.RequisitionId);
            }

            items.Add(new AssessmentCriteriaItem(
                string.IsNullOrWhiteSpace(row.Key) ? $"item-{index}" : row.Key.Trim(),
                row.NameAr?.Trim() ?? string.Empty,
                weight,
                maxScore,
                row.Stage,
                row.IsMandatory,
                row.Description));
        }

        var active = await criteriaService.GetActiveAsync(form.RequisitionId);
        var canApprove = (await authorization.AuthorizeAsync(User, HrManagementPolicies.HrCoreApprove)).Succeeded;
        if (active?.LockedAt is not null && !canApprove)
        {
            TempData["Error"] = "الإصدار النشط مُقفل بعد بدء التقييم — إنشاء إصدار جديد يتطلب صلاحية الاعتماد.";
            return RedirectToRequisition(form.RequisitionId);
        }

        var (criteria, error) = await criteriaService.CreateVersionAsync(
            form.RequisitionId,
            form.Title,
            items,
            form.ChangeReason,
            ParseUserId(User),
            canApprove);

        TempData[criteria is null ? "Error" : "Success"] = error
            ?? $"تم اعتماد الإصدار v{criteria!.VersionNumber} من معايير التقييم";
        return RedirectToRequisition(form.RequisitionId);
    }

    private RedirectToActionResult RedirectToRequisition(Guid requisitionId) =>
        RedirectToAction("Details", "RecruitmentRequisitions", new { id = requisitionId }, "assessment-criteria");

    private static Guid? ParseUserId(ClaimsPrincipal user)
    {
        var v = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(v, out var g) ? g : null;
    }
}
