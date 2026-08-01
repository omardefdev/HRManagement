using System.Text;
using HRManagement.Integration.Abstractions;
using HRManagement.Integration.Abstractions.Payroll;
using Microsoft.Extensions.Options;

namespace HRManagement.Integration.FileAdapters.Payroll;

/// <summary>يصدّر ملف CSV قياسي للخزانة/المالية إلى مجلد صادر.</summary>
public sealed class FilePayrollDisbursementGateway(IOptions<ExternalIntegrationsOptions> options) : IPayrollDisbursementGateway
{
    public async Task<IntegrationResult> SubmitAsync(PayrollDisbursementRequest request, CancellationToken cancellationToken = default)
    {
        var dir = IntegrationFileHelper.ResolveOutputDirectory(options.Value, "treasury");
        var fileName = IntegrationFileHelper.BuildFileName("payroll-disbursement", request.PeriodStart, "csv");
        var path = Path.Combine(dir, fileName);

        var sb = new StringBuilder();
        sb.AppendLine("PayrollRunId,PeriodStart,PeriodEnd,Currency,EmployeeId,NationalId,EmployeeName,GrossPay,TotalDeductions,NetPay,BankAccount");
        foreach (var line in request.Lines)
        {
            sb.Append(request.PayrollRunId).Append(',');
            sb.Append(request.PeriodStart.ToString("yyyy-MM-dd")).Append(',');
            sb.Append(request.PeriodEnd.ToString("yyyy-MM-dd")).Append(',');
            sb.Append(IntegrationFileHelper.EscapeCsv(request.CurrencyCode)).Append(',');
            sb.Append(line.EmployeeId).Append(',');
            sb.Append(IntegrationFileHelper.EscapeCsv(line.NationalId)).Append(',');
            sb.Append(IntegrationFileHelper.EscapeCsv(line.EmployeeName)).Append(',');
            sb.Append(IntegrationFileHelper.FormatDecimal(line.GrossPay)).Append(',');
            sb.Append(IntegrationFileHelper.FormatDecimal(line.TotalDeductions)).Append(',');
            sb.Append(IntegrationFileHelper.FormatDecimal(line.NetPay)).Append(',');
            sb.AppendLine(IntegrationFileHelper.EscapeCsv(line.BankAccount));
        }

        await IntegrationFileHelper.WriteUtf8Async(path, sb.ToString(), cancellationToken);
        var reference = $"TREASURY-{request.PayrollRunId:N}";
        return IntegrationResult.Ok(
            message: $"تم تصدير ملف الرواتب للمالية ({request.Lines.Count} سطر)",
            externalReference: reference,
            outputFilePath: path);
    }
}
