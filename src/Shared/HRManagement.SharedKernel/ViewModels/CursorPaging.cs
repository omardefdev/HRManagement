namespace HRManagement.SharedKernel.ViewModels;

/// <summary>PERF-002: keyset/cursor page request for large append-only or time-ordered sets.</summary>
public sealed class CursorPageRequest
{
    /// <summary>Opaque cursor from previous page (base64 or "ticks|guid").</summary>
    public string? Cursor { get; set; }

    public int PageSize { get; set; } = SearchFilterBase.DefaultPageSize;

    public void Normalize(int maxPageSize = SearchFilterBase.MaxPageSize) =>
        PageSize = SearchFilterBase.ClampPageSize(PageSize, maxPageSize);
}

public sealed class CursorPageResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public string? NextCursor { get; init; }
    public bool HasMore { get; init; }
    public int PageSize { get; init; }
}

/// <summary>Helpers to encode/decode keyset cursors (time + id).</summary>
public static class KeysetCursor
{
    public static string Encode(DateTimeOffset occurredAtUtc, Guid id) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{occurredAtUtc.UtcTicks}|{id:N}"));

    public static string Encode(DateTimeOffset occurredAtUtc, long id) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{occurredAtUtc.UtcTicks}|{id}"));

    public static bool TryDecode(string? cursor, out DateTimeOffset occurredAtUtc, out Guid id)
    {
        occurredAtUtc = default;
        id = default;
        if (!TrySplit(cursor, out var ticks, out var idPart)) return false;
        if (!Guid.TryParse(idPart, out id)) return false;
        occurredAtUtc = new DateTimeOffset(ticks, TimeSpan.Zero);
        return true;
    }

    public static bool TryDecodeLong(string? cursor, out DateTimeOffset occurredAtUtc, out long id)
    {
        occurredAtUtc = default;
        id = default;
        if (!TrySplit(cursor, out var ticks, out var idPart)) return false;
        if (!long.TryParse(idPart, out id)) return false;
        occurredAtUtc = new DateTimeOffset(ticks, TimeSpan.Zero);
        return true;
    }

    private static bool TrySplit(string? cursor, out long ticks, out string idPart)
    {
        ticks = 0;
        idPart = "";
        if (string.IsNullOrWhiteSpace(cursor)) return false;
        try
        {
            var raw = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor.Trim()));
            var parts = raw.Split('|', 2);
            if (parts.Length != 2) return false;
            if (!long.TryParse(parts[0], out ticks)) return false;
            idPart = parts[1];
            return true;
        }
        catch
        {
            return false;
        }
    }
}
