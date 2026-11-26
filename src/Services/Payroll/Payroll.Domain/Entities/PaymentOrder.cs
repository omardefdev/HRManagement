using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Domain.Entities;

/// <summary>Atomic treasury instruction with idempotency key (SYR-011 / EXT-003).</summary>
public class PaymentOrder : BaseEntity
{
    [Required]
    [MaxLength(128)]
    public string IdempotencyKey { get; set; } = null!;

    public Guid PayrollRunId { get; set; }
    public Guid PaymentBatchId { get; set; }
    public Guid SalaryLineId { get; set; }
    public Guid EmployeeId { get; set; }

    public decimal Amount { get; set; }

    [MaxLength(3)]
    public string CurrencyCode { get; set; } = "SYP";

    [MaxLength(64)]
    public string Channel { get; set; } = "treasury-file";

    [MaxLength(34)]
    public string? BeneficiaryIban { get; set; }

    public PaymentOrderStatus Status { get; set; } = PaymentOrderStatus.Draft;

    /// <summary>
    /// PLAN-58 (G-PAY-15): why a <see cref="PaymentOrderStatus.Held"/> order is out of the sending (<see cref="PaymentOrderHoldReasons"/>);
    /// null for an order that is not held. A code, never an account number.
    /// </summary>
    [MaxLength(PaymentOrderHoldReasons.MaxLength)]
    public string? HoldReason { get; set; }

    /// <summary>PLAN-58 (§2): the bank file that carried the order to the channel (null until it is sent).</summary>
    public Guid? PaymentBatchFileId { get; set; }

    [MaxLength(128)]
    public string? ExternalReference { get; set; }

    [MaxLength(128)]
    public string? SettlementReference { get; set; }

    [MaxLength(500)]
    public string? RejectionReason { get; set; }

    public DateTime? SubmittedAt { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public DateTime? SettledAt { get; set; }
    public DateTime? RejectedAt { get; set; }

    /// <summary>The order's idempotency key: one order per run and line, whichever path asks (EXT-003 exactly-once).</summary>
    public static string IdempotencyKeyFor(Guid payrollRunId, Guid salaryLineId) =>
        $"payroll:{payrollRunId:N}:{salaryLineId:N}";
}

/// <summary>PLAN-58 (G-PAY-15): the reasons an order is held out of a sending. Each holds that employee's order only.</summary>
public static class PaymentOrderHoldReasons
{
    public const int MaxLength = 64;

    /// <summary>The line pinned no bank account (PLAN-55): verifying the employee's account lifts it, with no release request.</summary>
    public const string NoAccount = "NO-ACCOUNT";

    /// <summary>The line's pinned account was retired and no person released its hold (PLAN-55).</summary>
    public const string AccountRetired = "ACCOUNT-RETIRED";

    /// <summary>An active payment hold of the employee, of this run or with no run (released by four eyes).</summary>
    public const string PaymentHold = "PAYMENT-HOLD";

    /// <summary>HR.Core reports the employee blocked by his service regime (D-PAY-4).</summary>
    public const string RegimeBlocker = "REGIME-BLOCKER";

    /// <summary>An open critical fraud alert of the employee in this run.</summary>
    public const string CriticalAlert = "CRITICAL-ALERT";

    /// <summary>The Arabic label of a reason, for the held report and the batch's page.</summary>
    public static string Describe(string? reason) => reason switch
    {
        NoAccount => "لا حساب مصرفي مثبَّت",
        AccountRetired => "الحساب المثبَّت أُحيل",
        PaymentHold => "حجز صرف نشط",
        RegimeBlocker => "حاجز نظام الخدمة",
        CriticalAlert => "تنبيه احتيال حرج مفتوح",
        null or "" => "—",
        _ => "سبب آخر",
    };
}
