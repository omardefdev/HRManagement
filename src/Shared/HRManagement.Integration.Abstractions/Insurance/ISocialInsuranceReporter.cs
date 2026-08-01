namespace HRManagement.Integration.Abstractions.Insurance;

/// <summary>إبلاغ اشتراكات التأمينات الاجتماعية لمؤسسة التأمينات.</summary>
public interface ISocialInsuranceReporter
{
    Task<IntegrationResult> SubmitAsync(SocialInsuranceReportRequest request, CancellationToken cancellationToken = default);
}
