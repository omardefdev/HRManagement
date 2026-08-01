using System.Text.Json;
using HRManagement.TestSupport;
using Payroll.Application.Frd;
using Payroll.Application.Services;
using Payroll.Domain.Constants;
using Payroll.Domain.Entities;

namespace HRManagement.UnitTests;

/// <summary>FRD-001: runtime fraud anomaly engine and four-eyes hold release.</summary>
public class Frd001FraudEngineTests
{
    private readonly FraudAnomalyEngine _engine = CreateEngine();
    private readonly PaymentHoldService _holds = new();

    private static FraudAnomalyEngine CreateEngine()
    {
        var json = global::System.IO.File.ReadAllText(RepoPaths.Resolve("docs", "frd", "anomaly-rules.json"));
        var doc = JsonSerializer.Deserialize<FrdAnomalyRulesDocument>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        return new FraudAnomalyEngine(new InlineFrdRulesProvider(doc));
    }

    private sealed class InlineFrdRulesProvider(FrdAnomalyRulesDocument doc) : IFrdAnomalyRulesProvider
    {
        public FrdAnomalyRulesDocument Document => doc;
        public FrdDetectionRule? FindRule(string ruleId) =>
            doc.DetectionRules.FirstOrDefault(r => string.Equals(r.Id, ruleId, StringComparison.OrdinalIgnoreCase));
        public bool HoldOnProofOfLifeExpiry => doc.ProofOfLife?.HoldOnExpiry ?? true;
        public bool SingleOperatorReleaseBlocked => doc.PaymentHold?.SingleOperatorReleaseBlocked ?? true;
    }

    [Fact]
    public void Ghost_employee_no_attendance_creates_alert_and_hold()
    {
        var runId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var result = _engine.Scan(runId, new[]
        {
            new SalaryLineScanInput(employeeId, 1000m, HasAttendance: false)
        });

        var alert = Assert.Single(result.Alerts);
        Assert.Equal(FraudRuleIds.GhostEmployee, alert.RuleId);
        Assert.Equal(FraudAlertSeverity.Critical, alert.Severity);
        var hold = Assert.Single(result.Holds);
        Assert.Equal(employeeId, hold.EmployeeId);
        Assert.Equal(PaymentHoldStatus.Active, hold.Status);
        Assert.Equal(alert.Id, hold.FraudAlertId);
        Assert.Equal(hold.Id, alert.PaymentHoldId);
    }

    [Fact]
    public void Shared_iban_with_two_or_more_employees_flags()
    {
        var runId = Guid.NewGuid();
        var e1 = Guid.NewGuid();
        var e2 = Guid.NewGuid();
        var result = _engine.Scan(runId, new[]
        {
            new SalaryLineScanInput(e1, 900m, Iban: "SY12 ACCT 0001"),
            new SalaryLineScanInput(e2, 800m, Iban: "SY12ACCT0001")
        });

        Assert.Equal(2, result.Alerts.Count);
        Assert.All(result.Alerts, a => Assert.Equal(FraudRuleIds.SharedIban, a.RuleId));
        Assert.Empty(result.Holds);
    }

    [Fact]
    public void Same_day_hire_and_pay_creates_hold()
    {
        var asOf = new DateTime(2026, 7, 31, 12, 0, 0, DateTimeKind.Utc);
        var employeeId = Guid.NewGuid();
        var result = _engine.Scan(Guid.NewGuid(), new[]
        {
            new SalaryLineScanInput(employeeId, 1200m, HireDate: asOf.Date, HasAttendance: true, HasSourceRecord: true)
        }, asOfUtc: asOf);

        var alert = Assert.Single(result.Alerts.Where(a => a.RuleId == FraudRuleIds.SameDayHireAndPay));
        Assert.Equal(FraudAlertSeverity.High, alert.Severity);
        Assert.Contains(result.Holds, h => h.EmployeeId == employeeId && h.FraudAlertId == alert.Id);
    }

    [Fact]
    public void Single_operator_cannot_release_hold()
    {
        var hold = new PaymentHold
        {
            EmployeeId = Guid.NewGuid(),
            Reason = "FR-01 test",
            Status = PaymentHoldStatus.Active
        };
        var user = Guid.NewGuid();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            _holds.ReleaseHold(hold, user, user, "cleared"));

        Assert.Contains("four-eyes", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PaymentHoldStatus.Active, hold.Status);
    }

    [Fact]
    public void Four_eyes_release_succeeds_when_different_users()
    {
        var hold = new PaymentHold
        {
            EmployeeId = Guid.NewGuid(),
            Reason = "FR-01 test",
            Status = PaymentHoldStatus.Active
        };
        var releaser = Guid.NewGuid();
        var approver = Guid.NewGuid();

        _holds.ReleaseHold(hold, releaser, approver, "Verified in person", Guid.NewGuid());

        Assert.Equal(PaymentHoldStatus.Released, hold.Status);
        Assert.Equal(releaser, hold.ReleasedBy);
        Assert.Equal(approver, hold.ReleaseApprovedBy);
        Assert.NotNull(hold.ReleasedAtUtc);
    }

    [Fact]
    public void Proof_of_life_expiry_creates_hold()
    {
        var employeeId = Guid.NewGuid();
        var asOf = new DateTime(2026, 7, 31, 0, 0, 0, DateTimeKind.Utc);
        var record = new ProofOfLifeRecord
        {
            EmployeeId = employeeId,
            VerificationDate = asOf.AddYears(-1),
            ExpiryDate = asOf.AddDays(-1),
            Method = "in-person",
            Status = ProofOfLifeStatus.Valid
        };

        var result = _engine.EvaluateProofOfLifeExpiry(new[] { record }, asOfUtc: asOf);

        Assert.Equal(ProofOfLifeStatus.Expired, record.Status);
        var alert = Assert.Single(result.Alerts);
        Assert.Equal(FraudRuleIds.ProofOfLifeExpiry, alert.RuleId);
        var hold = Assert.Single(result.Holds);
        Assert.Equal(PaymentHoldType.ProofOfLife, hold.HoldType);
        Assert.Equal(employeeId, hold.EmployeeId);
    }
}
