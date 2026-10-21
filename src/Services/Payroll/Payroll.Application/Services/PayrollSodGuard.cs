using HRManagement.SharedKernel.SeparationOfDuties;

namespace Payroll.Application.Services;

internal static class PayrollSodGuard
{
    public static (bool Allowed, string? Error) ValidatePayrollRunAction(
        SeparationOfDutiesService sod,
        SodAction action,
        Guid actorId,
        string? actorRole,
        Guid payrollRunId,
        Guid? creatorId,
        Guid? lastApproverId,
        decimal? financialAmount = null)
    {
        var result = sod.ValidateAction(
            actorId,
            actorRole,
            "PayrollRun",
            payrollRunId,
            action,
            creatorId,
            lastApproverId,
            financialAmount);

        if (result.Allowed)
            return (true, null);

        if (action == SodAction.Check && creatorId.HasValue && actorId == creatorId.Value)
            return (false, "لا يمكن لمنشئ التشغيل اعتماد نفس التشغيل.");

        if (action == SodAction.Post && lastApproverId.HasValue && actorId == lastApproverId.Value)
            return (false, "لا يمكن لنفس المستخدم اعتماد التشغيل ثم ترحيله.");

        return (false, result.DenialReason);
    }

    public static (bool Allowed, string? Error) ValidatePaymentBatchDisburse(
        SeparationOfDutiesService sod,
        Guid actorId,
        Guid paymentBatchId,
        Guid? batchCreatorId,
        Guid? payrollRunCreatorId,
        Guid? payrollRunApproverId)
    {
        if (payrollRunCreatorId.HasValue && actorId == payrollRunCreatorId.Value)
            return (false, "لا يمكن لمنشئ التشغيل تنفيذ الصرف.");

        var result = sod.ValidateAction(
            actorId,
            "payroll-poster",
            "PaymentBatch",
            paymentBatchId,
            SodAction.Post,
            batchCreatorId,
            payrollRunApproverId);

        if (result.Allowed)
            return (true, null);

        if (payrollRunApproverId.HasValue && actorId == payrollRunApproverId.Value)
            return (false, "لا يمكن لمعتمد التشغيل تنفيذ الصرف.");

        if (batchCreatorId.HasValue && actorId == batchCreatorId.Value)
            return (false, "لا يمكن لمنشئ الدفعة تنفيذ الصرف.");

        return (false, result.DenialReason);
    }
}
