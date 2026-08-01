using Payroll.Application.Frd;
using Payroll.Domain.Constants;
using Payroll.Domain.Entities;

namespace Payroll.Application.Services;

/// <summary>
/// FRD-001 continuous anti-fraud anomaly engine (FR-01..FR-08 + proof-of-life expiry).
/// Rule metadata (category/severity/action) is loaded from docs/frd/anomaly-rules.json.
/// </summary>
public class FraudAnomalyEngine(IFrdAnomalyRulesProvider rules)
{
    public const string ActionPaymentHold = "payment-hold";
    public const string ActionFlagForReview = "flag-for-review";

    public FraudScanResult Scan(
        Guid payrollRunId,
        IReadOnlyList<SalaryLineScanInput> lines,
        DateTime? asOfUtc = null)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var asOf = asOfUtc ?? DateTime.UtcNow;
        var alerts = new List<FraudAlert>();
        var holds = new List<PaymentHold>();

        foreach (var line in lines)
        {
            if (line.HasAttendance == false)
            {
                AddFinding(alerts, holds, payrollRunId, line.EmployeeId, FraudRuleIds.GhostEmployee,
                    "Payroll entry with no attendance or physical verification.");
            }

            if (line.IsDeceased == true)
            {
                AddFinding(alerts, holds, payrollRunId, line.EmployeeId, FraudRuleIds.DeceasedBeneficiary,
                    "Continued payment after civil registry death notification.");
            }

            if (line.HireDate.HasValue && IsSameDayHireAndPay(line.HireDate.Value, asOf))
            {
                AddFinding(alerts, holds, payrollRunId, line.EmployeeId, FraudRuleIds.SameDayHireAndPay,
                    $"Employee hired {line.HireDate:yyyy-MM-dd} and paid in the same cycle.");
            }

            if (line.HasSourceRecord == false)
            {
                AddFinding(alerts, holds, payrollRunId, line.EmployeeId, FraudRuleIds.MissingSourceRecord,
                    "Payroll line with no traceable HR action or decision.");
            }

            if (line.ExpectedNetPay is { } expected && expected > 0)
            {
                var deviation = Math.Abs(line.NetPay - expected) / expected;
                if (deviation >= 2.0m)
                {
                    AddFinding(alerts, holds, payrollRunId, line.EmployeeId, FraudRuleIds.AnomalousCompensation,
                        $"Net pay {line.NetPay} outside expected range (expected {expected}).");
                }
            }

            if (line.ApproverActionCount is { } count && count >= 50)
            {
                AddFinding(alerts, holds, payrollRunId, line.EmployeeId, FraudRuleIds.SingleApproverVolume,
                    $"Approver action volume abnormally high ({count}).");
            }
        }

        foreach (var group in lines
                     .Where(l => !string.IsNullOrWhiteSpace(l.NationalIdHash))
                     .GroupBy(l => l.NationalIdHash!.Trim(), StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Select(x => x.EmployeeId).Distinct().Count() >= 2))
        {
            foreach (var line in group)
            {
                AddFinding(alerts, holds, payrollRunId, line.EmployeeId, FraudRuleIds.DuplicateEmployment,
                    $"Same national-id hash shared across {group.Select(x => x.EmployeeId).Distinct().Count()} employees.");
            }
        }

        foreach (var group in lines
                     .Where(l => !string.IsNullOrWhiteSpace(l.Iban))
                     .GroupBy(l => NormalizeIban(l.Iban!), StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Select(x => x.EmployeeId).Distinct().Count() >= 2))
        {
            foreach (var line in group)
            {
                AddFinding(alerts, holds, payrollRunId, line.EmployeeId, FraudRuleIds.SharedIban,
                    $"IBAN shared by {group.Select(x => x.EmployeeId).Distinct().Count()} employees.");
            }
        }

        return new FraudScanResult(alerts, holds);
    }

    public FraudScanResult EvaluateProofOfLifeExpiry(
        IReadOnlyList<ProofOfLifeRecord> records,
        DateTime? asOfUtc = null,
        Guid? payrollRunId = null)
    {
        ArgumentNullException.ThrowIfNull(records);

        if (!rules.HoldOnProofOfLifeExpiry)
            return new FraudScanResult([], []);

        var asOf = asOfUtc ?? DateTime.UtcNow;
        var alerts = new List<FraudAlert>();
        var holds = new List<PaymentHold>();

        foreach (var record in records)
        {
            var expired = record.Status == ProofOfLifeStatus.Expired
                          || (record.Status == ProofOfLifeStatus.Valid && record.ExpiryDate < asOf);

            if (!expired)
                continue;

            if (record.Status == ProofOfLifeStatus.Valid)
                record.Status = ProofOfLifeStatus.Expired;

            AddFinding(alerts, holds, payrollRunId, record.EmployeeId, FraudRuleIds.ProofOfLifeExpiry,
                $"Proof-of-life expired on {record.ExpiryDate:yyyy-MM-dd} (method={record.Method}).",
                PaymentHoldType.ProofOfLife);
        }

        return new FraudScanResult(alerts, holds);
    }

    private static bool IsSameDayHireAndPay(DateTime hireDate, DateTime asOf) =>
        hireDate.Date == asOf.Date;

    private static string NormalizeIban(string iban) =>
        iban.Replace(" ", "", StringComparison.Ordinal).Trim();

    private void AddFinding(
        List<FraudAlert> alerts,
        List<PaymentHold> holds,
        Guid? payrollRunId,
        Guid employeeId,
        string ruleId,
        string details,
        string holdType = PaymentHoldType.Fraud)
    {
        var rule = rules.FindRule(ruleId);
        var category = rule?.Category ?? ruleId;
        var severity = rule?.Severity ?? FraudAlertSeverity.Medium;
        var action = rule?.Action ?? ActionFlagForReview;

        var alert = new FraudAlert
        {
            PayrollRunId = payrollRunId,
            EmployeeId = employeeId,
            RuleId = ruleId,
            Category = category,
            Severity = severity,
            Status = FraudAlertStatus.Open,
            Details = details,
            DetectedAtUtc = DateTime.UtcNow
        };

        if (string.Equals(action, ActionPaymentHold, StringComparison.OrdinalIgnoreCase))
        {
            var hold = new PaymentHold
            {
                EmployeeId = employeeId,
                PayrollRunId = payrollRunId,
                FraudAlertId = alert.Id,
                Reason = $"{ruleId}: {details}",
                HoldType = holdType,
                Status = PaymentHoldStatus.Active,
                HeldAtUtc = DateTime.UtcNow
            };
            alert.PaymentHoldId = hold.Id;
            holds.Add(hold);
        }

        alerts.Add(alert);
    }
}
