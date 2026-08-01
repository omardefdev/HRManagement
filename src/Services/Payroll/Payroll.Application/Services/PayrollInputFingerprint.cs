using System.Security.Cryptography;
using System.Text;
using Payroll.Domain.Entities;

namespace Payroll.Application.Services;

/// <summary>PERF-001: stable fingerprint of payroll inputs for an employee (snapshot drift detection).</summary>
public static class PayrollInputFingerprint
{
    public static string Compute(
        IEnumerable<AllowanceRecord> allowances,
        IEnumerable<DeductionRecord> deductions,
        IEnumerable<PayrollAdjustment> adjustments,
        IEnumerable<AbsenceDeduction> absences)
    {
        var sb = new StringBuilder(256);
        foreach (var a in allowances.OrderBy(x => x.Id))
            sb.Append("A:").Append(a.Id).Append(':').Append(a.Amount.ToString("F4"))
                .Append(':').Append(a.AllowanceTypeId).Append(':')
                .Append(a.EffectiveFrom.ToString("O")).Append(':')
                .Append(a.EffectiveTo?.ToString("O") ?? "-").Append(';');

        foreach (var d in deductions.OrderBy(x => x.Id))
            sb.Append("D:").Append(d.Id).Append(':').Append(d.Amount.ToString("F4"))
                .Append(':').Append(d.DeductionType).Append(';');

        foreach (var adj in adjustments.OrderBy(x => x.Id))
            sb.Append("J:").Append(adj.Id).Append(':').Append(adj.Amount.ToString("F4"))
                .Append(':').Append(adj.AdjustmentReason ?? "-").Append(';');

        foreach (var ab in absences.OrderBy(x => x.Id))
            sb.Append("B:").Append(ab.Id).Append(':').Append(ab.AbsentDays)
                .Append(':').Append(ab.Amount.ToString("F4")).Append(':')
                .Append(ab.PeriodStart.ToString("O")).Append(':')
                .Append(ab.PeriodEnd.ToString("O")).Append(';');

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
