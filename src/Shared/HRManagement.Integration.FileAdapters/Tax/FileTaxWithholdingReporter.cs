using System.Text;
using HRManagement.Integration.Abstractions;
using HRManagement.Integration.Abstractions.Tax;
using Microsoft.Extensions.Options;

namespace HRManagement.Integration.FileAdapters.Tax;

public sealed class FileTaxWithholdingReporter(IOptions<ExternalIntegrationsOptions> options) : ITaxWithholdingReporter
{
    public async Task<IntegrationResult> SubmitAsync(TaxWithholdingReportRequest request, CancellationToken cancellationToken = default)
    {
        var dir = IntegrationFileHelper.ResolveOutputDirectory(options.Value, "tax");
        var fileName = IntegrationFileHelper.BuildFileName("tax-withholding", request.PeriodStart, "csv");
        var path = Path.Combine(dir, fileName);

        var sb = new StringBuilder();
        sb.AppendLine("PayrollRunId,PeriodStart,PeriodEnd,EmployeeId,NationalId,TaxableAmount,WithheldAmount");
        foreach (var line in request.Lines)
        {
            sb.Append(request.PayrollRunId).Append(',');
            sb.Append(request.PeriodStart.ToString("yyyy-MM-dd")).Append(',');
            sb.Append(request.PeriodEnd.ToString("yyyy-MM-dd")).Append(',');
            sb.Append(line.EmployeeId).Append(',');
            sb.Append(IntegrationFileHelper.EscapeCsv(line.NationalId)).Append(',');
            sb.Append(IntegrationFileHelper.FormatDecimal(line.TaxableAmount)).Append(',');
            sb.AppendLine(IntegrationFileHelper.FormatDecimal(line.WithheldAmount));
        }

        await IntegrationFileHelper.WriteUtf8Async(path, sb.ToString(), cancellationToken);
        return IntegrationResult.Ok(
            message: $"تم تصدير ملف الضريبة ({request.Lines.Count} سطر)",
            externalReference: $"TAX-{request.PayrollRunId:N}",
            outputFilePath: path);
    }
}

public sealed class StubTaxWithholdingReporter : ITaxWithholdingReporter
{
    public Task<IntegrationResult> SubmitAsync(TaxWithholdingReportRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(IntegrationResult.Ok(
            message: "Stub: no tax withholding for civil servants (simulated)",
            externalReference: $"STUB-TAX-{request.PayrollRunId:N}"));
}
