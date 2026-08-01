using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;

namespace HR.Contracts.Domain.Services;

public static class ContractWorkflowRules
{
    public static IReadOnlyList<string> ValidateCanActivate(
        Contract contract,
        bool hasCommencement,
        bool hasPassedProbationWhenRequired)
    {
        var errors = new List<string>();

        if (contract.Status != ContractStatus.Pending)
            errors.Add("يمكن تفعيل العقود في حالة «قيد الانتظار» فقط.");

        if (!contract.Signatures.Any())
            errors.Add("يجب وجود توقيع واحد على الأقل على العقد.");

        if (!hasCommencement)
            errors.Add("يجب وجود سجل مباشرة مرتبط بالعقد.");

        if (contract.ProbationMonths is > 0 && !hasPassedProbationWhenRequired)
            errors.Add("يجب اجتياز فترة الاختبار بنجاح قبل تفعيل العقد.");

        return errors;
    }

    public static bool CanTerminate(Contract? contract) =>
        contract is not null && contract.Status is ContractStatus.Active or ContractStatus.Pending;

    public static void Terminate(Contract contract)
    {
        if (!CanTerminate(contract))
            throw new InvalidOperationException($"Cannot terminate contract in status {contract.Status}.");
        contract.Status = ContractStatus.Terminated;
    }
}
