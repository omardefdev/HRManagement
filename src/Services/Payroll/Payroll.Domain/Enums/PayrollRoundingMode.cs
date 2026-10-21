using System.ComponentModel.DataAnnotations;

namespace Payroll.Domain.Enums;

/// <summary>Maps to <see cref="MidpointRounding"/> for legal money rounding on a config version.</summary>
public enum PayrollRoundingMode
{
    [Display(Name = "بعيداً عن الصفر")]
    AwayFromZero = 0,
    [Display(Name = "تقريب زوجي")]
    ToEven = 1
}
