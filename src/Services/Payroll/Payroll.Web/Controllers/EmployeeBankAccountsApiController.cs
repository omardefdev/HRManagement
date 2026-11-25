using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Messaging;
using MediatR;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Features.BankAccounts;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;

namespace Payroll.Web.Controllers;

/// <summary>
/// PLAN-55 (§1, D-PAY-14): an employee's bank accounts over the API. A payroll processor registers an account; a payroll approver who is
/// not that processor verifies it against the bank's confirmation, or rejects it; an approver retires the account in force. Every write
/// names its policy and validates an antiforgery token. The branch scope is the employee's (the unit of his active pay profile): outside
/// it, or in another agency, the answer is 404. The number is always masked here: only the approver's verification page shows it whole.
/// </summary>
[ApiController]
[Route("api/payroll/employees/{employeeId:guid}/bank-accounts")]
[Authorize(Policy = HrManagementPolicies.ModulePayroll)]
[OrgScoped(typeof(EmployeeBankAccount))]
public sealed class EmployeeBankAccountsApiController(PayrollDbContext db, IMediator mediator, BankAccountRules rules) : ControllerBase
{
    /// <summary>
    /// The antiforgery token every POST of this controller must send (the header's name and its value); the matching cookie is set on
    /// this response. The API is reached with a gateway session as well as with a bearer token, so a forged cross-site POST must not
    /// reach a verification (PLAN-53's pattern).
    /// </summary>
    [HttpGet("/api/payroll/bank-accounts/antiforgery")]
    public MessagingRepairAntiforgery Antiforgery([FromServices] IAntiforgery antiforgery) =>
        MessagingRepairAntiforgery.Issue(antiforgery, HttpContext);

    /// <summary>The employee's accounts, newest first, each with its number masked. 404 when the caller does not reach the employee.</summary>
    [HttpGet]
    public async Task<IActionResult> List(Guid employeeId, CancellationToken ct)
    {
        if (!await rules.SeesEmployeeAsync(employeeId, await HttpContext.GetOrgScopeAsync(), ct))
            return NotFound(new { error = BankAccountMessages.EmployeeNotFound });
        var accounts = await db.EmployeeBankAccounts.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);
        return Ok(accounts.Select(EmployeeBankAccountDto.From));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> Register(Guid employeeId, [FromBody] RegisterBankAccountApiRequest request, CancellationToken ct)
    {
        // The kind is sent by its name («Iban», «Local»); an unknown name is the handler's refusal (400), like any other input rule.
        var kind = Enum.TryParse<BankAccountKind>(request.AccountKind, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : (BankAccountKind)(-1);
        var result = await mediator.Send(new RequestBankAccountCommand(
            employeeId, kind, request.AccountNumber, request.BankCode, request.BankNameAr, request.HolderNameAr,
            PayProfileChangeHttp.RequireUserId(User), await HttpContext.GetOrgScopeAsync()), ct);
        if (!result.Success)
            return EmployeeBankAccountHttp.Refusal(result);
        EmployeeBankAccountHttp.Audit(HttpContext, EmployeeBankAccountHttp.ActionRegister, result.AccountId);
        return StatusCode(StatusCodes.Status201Created, EmployeeBankAccountHttp.Body(result));
    }

    [HttpPost("{id:guid}/verify")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public async Task<IActionResult> Verify(Guid employeeId, Guid id, [FromBody] VerifyBankAccountApiRequest? request, CancellationToken ct)
    {
        var result = await mediator.Send(new VerifyBankAccountCommand(
            employeeId, id, request?.VerificationReference, PayProfileChangeHttp.RequireUserId(User), await HttpContext.GetOrgScopeAsync()), ct);
        return Answer(result, EmployeeBankAccountHttp.ActionVerify);
    }

    [HttpPost("{id:guid}/reject")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public async Task<IActionResult> Reject(Guid employeeId, Guid id, [FromBody] BankAccountReasonApiRequest? request, CancellationToken ct)
    {
        var result = await mediator.Send(new RejectBankAccountCommand(
            employeeId, id, request?.Reason, PayProfileChangeHttp.RequireUserId(User), await HttpContext.GetOrgScopeAsync()), ct);
        return Answer(result, EmployeeBankAccountHttp.ActionReject);
    }

    /// <summary>Retiring the account in force can leave the employee with no destination, so it takes the approval policy and a reason.</summary>
    [HttpPost("{id:guid}/retire")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public async Task<IActionResult> Retire(Guid employeeId, Guid id, [FromBody] BankAccountReasonApiRequest? request, CancellationToken ct)
    {
        var result = await mediator.Send(new RetireBankAccountCommand(
            employeeId, id, request?.Reason, PayProfileChangeHttp.RequireUserId(User), await HttpContext.GetOrgScopeAsync()), ct);
        return Answer(result, EmployeeBankAccountHttp.ActionRetire);
    }

    private IActionResult Answer(BankAccountResult result, string action)
    {
        if (!result.Success)
            return EmployeeBankAccountHttp.Refusal(result);
        EmployeeBankAccountHttp.Audit(HttpContext, action, result.AccountId);
        return Ok(EmployeeBankAccountHttp.Body(result));
    }
}

/// <summary>The body of a registration: what a processor may say, and nothing of the account's own life.</summary>
public sealed record RegisterBankAccountApiRequest(
    string? AccountKind,
    string? AccountNumber,
    string? BankCode = null,
    string? BankNameAr = null,
    string? HolderNameAr = null);

public sealed record VerifyBankAccountApiRequest(string? VerificationReference);

public sealed record BankAccountReasonApiRequest(string? Reason);
