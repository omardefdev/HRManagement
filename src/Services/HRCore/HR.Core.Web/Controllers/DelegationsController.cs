using System.Security.Claims;
using HR.Core.Infrastructure.Data;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.SeparationOfDuties;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

/// <summary>Temporary approval delegation management (IAM-003).</summary>
[Authorize(Policy = HrManagementPolicies.ManageDelegations)]
public class DelegationsController : Controller
{
    private readonly HRCoreDbContext _db;

    public DelegationsController(HRCoreDbContext db) => _db = db;

    private static Guid? ParseUserId(ClaimsPrincipal user)
    {
        var v = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(v, out var g) ? g : null;
    }

    public async Task<IActionResult> Index(
        Guid? principalId,
        Guid? delegateId,
        string? entityTypeScope,
        bool? activeOnly,
        DateTime? from,
        DateTime? to)
    {
        var query = _db.ApprovalDelegations.AsNoTracking().AsQueryable();
        if (principalId is Guid pid)
            query = query.Where(d => d.PrincipalId == pid);
        if (delegateId is Guid did)
            query = query.Where(d => d.DelegateId == did);
        if (!string.IsNullOrWhiteSpace(entityTypeScope))
            query = query.Where(d => d.EntityTypeScope == entityTypeScope);
        if (activeOnly == true)
            query = query.Where(d => !d.IsRevoked && d.ActiveUntil >= DateTime.UtcNow && d.ActiveFrom <= DateTime.UtcNow);
        else if (activeOnly == false)
            query = query.Where(d => d.IsRevoked || d.ActiveUntil < DateTime.UtcNow || d.ActiveFrom > DateTime.UtcNow);
        if (from is DateTime f)
            query = query.Where(d => d.ActiveUntil >= f);
        if (to is DateTime t)
            query = query.Where(d => d.ActiveFrom <= t);

        var items = await query.OrderByDescending(d => d.ActiveFrom).Take(200).ToListAsync();
        return View(new DelegationsIndexViewModel
        {
            Items = items,
            PrincipalId = principalId,
            DelegateId = delegateId,
            EntityTypeScope = entityTypeScope,
            ActiveOnly = activeOnly,
            From = from,
            To = to
        });
    }

    public IActionResult Create()
    {
        var now = DateTime.UtcNow;
        return View(new ApprovalDelegationCreateViewModel
        {
            ActiveFrom = now,
            ActiveUntil = now.AddDays(7),
            Reason = string.Empty
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ApprovalDelegationCreateViewModel vm)
    {
        var principalId = ParseUserId(User);
        if (principalId is null)
        {
            ModelState.AddModelError(string.Empty, "تعذر تحديد هوية المفوِّض من التوكن");
            return View(vm);
        }

        var model = new ApprovalDelegation
        {
            PrincipalId = principalId.Value,
            DelegateId = vm.DelegateId,
            EntityTypeScope = vm.EntityTypeScope,
            RoleScope = vm.RoleScope,
            ActiveFrom = vm.ActiveFrom,
            ActiveUntil = vm.ActiveUntil,
            Reason = vm.Reason,
            IsRevoked = false
        };

        var validation = SeparationOfDutiesService.ValidateDelegation(model);
        if (!validation.Allowed)
        {
            ModelState.AddModelError(string.Empty, validation.DenialReason ?? "تفويض غير صالح");
            return View(vm);
        }

        _db.ApprovalDelegations.Add(model);
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم إنشاء التفويض المؤقت";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Revoke(Guid id)
    {
        var delegation = await _db.ApprovalDelegations.FirstOrDefaultAsync(d => d.Id == id);
        if (delegation is null) return NotFound();

        var actorId = ParseUserId(User);
        if (actorId is null)
        {
            TempData["Error"] = "تعذر تحديد هوية المستخدم من التوكن";
            return RedirectToAction(nameof(Index));
        }

        if (delegation.IsRevoked)
        {
            TempData["Warning"] = "التفويض ملغى مسبقاً";
            return RedirectToAction(nameof(Index));
        }

        if (delegation.PrincipalId != actorId.Value
            && !User.IsInRole(HrManagementRoles.Admin)
            && !User.IsInRole(HrManagementRoles.PlatformAdmin))
        {
            TempData["Error"] = "يمكن للمفوِّض أو المشرف فقط إلغاء التفويض";
            return RedirectToAction(nameof(Index));
        }

        delegation.IsRevoked = true;
        delegation.RevokedBy = actorId;
        delegation.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم إلغاء التفويض";
        return RedirectToAction(nameof(Index));
    }
}
