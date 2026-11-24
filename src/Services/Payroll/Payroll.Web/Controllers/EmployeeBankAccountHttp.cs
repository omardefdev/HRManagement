using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Mvc;
using Payroll.Application.Features.BankAccounts;
using Payroll.Domain.Entities;

namespace Payroll.Web.Controllers;

/// <summary>
/// PLAN-55: what the bank account controllers share: the one mapping from a command's answer to HTTP, the masked account an answer
/// carries, and the security audit entry of each act. No answer and no audit text carries an account number.
/// </summary>
internal static class EmployeeBankAccountHttp
{
    /// <summary>
    /// The API's answer to a refusal, the same everywhere: 400 for a broken input rule and for the registrar verifying his own
    /// registration, 409 for a state conflict, 404 outside the scope or the agency. The body carries the Arabic message and the account's
    /// unchanged status.
    /// </summary>
    public static ObjectResult Refusal(BankAccountResult result) =>
        new(new { error = result.Error, accountId = result.AccountId, status = result.Status?.ToString() }) { StatusCode = result.StatusCode };

    public static object Body(BankAccountResult result) => new
    {
        accountId = result.AccountId,
        status = result.Status?.ToString(),
        retiredAccountId = result.RetiredAccountId,
        holdsPlaced = result.HoldsPlaced,
    };

    /// <summary>The act in the security audit: its name and the account's id. Never the number.</summary>
    public static void Audit(HttpContext context, string action, Guid? accountId) =>
        SecurityAudit.Log(
            context,
            SecurityAudit.EventTypes.PayrollOverride,
            resource: $"BankAccount:{accountId:D}",
            detailJson: $"{{\"action\":\"{action}\"}}");

    public const string ActionRegister = "BankAccountRegister";
    public const string ActionVerify = "BankAccountVerify";
    public const string ActionReject = "BankAccountReject";
    public const string ActionRetire = "BankAccountRetire";
}

/// <summary>An account as the API returns it: the number masked to its last four characters.</summary>
public sealed record EmployeeBankAccountDto(
    Guid Id,
    Guid EmployeeId,
    string AccountKind,
    string MaskedNumber,
    string? BankCode,
    string? BankNameAr,
    string? HolderNameAr,
    string Status,
    Guid? RegisteredBy,
    DateTime RegisteredAt,
    Guid? VerifiedBy,
    DateTime? VerifiedAt,
    string? VerificationReference,
    Guid? RejectedBy,
    DateTime? RejectedAt,
    string? RejectionReason,
    Guid? RetiredBy,
    DateTime? RetiredAt,
    string? RetirementReason,
    Guid? SupersedesAccountId)
{
    public static EmployeeBankAccountDto From(EmployeeBankAccount a) => new(
        a.Id, a.EmployeeId, a.AccountKind.ToString(), a.MaskedNumber, a.BankCode, a.BankNameAr, a.HolderNameAr, a.Status.ToString(),
        a.CreatedBy, a.CreatedAt, a.VerifiedBy, a.VerifiedAt, a.VerificationReference, a.RejectedBy, a.RejectedAt, a.RejectionReason,
        a.RetiredBy, a.RetiredAt, a.RetirementReason, a.SupersedesAccountId);
}
