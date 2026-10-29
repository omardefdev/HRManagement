using System.Security.Claims;
using HR.Core.Infrastructure.Services.Recruitment.Competitions;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HR.Core.Web.Controllers;

/// <summary>
/// HCM-031 (PLAN-31, AC-HCM-31c): anonymous marking screens for the exam marker role (<c>HrCoreExamMarking</c>). Pages
/// show exam numbers and marks only — no name, national id, application id or channel; URLs carry the exam session and
/// exam candidate ids. Reachable through the gateway rule <c>/ui/hr-core/ExamMarking</c> and the module-filter entry.
/// </summary>
[Authorize(Policy = HrManagementPolicies.HrCoreExamMarking)]
public sealed class ExamMarkingController(ExamMarkingService marking, IAuthorizationService authorization) : Controller
{
    public const string HrReadAccessWarning = ExamSeatingService.HrReadAccessWarning;

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (await ActorAsync() is not { } actor)
            return View(new ExamMarkingIndexViewModel { MissingEmployee = true });
        return View(new ExamMarkingIndexViewModel
        {
            Sessions = await marking.GetSessionsAsync(actor, ct),
            HoldsHrReadAccess = actor.HoldsHrReadAccess
        });
    }

    public async Task<IActionResult> Session(Guid id, CancellationToken ct)
    {
        if (await ActorAsync() is not { } actor)
            return RedirectToAction(nameof(Index));
        var (papers, assignment) = await marking.GetPapersAsync(id, actor, ct);
        if (assignment is null)
            return NotFound();
        return View(new ExamMarkingSessionViewModel
        {
            SessionId = id,
            SessionTitle = assignment.ExamSession!.Title,
            Role = assignment.Role,
            Locked = assignment.ExamSession.Status == HR.Core.Domain.Enums.ExamSessionStatus.MarkingClosed,
            Papers = papers
        });
    }

    public async Task<IActionResult> Paper(Guid id, Guid sessionId, CancellationToken ct)
    {
        if (await ActorAsync() is not { } actor)
            return RedirectToAction(nameof(Index));
        var (papers, assignment) = await marking.GetPapersAsync(sessionId, actor, ct);
        var paper = papers.FirstOrDefault(p => p.ExamCandidateId == id);
        if (assignment is null || paper is null)
            return NotFound();
        return View(new ExamMarkingPaperViewModel { SessionId = sessionId, Role = assignment.Role, Paper = paper });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(Guid id, decimal? score, string? note, Guid sessionId, CancellationToken ct)
    {
        if (await ActorAsync() is not { } actor)
            return RedirectToAction(nameof(Index));
        if (score is not decimal value)
        {
            TempData["Error"] = "أدخل العلامة.";
            return RedirectToAction(nameof(Paper), new { id, sessionId });
        }
        var (ok, error) = await marking.SubmitAsync(id, value, note, actor, ct);
        TempData[ok ? "Success" : "Error"] = ok ? "تم حفظ العلامة." : error;
        return ok ? RedirectToAction(nameof(Session), new { id = sessionId }) : RedirectToAction(nameof(Paper), new { id, sessionId });
    }

    private async Task<ExamMarkerActor?> ActorAsync()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var userId))
            return null;
        var employeeId = HrOrgScopeVisibility.GetEmployeeId(User);
        if (employeeId is null)
            return null;
        var hrRead = (await authorization.AuthorizeAsync(User, HrManagementPolicies.HrCoreReadOnly)).Succeeded;
        return new ExamMarkerActor(userId, employeeId, hrRead);
    }
}
