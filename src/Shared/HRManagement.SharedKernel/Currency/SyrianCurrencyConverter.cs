namespace HRManagement.SharedKernel.Currency;

/// <summary>
/// SYR-004: converts between old Syrian Lira (SYP-old) and new Syrian Lira (SYP-new)
/// at a fixed 100:1 ratio. All amounts use decimal for lossless arithmetic.
/// </summary>
public static class SyrianCurrencyConverter
{
    public const int ConversionRatio = 100;
    public const string OldCurrencyCode = "SYP";
    public const string NewCurrencyCode = "SYP-NEW";
    public static readonly DateTime CutoverDate = new(2026, 7, 31, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Convert old lira amount to new lira (divide by 100).</summary>
    public static decimal ToNewLira(decimal oldAmount)
    {
        return oldAmount / ConversionRatio;
    }

    /// <summary>Convert new lira amount to old lira (multiply by 100).</summary>
    public static decimal ToOldLira(decimal newAmount)
    {
        return newAmount * ConversionRatio;
    }

    /// <summary>Determine the currency era for a given date.</summary>
    public static CurrencyEra GetEra(DateTime date)
    {
        return date >= CutoverDate ? CurrencyEra.NewLira : CurrencyEra.OldLira;
    }

    /// <summary>
    /// Normalize an amount to the target era, converting if the source era differs.
    /// Returns the amount unchanged if already in the target era.
    /// </summary>
    public static decimal Normalize(decimal amount, CurrencyEra sourceEra, CurrencyEra targetEra)
    {
        if (sourceEra == targetEra)
            return amount;

        return targetEra == CurrencyEra.NewLira
            ? ToNewLira(amount)
            : ToOldLira(amount);
    }

    /// <summary>Round according to official rounding policy (banker's rounding to 2 decimal places).</summary>
    public static decimal RoundOfficial(decimal amount)
    {
        return Math.Round(amount, 2, MidpointRounding.ToEven);
    }
}

public enum CurrencyEra
{
    OldLira = 0,
    NewLira = 1
}
