namespace HRManagement.Integration.Abstractions.Tax;

/// <summary>إبلاغ الاستقطاعات الضريبية (إن وُجدت).</summary>
public interface ITaxWithholdingReporter
{
    Task<IntegrationResult> SubmitAsync(TaxWithholdingReportRequest request, CancellationToken cancellationToken = default);
}
