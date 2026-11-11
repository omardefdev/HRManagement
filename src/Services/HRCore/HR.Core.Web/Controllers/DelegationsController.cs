using System.Security.Claims;
using System.Text.Json;
using HR.Core.Infrastructure.Data;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Delegation;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.SeparationOfDuties;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

/// <summary>
/// Temporary approval delegation management (IAM-003). PLAN-50 (§5): the delegate is an employee of the directory, the role is one of the
/// user's own delegable roles (D-50-1), the entity type one of the closed list, at most 90 days; each refusal answers 400 with its Arabic
/// message, and every creation and revocation writes an audit row. A user sees what he delegated and what was delegated to him; an
/// administrator or a platform operator sees the tenant's.
/// </summary>
[Authorize(Policy = HrManagementPolicies.ManageDelegations)]
public class DelegationsController(HRCoreDbContext db, IConfiguration configuration, ITenantContext tenantContext) : Controller
{
    private const int MaxRevokeReason = 500;

    private static Guid? ParseUserId(ClaimsPrincipal user) => DelegationIdentity.UserId(user);

    private bool SeesAll => User.IsInRole(HrManagementRoles.Admin) || User.IsInRole(HrManagementRoles.PlatformAdmin);

    /// <summary>The roles of the user's own token (never a delegated one: a delegation reaches no token).</summary>
    private IReadOnlyList<string> DirectRoles() =>
        User.Claims
            .Where(c => c.Type == ClaimTypes.Role || c.Type == "role" || (c.Subject is { } identity && c.Type == identity.RoleClaimType))
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private IReadOnlyList<string> DirectDelegableRoles() =>
        DelegationOptions.DelegableRoles(configuration)
            .Where(r => DirectRoles().Contains(r, StringComparer.OrdinalIgnoreCase))
            .ToList();

