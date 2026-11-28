using System.Text.RegularExpressions;

namespace HRManagement.E2E;

/// <summary>
/// FIX-CAREERS-REF: the public portal's confirmation page (<c>/careers/submitted</c>), read as the candidate reads it. The reference
/// number is taken from the page's badge; a test that proves the page takes it from there and only then checks it against the
/// database. The number never goes into a URL, a log or an evidence file name.
/// </summary>
public static partial class CareersConfirmationPage
{
    public const string Path = "/careers/submitted";

    /// <summary>The marker of the page that has no number to show (a reload, an expired flash cookie, a direct visit).</summary>
    public const string UnavailableMarker = "data-reference-unavailable";

    /// <summary>The page's own words when it has no number (Views/PublicCareers/Submitted.cshtml).</summary>
    public const string UnavailableText = "يظهر الرقم المرجعي مرة واحدة فقط بعد تقديم الطلب";

    /// <summary>The text of the reference badge, or null when the page shows none (HTML-decoded or not: the number holds no entity).</summary>
    public static string? ReadReference(string html)
    {
        var badge = BadgeRegex().Match(html);
        var value = badge.Success ? badge.Groups[1].Value.Trim() : string.Empty;
        return value.Length == 0 ? null : value;
    }

    /// <summary>The number the page shows; fails with the step's name (never the page) when the badge is missing or empty.</summary>
    public static string RequireReference(string html, string step)
    {
        var reference = ReadReference(html);
        Assert.True(reference is not null,
            $"{step}: the confirmation page shows no reference number (an empty badge: HR.Core's flash cookie did not reach it).");
        return reference!;
    }

    [GeneratedRegex(@"<span class=""careers-badge"" data-reference-number[^>]*>\s*<bdi[^>]*>([^<]*)</bdi>")]
    private static partial Regex BadgeRegex();
}
