using System.Globalization;

namespace HRManagement.SharedKernel.Localization;

public static class LocalizedNameExtensions
{
    public static string GetLocalizedName(this ILocalizedName? entity, CultureInfo? culture = null)
    {
        if (entity is null)
            return "—";

        culture ??= CultureInfo.CurrentUICulture;
        var isArabic = culture.Name.StartsWith("ar", StringComparison.OrdinalIgnoreCase);

        if (isArabic)
            return FirstNonEmpty(entity.NameAr, entity.NameEn, entity.Code) ?? "—";

        return FirstNonEmpty(entity.NameEn, entity.NameAr, entity.Code) ?? "—";
    }

    public static string? GetLocalizedNameOrNull(this ILocalizedName? entity, CultureInfo? culture = null)
    {
        if (entity is null)
            return null;

        var name = entity.GetLocalizedName(culture);
        return name == "—" ? null : name;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }
}
