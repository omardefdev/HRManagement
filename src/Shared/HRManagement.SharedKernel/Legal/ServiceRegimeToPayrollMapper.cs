namespace HRManagement.SharedKernel.Legal;

/// <summary>SYR-007: maps HRCore service regime ids to Payroll regime codes.</summary>
public static class ServiceRegimeToPayrollMapper
{
    public const string PayrollLaw50 = "LAW_50_2004";
    public const string PayrollLaw53 = "LAW_53_2021";

    public static string? MapToPayrollCode(string? legalRegimeId) =>
        legalRegimeId switch
        {
            "law50" => PayrollLaw50,
            "law53" => PayrollLaw53,
            _ => null
        };
}
