using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Infrastructure.Integration;

/// <summary>One row of a bank file: an order with the figures of its line and its beneficiary's name.</summary>
public sealed record PaymentBatchFileRow(PaymentOrder Order, SalaryLine? Line, string? EmployeeName);

/// <summary>What a built file is: its bytes, its control count and total, and its SHA-256.</summary>
public sealed record BuiltPaymentBatchFile(byte[] Content, int ControlCount, decimal ControlTotal, string Sha256);

/// <summary>
/// PLAN-58 (§2, P-6, D-PAY-9): today's treasury CSV, built from the orders of one sending only (each to the account its line pinned,
/// <see cref="PaymentOrder.BeneficiaryIban"/>), with the columns the treasury file had (a reader by position keeps every column it knew),
/// then the order and the amount it pays (after a funding decision, less than the line's net), and a closing control line:
/// <c>CONTROL,&lt;count&gt;,&lt;total&gt;,&lt;batch&gt;,&lt;sequence&gt;,&lt;kind&gt;</c>. Pure: the same rows make the same bytes.
/// </summary>
public static class PaymentBatchFileBuilder
{
    public const string Header =
        "PayrollRunId,PeriodStart,PeriodEnd,Currency,EmployeeId,NationalId,EmployeeName,GrossPay,TotalDeductions,NetPay,BankAccount,TotalAdjustments,PaymentOrderId,Amount";

    public const string ControlTag = "CONTROL";

    public static BuiltPaymentBatchFile Build(
        PaymentBatch batch,
        PayrollRun run,
        IReadOnlyList<PaymentBatchFileRow> rows,
        int sequence,
        PaymentBatchFileKind kind)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(run);
        var sb = new StringBuilder();
        sb.Append(Header).Append('\n');
        foreach (var row in rows.OrderBy(r => r.Order.EmployeeId).ThenBy(r => r.Order.Id))
        {
            var order = row.Order;
            sb.Append(run.Id).Append(',');
            sb.Append(DateOnly.FromDateTime(run.PeriodStart).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(DateOnly.FromDateTime(run.PeriodEnd).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(EscapeCsv(order.CurrencyCode)).Append(',');
            sb.Append(order.EmployeeId).Append(',');
            // The channel identifies the beneficiary by account; the file carries no national identifier (as the treasury file did not).
            sb.Append(',');
            sb.Append(EscapeCsv(row.EmployeeName)).Append(',');
            sb.Append(Money(row.Line?.GrossPay ?? 0m)).Append(',');
            sb.Append(Money(row.Line?.TotalDeductions ?? 0m)).Append(',');
            sb.Append(Money(row.Line?.NetPay ?? order.Amount)).Append(',');
            sb.Append(EscapeCsv(order.BeneficiaryIban)).Append(',');
            sb.Append(Money(row.Line?.TotalAdjustments ?? 0m)).Append(',');
            sb.Append(order.Id).Append(',');
            sb.Append(Money(order.Amount)).Append('\n');
        }

        var count = rows.Count;
        var total = rows.Sum(r => r.Order.Amount);
        sb.Append(ControlTag).Append(',')
            .Append(count.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(Money(total)).Append(',')
            .Append(batch.Id).Append(',')
            .Append(sequence.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(kind).Append('\n');

        var content = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(sb.ToString());
        return new BuiltPaymentBatchFile(content, count, total, Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant());
    }

    /// <summary>The treasury file's quoting: a value with a quote, a comma or a line break is quoted, its quotes doubled.</summary>
    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return value.Contains('"') || value.Contains(',') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
