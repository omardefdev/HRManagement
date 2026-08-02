using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.ViewModels;

namespace HR.Core.Web.Controllers;

[Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]
public class ApprovalsController : Controller
{
    private readonly HRCoreDbContext _context;

    public ApprovalsController(HRCoreDbContext context) => _context = context;

    public async Task<IActionResult> Index(SearchFilterBase? filter, string? status, string? entityType)
    {
        filter ??= new SearchFilterBase();
        filter.NormalizePaging();

        var query = _context.ApprovalRequests
            .Include(r => r.Steps)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ApprovalStatus>(status, out var s))
            query = query.Where(r => r.Status == s);

        if (!string.IsNullOrWhiteSpace(entityType))
            query = query.Where(r => r.EntityType == entityType);

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var term = filter.SearchTerm.Trim();
            query = query.Where(r =>
                (r.Description != null && r.Description.Contains(term)) ||
                (r.RequestedByName != null && r.RequestedByName.Contains(term)));
        }

        query = query.OrderByDescending(r => r.RequestedAt);

        var count = await query.CountAsync();
        var items = await query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

        var vm = new ApprovalListViewModel
        {
            Requests = new PaginatedList<ApprovalRequest>(items, count, filter.Page, filter.PageSize),
            Filter = filter,
            StatusFilter = status,
            EntityTypeFilter = entityType
        };

        return View(vm);
    }

    public async Task<IActionResult> Pending()
    {
        var requests = await _context.ApprovalRequests
            .Include(r => r.Steps)
            .Where(r => r.Status == ApprovalStatus.Pending)
            .OrderBy(r => r.RequestedAt)
            .Take(SearchFilterBase.MaxPageSize)
            .AsNoTracking()
            .ToListAsync();

        return View(requests);
    }

    public async Task<IActionResult> Review(Guid id)
    {
        var request = await _context.ApprovalRequests
            .Include(r => r.Steps.OrderBy(s => s.Level))
            .FirstOrDefaultAsync(r => r.Id == id);

        if (request == null) return NotFound();

        var vm = new ApprovalReviewViewModel
        {
            Id = request.Id,
            EntityType = request.EntityType,
            EntityId = request.EntityId,
            Description = request.Description,
            RequestedByName = request.RequestedByName,
            RequestedAt = request.RequestedAt,
            Status = request.Status,
            CurrentLevel = request.CurrentLevel,
            Steps = request.Steps.OrderBy(s => s.Level).ToList()
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreApprove)]
    public async Task<IActionResult> Approve(Guid id, string? reviewComments)
    {
        var request = await _context.ApprovalRequests
            .Include(r => r.Steps)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (request == null) return NotFound();
        if (request.Status != ApprovalStatus.Pending)
        {
            TempData["Error"] = "هذا الطلب ليس في حالة انتظار";
            return RedirectToAction(nameof(Index));
        }

        var userId = ParseUserId(User);
        var userName = User.FindFirstValue("preferred_username") ?? User.Identity?.Name ?? "مجهول";

        if (!ApprovalWorkflowService.TryApproveStep(request, userId, userName, reviewComments, out var approveError))
        {
            TempData["Error"] = approveError;
            return RedirectToAction(nameof(Index));
        }

        if (request.Status == ApprovalStatus.Approved)
            ApprovalMetrics.RecordApproved(request.EntityType, fullyApproved: true);
        else
        {
            ApprovalMetrics.RecordApproved(request.EntityType, fullyApproved: false);
            ApprovalMetrics.RecordEscalated(request.EntityType);
        }

        if (request.Status == ApprovalStatus.Approved && request.EntityType == "ApprovedCV")
        {
            var cv = await _context.ApprovedCVs.FindAsync(request.EntityId);
            if (cv is not null)
            {
                cv.ApprovedBy = userId;
                cv.ApprovedAt = DateTime.UtcNow;
            }
        }

        await _context.SaveChangesAsync();

        TempData["Success"] = "تمت الموافقة على الطلب بنجاح";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreApprove)]
    public async Task<IActionResult> Reject(Guid id, string? reviewComments)
    {
        var request = await _context.ApprovalRequests
            .Include(r => r.Steps)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (request == null) return NotFound();
        if (request.Status != ApprovalStatus.Pending)
        {
            TempData["Error"] = "هذا الطلب ليس في حالة انتظار";
            return RedirectToAction(nameof(Index));
        }

        var userId = ParseUserId(User);
        var userName = User.FindFirstValue("preferred_username") ?? User.Identity?.Name ?? "مجهول";

        if (!ApprovalWorkflowService.TryReject(request, userId, userName, reviewComments, out var rejectError))
        {
            TempData["Error"] = rejectError;
            return RedirectToAction(nameof(Index));
        }

        ApprovalMetrics.RecordRejected(request.EntityType);

        await _context.SaveChangesAsync();

        TempData["Success"] = "تم رفض الطلب";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var request = await _context.ApprovalRequests.FindAsync(id);
        if (request == null) return NotFound();

        if (request.Status != ApprovalStatus.Pending)
        {
            TempData["Error"] = "لا يمكن إلغاء طلب غير معلق";
            return RedirectToAction(nameof(Index));
        }

        request.Status = ApprovalStatus.Cancelled;
        await _context.SaveChangesAsync();

        TempData["Success"] = "تم إلغاء الطلب";
        return RedirectToAction(nameof(Index));
    }

    public static ApprovalRequest CreateApprovalRequest(
        string entityType,
        Guid entityId,
        string? description,
        Guid? requestedBy,
        string? requestedByName,
        string? payloadJson = null,
        string[]? requiredRoles = null,
        decimal? financialAmount = null)
    {
        ApprovalMetrics.RecordSubmitted(entityType);
        return ApprovalWorkflowService.CreateApprovalRequest(
            entityType, entityId, description, requestedBy, requestedByName, payloadJson, requiredRoles, financialAmount);
    }

    private static Guid? ParseUserId(ClaimsPrincipal user)
    {
        var v = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(v, out var g) ? g : null;
    }
}