    public async Task<IActionResult> Index(
        Guid? principalId,
        Guid? delegateId,
        string? entityTypeScope,
        bool? activeOnly,
        DateTime? from,
        DateTime? to)
    {
        var me = ParseUserId(User);
        var myEmployee = DelegationIdentity.EmployeeId(User);
        var query = db.ApprovalDelegations.AsNoTracking().AsQueryable();
        if (!SeesAll)
        {
            if (me is null)
                return Forbid();
            query = query.Where(d => d.PrincipalId == me
                                     || (myEmployee != null && d.DelegateEmployeeId == myEmployee)
                                     || (d.DelegateEmployeeId == null && d.DelegateId == me));
        }

        if (principalId is Guid pid)
            query = query.Where(d => d.PrincipalId == pid);
        if (delegateId is Guid did)
            query = query.Where(d => d.DelegateId == did || d.DelegateEmployeeId == did);
        if (!string.IsNullOrWhiteSpace(entityTypeScope))
            query = query.Where(d => d.EntityTypeScope == entityTypeScope);
        var now = DateTime.UtcNow;
        if (activeOnly == true)
            query = query.Where(d => !d.IsRevoked && d.ActiveUntil >= now && d.ActiveFrom <= now);
        else if (activeOnly == false)
            query = query.Where(d => d.IsRevoked || d.ActiveUntil < now || d.ActiveFrom > now);
        if (from is DateTime fromValue)
        {
            var f = Utc(fromValue);
            query = query.Where(d => d.ActiveUntil >= f);
        }
        if (to is DateTime toValue)
        {
            var t = Utc(toValue);
            query = query.Where(d => d.ActiveFrom <= t);
        }

        var items = await query.OrderByDescending(d => d.ActiveFrom).Take(200).ToListAsync();
        return View(new DelegationsIndexViewModel
        {
            Items = items,
            SeesAll = SeesAll,
            CurrentUserId = me,
            CurrentEmployeeId = myEmployee,
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
        return View(WithLists(new ApprovalDelegationCreateViewModel
        {
            ActiveFrom = now,
            ActiveUntil = now.AddDays(7),
            Reason = string.Empty
        }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ApprovalDelegationCreateViewModel vm)
    {
        var principalId = ParseUserId(User);
        if (principalId is null)
            return Refused(vm, "تعذر تحديد هوية المفوِّض من التوكن");
        if (!ModelState.IsValid)
            return Refused(vm, null);

        // P-5: a delegate never gets a wider scope than his principal had; a branch-scoped principal with an unusable claim delegates nothing.
        var scope = HrOrgScopeVisibility.GetScopeClaimResult(User);
        if (scope.Kind == OrgScopeClaimKind.InvalidClaim)
            return Refused(vm, "نطاق المفوِّض في التوكن غير صالح، فلا يمكنه التفويض.");

        var delegateName = await db.Employees.AsNoTracking()
            .Where(e => e.Id == vm.DelegateEmployeeId)
            .Select(e => e.FullName)
            .FirstOrDefaultAsync();
        if (delegateName is null)
            return Refused(vm, "الموظف المفوَّض غير موجود في دليل الجهة.");

        var model = new ApprovalDelegation
        {
            PrincipalId = principalId.Value,
            PrincipalEmployeeId = DelegationIdentity.EmployeeId(User),
            PrincipalName = BoundedName(User.FindFirstValue("name") ?? User.FindFirstValue("preferred_username") ?? User.Identity?.Name),
            PrincipalScopeOrgUnitId = scope.Kind == OrgScopeClaimKind.Valid ? scope.RootOrgUnitId : null,
            DelegateEmployeeId = vm.DelegateEmployeeId,
            DelegateName = BoundedName(delegateName),
            EntityTypeScope = string.IsNullOrWhiteSpace(vm.EntityTypeScope) ? null : vm.EntityTypeScope.Trim(),
            RoleScope = vm.RoleScope?.Trim(),
            ActiveFrom = Utc(vm.ActiveFrom),
            ActiveUntil = Utc(vm.ActiveUntil),
            Reason = vm.Reason.Trim(),
            IsRevoked = false
        };

        var rules = new DelegationRules(
            DelegationOptions.DelegableRoles(configuration),
            DelegationOptions.EntityTypes(configuration),
            DirectRoles(),
            DateTime.UtcNow); // PLAN-50 (review 2): the period ends within 90 days of the creation, not only of its own start.
        var validation = SeparationOfDutiesService.ValidateDelegation(model, rules);
        if (!validation.Allowed)
            return Refused(vm, validation.DenialReason ?? "تفويض غير صالح");

        db.ApprovalDelegations.Add(model);
        db.AuditLogs.Add(new EntityAuditLog
        {
            EntityType = nameof(ApprovalDelegation),
            EntityId = model.Id,
            Action = "ApprovalDelegationCreated",
            ChangedFields = JsonSerializer.Serialize(new
            {
                model.RoleScope,
                model.EntityTypeScope,
                model.DelegateEmployeeId,
                model.PrincipalScopeOrgUnitId,
                model.ActiveFrom,
                model.ActiveUntil
            }),
            UserId = principalId,
            TenantId = tenantContext.TenantId ?? Guid.Empty
        });
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إنشاء التفويض المؤقت";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Revoke(Guid id, string? reason)
    {
        var delegation = await db.ApprovalDelegations.FirstOrDefaultAsync(d => d.Id == id);
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

        if (delegation.PrincipalId != actorId.Value && !SeesAll)
        {
            TempData["Error"] = "يمكن للمفوِّض أو المشرف فقط إلغاء التفويض";
            return RedirectToAction(nameof(Index));
        }

        var trimmed = string.IsNullOrWhiteSpace(reason) ? "إلغاء من المفوِّض" : reason.Trim();
        delegation.IsRevoked = true;
        delegation.RevokedBy = actorId;
        delegation.RevokedAt = DateTime.UtcNow;
        delegation.RevokedReason = trimmed.Length > MaxRevokeReason ? trimmed[..MaxRevokeReason] : trimmed;
        db.AuditLogs.Add(new EntityAuditLog
        {
            EntityType = nameof(ApprovalDelegation),
            EntityId = delegation.Id,
            Action = "ApprovalDelegationRevoked",
            ChangedFields = JsonSerializer.Serialize(new { delegation.RevokedReason }),
            UserId = actorId,
            TenantId = delegation.TenantId
        });
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إلغاء التفويض";
        return RedirectToAction(nameof(Index));
    }

    private ApprovalDelegationCreateViewModel WithLists(ApprovalDelegationCreateViewModel vm)
    {
        vm.DelegableRoles = DirectDelegableRoles();
        vm.EntityTypes = DelegationOptions.EntityTypes(configuration);
        return vm;
    }

    /// <summary>A refused creation answers 400 with the form and its Arabic message.</summary>
    private IActionResult Refused(ApprovalDelegationCreateViewModel vm, string? message)
    {
        if (message is not null)
            ModelState.AddModelError(string.Empty, message);
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return View(nameof(Create), WithLists(vm));
    }

    /// <summary>The form's datetime-local value is UTC (the page shows UTC); a timestamptz column needs the kind said.</summary>
    private static DateTime Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static string? BoundedName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : name.Length > 200 ? name[..200] : name;
}
