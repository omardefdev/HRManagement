namespace HRManagement.Integration.Abstractions.Payroll;

/// <summary>تسليم ملف الرواتب للخزانة العامة / وزارة المالية.</summary>
public interface IPayrollDisbursementGateway
{
    Task<IntegrationResult> SubmitAsync(PayrollDisbursementRequest request, CancellationToken cancellationToken = default);
}
