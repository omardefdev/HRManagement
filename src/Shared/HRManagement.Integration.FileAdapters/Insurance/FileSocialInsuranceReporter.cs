using System.Text;
using HRManagement.Integration.Abstractions;
using HRManagement.Integration.Abstractions.Insurance;
using Microsoft.Extensions.Options;

namespace HRManagement.Integration.FileAdapters.Insurance;

/// <summary>يصدّر ملف اشتراكات التأمينات الاجتماعية (CSV) إلى مجلد صادر.</summary>
public sealed class FileSocialInsuranceReporter(IOptions<ExternalIntegrationsOptions> options) : ISocialInsuranceReporter
{
    public async Task<IntegrationResult> SubmitAsync(SocialInsuranceReportRequest request, CancellationToken cancellationToken = default)
    {
        var dir = IntegrationFileHelper.ResolveOutputDirectory(options.Value, "social-insurance");
        var fileName = IntegrationFileHelper.BuildFileName("social-insurance", request.PeriodStart, "csv");
        var path = Path.Combine(dir, fileName);

        var sb = new StringBuilder();
        sb.AppendLine("PayrollRunId,PeriodStart,PeriodEnd,EmployeeId,NationalId,EmployeeName,InsurableWageBase,EmployeeContribution,EmployerContribution");
        foreach (var line in request.Lines)
        {
            sb.Append(request.PayrollRunId).Append(',');
            sb.Append(request.PeriodStart.ToString("yyyy-MM-dd")).Append(',');
            sb.Append(request.PeriodEnd.ToString("yyyy-MM-dd")).Append(',');
            sb.Append(line.EmployeeId).Append(',');
            sb.Append(IntegrationFileHelper.EscapeCsv(line.NationalId)).Append(',');
            sb.Append(IntegrationFileHelper.EscapeCsv(line.EmployeeName)).Append(',');
            sb.Append(IntegrationFileHelper.FormatDecimal(line.InsurableWageBase)).Append(',');
            sb.Append(IntegrationFileHelper.FormatDecimal(line.EmployeeContribution)).Append(',');
            sb.AppendLine(IntegrationFileHelper.FormatDecimal(line.EmployerContribution));
        }

        await IntegrationFileHelper.WriteUtf8Async(path, sb.ToString(), cancellationToken);
        return IntegrationResult.Ok(
            message: $"تم تصدير ملف التأمينات ({request.Lines.Count} سطر)",
            externalReference: $"SSI-{request.PayrollRunId:N}",
            outputFilePath: path);
    }
}
