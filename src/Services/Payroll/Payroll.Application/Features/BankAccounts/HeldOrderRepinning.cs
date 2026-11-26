using System.Text.Json;
using HRManagement.SharedKernel.Entities;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Domain.Constants;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Application.Features.BankAccounts;

/// <summary>
/// PLAN-58 (§6 rule 3, P-10): <b>the plan's one deliberate exception to D-PAY-15's pinning, for an order that has not left only.</b> An
/// order held because its line pinned no account (<c>NO-ACCOUNT</c>) or an account since retired (<c>ACCOUNT-RETIRED</c>) needs no release
/// request: verifying the employee's new account is already two people's act (PLAN-55). When that account becomes the one in force, each
/// such order of the employee that has not left — and its line — is pinned to it, the change is written to <c>EntityAuditLog</c> (ids only,
/// never a number), and the account holds are lifted, with the account's two people as releaser and approver. A batch still pending sends
/// the order with its first sending; a batch already sent sends it by <c>submit-held</c> with a supplementary file. An order that has
/// left keeps the account it was sent to.
/// </summary>
public static class HeldOrderRepinning
{
    public const string AuditAction = "RepinnedOnAccountVerification";

    public const string HoldLiftedReason =
        "رُفع حجز الحساب بالتحقق من حساب الموظف الجديد (فعل شخصين): ثُبّت الحساب الجديد على البند وعلى الأمر الذي لم يُرسل.";

    /// <summary>Re-pins in the caller's unit of work (the caller saves, in the verification's transaction). Returns how many orders moved.</summary>
    public static async Task<int> RepinAsync(
        IPayrollRepository repository, EmployeeBankAccount account, Guid verifiedBy, DateTime now, CancellationToken ct)
    {
        if (account.Status != BankAccountStatus.Active)
            return 0;

        var retired = (await repository.EmployeeBankAccounts.AsNoTracking()
                .Where(a => a.EmployeeId == account.EmployeeId && a.Status == BankAccountStatus.Retired)
                .Select(a => a.Id)
                .ToListAsync(ct))
            .ToHashSet();

        var candidates = await (from order in repository.PaymentOrders
                                join batch in repository.PaymentBatches on order.PaymentBatchId equals batch.Id
                                where order.EmployeeId == account.EmployeeId
                                      && (order.Status == PaymentOrderStatus.Draft || order.Status == PaymentOrderStatus.Held)
                                      && batch.Status != PaymentBatchStatus.Voided && batch.Status != PaymentBatchStatus.Reversed
                                select new { Order = order, BatchStatus = batch.Status })
            .ToListAsync(ct);
        if (candidates.Count == 0)
            return 0;

        var lineIds = candidates.Select(c => c.Order.SalaryLineId).ToList();
        var lines = await repository.SalaryLines.Where(l => lineIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);

        var moved = 0;
        var runs = new HashSet<Guid>();
        foreach (var candidate in candidates)
        {
            var order = candidate.Order;
            if (!lines.TryGetValue(order.SalaryLineId, out var line))
                continue;
            var accountProblem = line.BankAccountId is null || retired.Contains(line.BankAccountId.Value)
                                 || order.HoldReason is PaymentOrderHoldReasons.NoAccount or PaymentOrderHoldReasons.AccountRetired;
            if (!accountProblem || line.BankAccountId == account.Id)
                continue;

            var fromAccount = line.BankAccountId;
            line.BankAccountId = account.Id;
            order.BeneficiaryIban = account.AccountNumber;
            order.UpdatedAt = now;
            if (order.Status == PaymentOrderStatus.Held
                && order.HoldReason is PaymentOrderHoldReasons.NoAccount or PaymentOrderHoldReasons.AccountRetired)
            {
                order.HoldReason = null;
                // A batch not yet sent takes the order back into its first sending; a sent batch sends it by submit-held.
                if (candidate.BatchStatus == PaymentBatchStatus.Pending)
                    order.Status = PaymentOrderStatus.Draft;
            }

            await repository.AddAsync(new EntityAuditLog
            {
                TenantId = order.TenantId,
                EntityType = nameof(PaymentOrder),
                EntityId = order.Id,
                Action = AuditAction,
                UserId = verifiedBy,
                ChangedFields = JsonSerializer.Serialize(new
                {
                    paymentOrderId = order.Id,
                    salaryLineId = line.Id,
                    payrollRunId = order.PayrollRunId,
                    fromBankAccountId = fromAccount,
                    toBankAccountId = account.Id,
                    rule = "PLAN-58 §6 rule 3: the one exception to D-PAY-15, for an order that has not left",
                }),
            }, ct);
            runs.Add(order.PayrollRunId);
            moved++;
        }

        if (moved == 0)
            return 0;

        // The account holds of those runs are lifted: the account's registrar and its verifier are the two people.
        var holds = await repository.PaymentHolds
            .Where(h => h.EmployeeId == account.EmployeeId && h.Status == PaymentHoldStatus.Active
                        && h.PayrollRunId != null && runs.Contains(h.PayrollRunId.Value)
                        && h.HoldType == PaymentHoldType.Manual && h.Reason.StartsWith(EmployeeBankAccount.RetiredHoldRule))
            .ToListAsync(ct);
        foreach (var hold in holds)
        {
            hold.Status = PaymentHoldStatus.Released;
            hold.ReleasedBy = account.CreatedBy;
            hold.ReleaseApprovedBy = verifiedBy;
            hold.ReleaseReason = HoldLiftedReason;
            hold.ReleasedAtUtc = now;
            hold.UpdatedAt = now;
        }

        return moved;
    }
}
