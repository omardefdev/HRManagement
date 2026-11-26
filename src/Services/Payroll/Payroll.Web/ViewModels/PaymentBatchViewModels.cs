using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Web.ViewModels;

/// <summary>PLAN-58: how many orders of a batch are in a state, and their amount.</summary>
public sealed record PaymentOrderTally(PaymentOrderStatus Status, int Count, decimal Amount);

/// <summary>PLAN-58 (§2): a stored bank file as the batch's page lists it (its content is read by the download only).</summary>
public sealed record PaymentBatchFileRowViewModel(
    Guid Id,
    int Sequence,
    PaymentBatchFileKind Kind,
    int ControlCount,
    decimal ControlTotal,
    string Sha256,
    Guid GeneratedBy,
    DateTime GeneratedAt,
    Guid? SupersededById,
    string? ReissueReason);

/// <summary>PLAN-58 (§6): an employee whose order is held, by name (never an id), with the reason.</summary>
public sealed record HeldOrderViewModel(Guid EmployeeId, string? EmployeeName, string? HoldReason, decimal Amount);

/// <summary>PLAN-58: a batch's page.</summary>
public sealed record PaymentBatchDetailsViewModel(
    PaymentBatch Batch,
    IReadOnlyList<PaymentOrderTally> Orders,
    IReadOnlyList<PaymentBatchFileRowViewModel> Files,
    FundingDecision? Funding,
    IReadOnlyList<HeldOrderViewModel> Held,
    bool MayPost,
    bool MayApprove);
