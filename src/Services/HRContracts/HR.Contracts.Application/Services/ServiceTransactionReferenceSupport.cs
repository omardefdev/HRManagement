using FluentValidation;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HRManagement.ServiceDefaults.CodeGeneration;

namespace HR.Contracts.Application.Services;

public static class ServiceTransactionReferenceSupport
{
    public static string MapTypeCode(ServiceTransactionType type) => type switch
    {
        ServiceTransactionType.Appointment => "APPT",
        ServiceTransactionType.Confirmation => "CONF",
        ServiceTransactionType.LeaveAdvance => "LEAV",
        ServiceTransactionType.JobUpdate => "JOB",
        ServiceTransactionType.Separation => "SEP",
        _ => type.ToString().ToUpperInvariant()
    };

    public static async Task ApplyReferenceAsync(
        ServiceTransaction transaction,
        ICodeGenerationClient client,
        CancellationToken ct)
    {
        var (ok, code, error) = await CodeGenerationGateway.GenerateOrErrorAsync(
            client,
            CodeGenerationRequests.ServiceTransaction(MapTypeCode(transaction.TransactionType)),
            ct);
        if (!ok)
            throw new ValidationException(error ?? "تعذّر توليد الرقم المرجعي للمعاملة");
        transaction.ReferenceNumber = code;
    }
}
