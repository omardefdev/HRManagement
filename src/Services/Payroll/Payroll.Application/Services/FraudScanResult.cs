using Payroll.Domain.Entities;

namespace Payroll.Application.Services;

/// <summary>Result of an FRD-001 anomaly scan (alerts + payment holds).</summary>
public sealed class FraudScanResult
{
    public FraudScanResult(IReadOnlyList<FraudAlert> alerts, IReadOnlyList<PaymentHold> holds)
    {
        Alerts = alerts;
        Holds = holds;
    }

    public IReadOnlyList<FraudAlert> Alerts { get; }
    public IReadOnlyList<PaymentHold> Holds { get; }
}
