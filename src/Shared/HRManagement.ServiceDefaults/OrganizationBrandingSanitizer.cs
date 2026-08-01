using System.Text.RegularExpressions;

namespace HRManagement.ServiceDefaults;

public static partial class OrganizationBrandingSanitizer
{
    [GeneratedRegex("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled)]
    private static partial Regex HexColorPattern();

    public const string DefaultPrimaryColor = "#6b1f2a";

    public static string SanitizePrimaryColor(string? value) =>
        !string.IsNullOrWhiteSpace(value) && HexColorPattern().IsMatch(value.Trim())
            ? value.Trim()
            : DefaultPrimaryColor;
}
