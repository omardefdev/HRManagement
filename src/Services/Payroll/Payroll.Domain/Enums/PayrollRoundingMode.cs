namespace Payroll.Domain.Enums;

/// <summary>Maps to <see cref="MidpointRounding"/> for legal money rounding on a config version.</summary>
public enum PayrollRoundingMode
{
    AwayFromZero = 0,
    ToEven = 1
}
