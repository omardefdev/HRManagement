using System.Globalization;

namespace HRManagement.E2E;

/// <summary>
/// PLAN-67 (D-67-2): a page reads a <c>datetime-local</c> value on the user's clock (<c>Asia/Damascus</c>) and stores it in UTC. A test
/// that means an instant posts it the way a user in Damascus types it; posting the UTC digits would store a time three hours early.
/// </summary>
public static class DamascusClock
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Damascus");

    /// <summary>The <c>datetime-local</c> text (<c>yyyy-MM-ddTHH:mm</c>) for an instant given in UTC.</summary>
    public static string Input(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
}
