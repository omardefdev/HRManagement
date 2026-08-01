namespace HRManagement.ServiceDefaults.ValueKpi;

public interface IValueKpiProvider
{
    ValueKpiOptions Options { get; }
    ValueKpiMatrixDocument Matrix { get; }
}

public interface IValueBenefitGuard
{
    string? EnsureLiveDashboardAllowed();
    string? ValidateBenefitMeasure(string measureId);
    string? ValidateKpiPair(ValueKpiDefinition kpi);
    string? ValidateBaseline(string kpiId);
}
