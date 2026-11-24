using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Payroll.Application.Features.BankAccounts;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;
using Payroll.Web.ViewModels;

namespace Payroll.Web.Controllers;

/// <summary>
/// PLAN-55 (§1): «الحسابات المصرفية». An employee's accounts, the accounts that wait for verification, the registration form, and the
/// approver's verification page. The number is masked on every page but the verification page, which only a payroll approver opens and
/// which is never cached. A restricted user reaches only the employees of his branch (the scope filter answers 404 before the action
/// runs, and the handlers check again).
/// </summary>
[Authorize(Policy = HrManagementPolicies.ModulePayroll)]
[OrgScoped(typeof(EmployeeBankAccount))]
public sealed class EmployeeBankAccountsController(
    PayrollDbContext db,
    IMediator mediator,
    BankAccountRules rules,
    IOptions<PayrollBankOptions> bankOptions) : Controller
{
    public async Task<IActionResult> Index(Guid employeeId)
    {
        var model = await IndexModelAsync(employeeId);
        return model is null ? NotFound() : View(model);
    }

    /// <summary>The accounts that wait for a second person, oldest first, in the caller's scope.</summary>
    public async Task<IActionResult> Pending()
    {
        var query = await db.EmployeeBankAccounts.AsNoTracking().InOrgScopeAsync(HttpContext, db);
        var accounts = await query
            .Where(a => a.Status == BankAccountStatus.PendingVerification)
            .OrderBy(a => a.CreatedAt)
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync(HttpContext.RequestAborted);
        return View(new PendingBankAccountsViewModel
        {
            Accounts = accounts,
            CanApprove = PayrollPolicyRules.CanApprove(User),
            CurrentUserId = PayProfileChangeHttp.RequireUserId(User),
        });
    }

    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> Create(Guid employeeId)
    {
        if (!await rules.SeesEmployeeAsync(employeeId, await HttpContext.GetOrgScopeAsync(), HttpContext.RequestAborted))
            return NotFound();
        return View(WithBanks(new RegisterBankAccountViewModel { EmployeeId = employeeId }));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> Create(RegisterBankAccountViewModel model)
    {
        if (!ModelState.IsValid || model.EmployeeId is not Guid employeeId)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View(WithBanks(Unposted(model)));
        }

        var result = await mediator.Send(new RequestBankAccountCommand(
            employeeId, model.AccountKind, model.AccountNumber, model.BankCode, model.BankNameAr, model.HolderNameAr,
            PayProfileChangeHttp.RequireUserId(User), await HttpContext.GetOrgScopeAsync()), HttpContext.RequestAborted);
        if (result.Success)
        {
            EmployeeBankAccountHttp.Audit(HttpContext, EmployeeBankAccountHttp.ActionRegister, result.AccountId);
            TempData["Success"] = "سُجّل الحساب وهو بانتظار تحقق معتمد رواتب آخر";
            return RedirectToAction(nameof(Index), new { employeeId });
        }

        if (result.Failure == BankAccountFailure.NotFound)
            return NotFound();
        ModelState.AddModelError(string.Empty, result.Error ?? "تعذّر تسجيل الحساب");
        Response.StatusCode = result.StatusCode;
        return View(WithBanks(Unposted(model)));
    }

    /// <summary>
    /// The verification page: the one place the number is shown whole, for a payroll approver to compare it with the bank's
    /// confirmation. Never stored by a browser or a proxy.
    /// </summary>
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Verify(Guid id)
    {
        var model = await VerifyModelAsync(id, null);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Verify(Guid id, string? verificationReference)
    {
        var employeeId = await EmployeeOfAsync(id);
        if (employeeId is null)
            return NotFound();
        var result = await mediator.Send(new VerifyBankAccountCommand(
            employeeId.Value, id, verificationReference, PayProfileChangeHttp.RequireUserId(User), await HttpContext.GetOrgScopeAsync()),
            HttpContext.RequestAborted);
        if (result.Success)
        {
            EmployeeBankAccountHttp.Audit(HttpContext, EmployeeBankAccountHttp.ActionVerify, result.AccountId);
            TempData["Success"] = result.RetiredAccountId is null
                ? "جرى التحقق من الحساب وصار نافذاً"
                : "جرى التحقق من الحساب وصار نافذاً، وأُحيل الحساب السابق";
            if (result.HoldsPlaced > 0)
                TempData["Warning"] = HoldsNotice(result.HoldsPlaced);
            return RedirectToAction(nameof(Index), new { employeeId });
        }

        if (result.Failure == BankAccountFailure.NotFound)
            return NotFound();
        db.ChangeTracker.Clear();
        var model = await VerifyModelAsync(id, verificationReference);
        if (model is null)
            return NotFound();
        ModelState.AddModelError(string.Empty, result.Error ?? "تعذّر التحقق من الحساب");
        Response.StatusCode = result.StatusCode;
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public async Task<IActionResult> Reject(Guid id, string? reason)
    {
        var employeeId = await EmployeeOfAsync(id);
        if (employeeId is null)
            return NotFound();
        var result = await mediator.Send(new RejectBankAccountCommand(
            employeeId.Value, id, reason, PayProfileChangeHttp.RequireUserId(User), await HttpContext.GetOrgScopeAsync()),
            HttpContext.RequestAborted);
        return await AnswerAsync(employeeId.Value, result, EmployeeBankAccountHttp.ActionReject, "رُفض الحساب");
    }

    /// <summary>Retiring the account in force can leave the employee with no destination: the approval policy, and a reason.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public async Task<IActionResult> Retire(Guid id, string? reason)
    {
        var employeeId = await EmployeeOfAsync(id);
        if (employeeId is null)
            return NotFound();
        var result = await mediator.Send(new RetireBankAccountCommand(
            employeeId.Value, id, reason, PayProfileChangeHttp.RequireUserId(User), await HttpContext.GetOrgScopeAsync()),
            HttpContext.RequestAborted);
        return await AnswerAsync(employeeId.Value, result, EmployeeBankAccountHttp.ActionRetire, "أُحيل الحساب");
    }

    /// <summary>
    /// A form's answer, with the API's status: 404 outside the scope; for a broken rule (400) or a state conflict (409) the employee's
    /// page again with the Arabic message. Nothing changed in any of them.
    /// </summary>
    private async Task<IActionResult> AnswerAsync(Guid employeeId, BankAccountResult result, string action, string done)
    {
        if (result.Success)
        {
            EmployeeBankAccountHttp.Audit(HttpContext, action, result.AccountId);
            TempData["Success"] = done;
            if (result.HoldsPlaced > 0)
                TempData["Warning"] = HoldsNotice(result.HoldsPlaced);
            return RedirectToAction(nameof(Index), new { employeeId });
        }

        if (result.Failure == BankAccountFailure.NotFound)
            return NotFound();
        db.ChangeTracker.Clear();
        var model = await IndexModelAsync(employeeId);
        if (model is null)
            return NotFound();
        ModelState.AddModelError(string.Empty, result.Error ?? "تعذّر تنفيذ الإجراء");
        Response.StatusCode = result.StatusCode;
        return View(nameof(Index), model);
    }

    private static string HoldsNotice(int holds) =>
        FormattableString.Invariant(
            $"كان الحساب المحال مثبَّتاً في {holds} تشغيل لم يُصرف بعد: وُضع حجز صرف للموظف في كل منها إلى أن يُفرج عنه من شاشة حجوزات الصرف");

    private async Task<EmployeeBankAccountsViewModel?> IndexModelAsync(Guid employeeId)
    {
        if (!await rules.SeesEmployeeAsync(employeeId, await HttpContext.GetOrgScopeAsync(), HttpContext.RequestAborted))
            return null;
        var accounts = await db.EmployeeBankAccounts.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(HttpContext.RequestAborted);
        return new EmployeeBankAccountsViewModel
        {
            EmployeeId = employeeId,
            Accounts = accounts,
            CanRegister = PayrollPolicyRules.CanProcess(User),
            CanApprove = PayrollPolicyRules.CanApprove(User),
        };
    }

    private async Task<VerifyBankAccountViewModel?> VerifyModelAsync(Guid id, string? reference)
    {
        var account = await db.EmployeeBankAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, HttpContext.RequestAborted);
        if (account is null
            || !await rules.SeesEmployeeAsync(account.EmployeeId, await HttpContext.GetOrgScopeAsync(), HttpContext.RequestAborted))
            return null;
        var inForce = await db.EmployeeBankAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.EmployeeId == account.EmployeeId && a.Status == BankAccountStatus.Active, HttpContext.RequestAborted);
        return new VerifyBankAccountViewModel
        {
            Account = account,
            InForce = inForce,
            IsOwnRegistration = account.CreatedBy == PayProfileChangeHttp.RequireUserId(User),
            HasNoRegistrar = account.CreatedBy is null || account.CreatedBy == Guid.Empty,
            VerificationReference = reference,
        };
    }

    private async Task<Guid?> EmployeeOfAsync(Guid accountId)
    {
        var employeeId = await db.EmployeeBankAccounts.AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => (Guid?)a.EmployeeId)
            .FirstOrDefaultAsync(HttpContext.RequestAborted);
        return employeeId;
    }

    private RegisterBankAccountViewModel WithBanks(RegisterBankAccountViewModel model)
    {
        model.Banks = bankOptions.Value.Banks;
        return model;
    }

    /// <summary>A refused form is shown again without the number it posted: the number is typed again, never echoed into a page.</summary>
    private RegisterBankAccountViewModel Unposted(RegisterBankAccountViewModel model)
    {
        // The entry keeps its errors; only the posted value is dropped (a tag helper would write it back into the field).
        if (ModelState.TryGetValue(nameof(RegisterBankAccountViewModel.AccountNumber), out var entry))
        {
            entry.RawValue = null;
            entry.AttemptedValue = null;
        }

        model.AccountNumber = null;
        return model;
    }
}
