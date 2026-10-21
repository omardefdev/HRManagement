namespace Payroll.Domain.Constants;

/// <summary>Known payroll legal regime identifiers selectable at installation.</summary>
public static class LegalRegimeCodes
{
    public const string Law50_2004 = "LAW_50_2004";
    public const string Law53_2021 = "LAW_53_2021";
    public const string CivilService2026 = "CIVIL_SERVICE_2026";

    public static readonly IReadOnlyList<string> All = [Law50_2004, Law53_2021, CivilService2026];

    public static string DisplayNameAr(string code) => code switch
    {
        Law50_2004 => "قانون العاملين الأساسي 50/2004",
        Law53_2021 => "قانون العاملين الأساسي 53/2021",
        CivilService2026 => "قانون الخدمة المدنية الجديد",
        _ => code
    };
}
