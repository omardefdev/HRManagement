using System.Security.Claims;
using Auth.Directory.Domain.Entities;
using Auth.Directory.Infrastructure.Data;
using Auth.Directory.Infrastructure.Keycloak;
using HRManagement.SharedKernel.SeparationOfDuties;
using HRManagement.ServiceDefaults.Integration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Rendering;
using HRManagement.SharedKernel.ViewModels;

namespace Auth.Directory.Web.Controllers;

public class AuthUsersController : Controller
{
    private readonly AuthDirectoryDbContext _db;
    private readonly IKeycloakUserProvisioningService _keycloak;
    private readonly IHrCoreLookupClient? _hrCoreLookup;

    public AuthUsersController(
        AuthDirectoryDbContext db,
        IKeycloakUserProvisioningService keycloak,
        IHrCoreLookupClient? hrCoreLookup = null)
    {
        _db = db;
        _keycloak = keycloak;
        _hrCoreLookup = hrCoreLookup;
    }

    private static Guid? ParseUserId(ClaimsPrincipal user)
    {
        var v = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(v, out var g) ? g : null;
    }

    public async Task<IActionResult> Index()
    {
        return View(await _db.AuthUsers.AsNoTracking().OrderBy(u => u.Username).Take(SearchFilterBase.MaxPageSize).ToListAsync());
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var user = await _db.AuthUsers
            .Include(u => u.Tokens)
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user is null) return NotFound();
        ViewBag.EmployeeName = await ResolveEmployeeNameAsync(user.EmployeeId);
        return View(user);
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var user = await _db.AuthUsers
            .Include(u => u.UserRoles)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user is null) return NotFound();

        await PopulateEditLookupsAsync(id);
        return View(user);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, bool isActive, Guid[] selectedRoles, string? secondApproverId)
    {
        var user = await _db.AuthUsers
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user is null) return NotFound();

        var assignorId = ParseUserId(User);
        if (assignorId is null)
        {
            TempData["Error"] = "تعذر تحديد هوية المانح من التوكن";
            return RedirectToAction(nameof(Edit), new { id });
        }

        var allRoles = await _db.Roles.AsNoTracking().OrderBy(r => r.Name).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync();
        var roleById = allRoles.ToDictionary(r => r.Id);
        var selected = (selectedRoles ?? Array.Empty<Guid>()).ToHashSet();
        var currentRoleIds = user.UserRoles.Select(ur => ur.RoleId).ToHashSet();
        var toAdd = selected.Except(currentRoleIds).ToList();
        var remainingRoleNames = user.UserRoles
            .Where(ur => selected.Contains(ur.RoleId) && ur.Role is not null)
            .Select(ur => ur.Role!.Name)
            .ToList();

        var assignorRoles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        Guid? secondApprover = Guid.TryParse(secondApproverId, out var sa) ? sa : null;
        var sod = new SeparationOfDutiesService();

        foreach (var roleId in toAdd)
        {
            if (!roleById.TryGetValue(roleId, out var role))
                continue;

            SodResult guardResult;
            if (RoleAssignmentGuard.IsPrivileged(role.Name))
            {
                if (!secondApprover.HasValue)
                {
                    TempData["Error"] = $"منح الدور الحساس '{role.Name}' يتطلب اختيار معتمد ثانٍ (اعتماد مزدوج).";
                    await PopulateEditLookupsAsync(id, secondApproverId);
                    return View(user);
                }

                var secondApproverIsValid = await _db.AuthUsers
                    .AsNoTracking()
                    .AnyAsync(u => u.Id == secondApprover.Value
                        && u.IsActive
                        && u.Id != user.Id
                        && u.Id != assignorId.Value);
                if (!secondApproverIsValid)
                {
                    TempData["Error"] = "اختر معتمداً ثانياً فعالاً ومختلفاً عن المانح وصاحب الحساب.";
                    await PopulateEditLookupsAsync(id, secondApproverId);
                    return View(user);
                }

                guardResult = RoleAssignmentGuard.ValidatePrivilegedGrant(
                    assignorId.Value,
                    secondApprover.Value,
                    user.Id,
                    remainingRoleNames,
                    role.Name,
                    sod);
            }
            else
            {
                guardResult = RoleAssignmentGuard.ValidateGrant(
                    assignorId.Value,
                    assignorRoles,
                    user.Id,
                    remainingRoleNames,
                    role.Name,
                    sod);
            }

            if (!guardResult.Allowed)
            {
                TempData["Error"] = guardResult.DenialReason;
                await PopulateEditLookupsAsync(id, secondApproverId);
                return View(user);
            }

            remainingRoleNames.Add(role.Name);
        }

        user.IsActive = isActive;

        var toRemove = user.UserRoles.Where(ur => !selected.Contains(ur.RoleId)).ToList();
        foreach (var ur in toRemove)
            _db.UserRoles.Remove(ur);

        foreach (var roleId in toAdd)
        {
            _db.UserRoles.Add(new UserRole
            {
                AuthUserId = user.Id,
                RoleId = roleId,
                AssignedAt = DateTime.UtcNow,
                AssignedBy = User.Identity?.Name
            });
        }

        _db.AuthAuditLogs.Add(new AuthAuditLog
        {
            Action = "EditUser",
            EntityType = nameof(AuthUser),
            EntityId = user.Id,
            PerformedBy = User.Identity?.Name,
            Details = $"IsActive={isActive}, Roles=[{string.Join(",", selected)}], SecondApprover={secondApprover}"
        });

        await _db.SaveChangesAsync();

        HRManagement.ServiceDefaults.SecurityAudit.Log(
            HttpContext,
            HRManagement.ServiceDefaults.SecurityAudit.EventTypes.RoleChange,
            resource: $"AuthUser:{user.Id}",
            detailJson: $"{{\"roles\":[{string.Join(",", selected.Select(r => $"\"{r}\""))}],\"isActive\":{(isActive ? "true" : "false")}}}");

        if (!string.IsNullOrEmpty(user.KeycloakUserId))
        {
            var kcResult = isActive
                ? await _keycloak.TryEnableUserAsync(user.KeycloakUserId)
                : await _keycloak.TryDisableUserAsync(user.KeycloakUserId);

            if (kcResult.Attempted && !kcResult.Success)
            {
                TempData["Error"] = $"تم حفظ المستخدم محلياً لكن فشلت مزامنة Keycloak: {kcResult.Detail}";
                return RedirectToAction(nameof(Details), new { id });
            }

            var roleNames = await _db.UserRoles
                .AsNoTracking()
                .Where(ur => ur.AuthUserId == user.Id)
                .Include(ur => ur.Role)
                .Select(ur => ur.Role!.Name)
                .ToArrayAsync();

            if (roleNames.Length > 0)
            {
                var rolesResult = await _keycloak.TryAssignRealmRolesAsync(user.KeycloakUserId, roleNames);
                if (rolesResult.Attempted && !rolesResult.Success)
                {
                    TempData["Error"] = $"تم حفظ المستخدم لكن فشلت مزامنة الأدوار: {rolesResult.Detail}";
                    return RedirectToAction(nameof(Details), new { id });
                }
            }
        }

        TempData["Success"] = "تم تحديث المستخدم";
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task PopulateEditLookupsAsync(Guid targetId, string? selectedSecondApproverId = null)
    {
        ViewBag.AllRoles = await _db.Roles
            .AsNoTracking()
            .OrderBy(r => r.Name)
            .Take(SearchFilterBase.LookupMaxPageSize)
            .ToListAsync();

        var approverRows = await _db.AuthUsers
            .AsNoTracking()
            .Where(u => u.IsActive && u.Id != targetId)
            .OrderBy(u => u.Username)
            .Take(SearchFilterBase.LookupMaxPageSize)
            .Select(u => new { u.Id, u.Username, u.Email })
            .ToListAsync();
        ViewBag.SecondApprovers = approverRows
            .Select(u => new SelectListItem
            {
                Value = u.Id.ToString(),
                Text = u.Username + " — " + u.Email,
                Selected = u.Id.ToString() == selectedSecondApproverId
            })
            .ToList();
    }

    private async Task<string> ResolveEmployeeNameAsync(Guid? employeeId)
    {
        if (!employeeId.HasValue)
            return "غير مرتبط بموظف";

        try
        {
            if (_hrCoreLookup is null)
                return "غير متاح";

            var result = await _hrCoreLookup.GetEmployeesByIdsAsync([employeeId.Value], HttpContext.RequestAborted);
            return result.FirstOrDefault()?.Label ?? "غير متاح";
        }
        catch (Exception ex)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<AuthUsersController>>()
                .LogWarning(ex, "Employee name lookup failed for auth user details.");
            return "غير متاح";
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SyncFromKeycloak(Guid id, CancellationToken cancellationToken)
    {
        var user = await _db.AuthUsers.FindAsync([id], cancellationToken);
        if (user is null) return NotFound();

        var syncRecord = new DirectorySyncRecord
        {
            SourceService = "Keycloak",
            RecordsProcessed = 1,
            StartedAt = DateTime.UtcNow
        };

        KeycloakProvisionResult result;
        if (string.IsNullOrWhiteSpace(user.KeycloakUserId))
        {
            if (user.EmployeeId is null)
            {
                TempData["Error"] = "لا يمكن المزامنة: المستخدم غير مرتبط بموظف";
                return RedirectToAction(nameof(Details), new { id });
            }

            result = await _keycloak.TryEnsureUserAsync(
                user.Username,
                user.Email,
                user.Username,
                user.EmployeeId.Value,
                user.ScopeOrgUnitId,
                tenantId: user.TenantId,
                cancellationToken: cancellationToken);
        }
        else
        {
            result = await _keycloak.TrySyncUserInfoAsync(
                user.KeycloakUserId,
                user.Username,
                user.Email,
                cancellationToken);
        }

        syncRecord.SuccessCount = result.Success ? 1 : 0;
        syncRecord.FailureCount = result.Success ? 0 : 1;
        syncRecord.CompletedAt = DateTime.UtcNow;
        syncRecord.Log = result.Attempted
            ? $"Reconcile: Success={result.Success}, Detail={result.Detail}"
            : "DirectorySync disabled — no Keycloak call attempted";

        if (result.Success && result.Attempted && !string.IsNullOrWhiteSpace(result.Detail)
            && string.IsNullOrWhiteSpace(user.KeycloakUserId))
        {
            user.KeycloakUserId = result.Detail;
        }

        _db.Set<DirectorySyncRecord>().Add(syncRecord);

        _db.AuthAuditLogs.Add(new AuthAuditLog
        {
            Action = "SyncFromKeycloak",
            EntityType = nameof(AuthUser),
            EntityId = user.Id,
            PerformedBy = User.Identity?.Name,
            Details = syncRecord.Log
        });

        await _db.SaveChangesAsync(cancellationToken);
        TempData[result.Success ? "Success" : "Error"] = result.Success
            ? "تمت مزامنة المستخدم مع Keycloak"
            : $"فشلت المزامنة: {result.Detail ?? "unknown"}";
        return RedirectToAction(nameof(Details), new { id });
    }
}
