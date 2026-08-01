using System.IO.Compression;
using System.Text;

namespace HRManagement.FileStorage;

public sealed record FileContentProbeResult(
    bool Success,
    string? DetectedContentType,
    string? Error);

/// <summary>Magic-byte / Office ZIP structure probe (FILE-002-A).</summary>
public static class FileContentProbe
{
    public static async Task<FileContentProbeResult> ProbeAsync(
        Stream content,
        string fileName,
        FileUploadConstraints constraints,
        CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(extension) || !constraints.AllowedExtensions.Contains(extension))
            return new FileContentProbeResult(false, null, "نوع الملف غير مدعوم.");

        if (!content.CanSeek)
            return new FileContentProbeResult(false, null, "تعذر قراءة محتوى الملف.");

        var originalPos = content.Position;
        try
        {
            content.Position = 0;
            var header = new byte[16];
            var read = await content.ReadAsync(header.AsMemory(0, header.Length), cancellationToken);
            content.Position = 0;

            if (read < 4)
                return new FileContentProbeResult(false, null, "محتوى الملف قصير جداً.");

            var detected = Detect(header.AsSpan(0, read), extension, content);
            if (detected is null)
                return new FileContentProbeResult(false, null, "تعارض بين امتداد الملف ومحتواه (magic bytes).");

            if (constraints.AllowedContentTypes is not null
                && !constraints.AllowedContentTypes.Contains(detected))
                return new FileContentProbeResult(false, detected, "نوع المحتوى المكتشف غير مسموح.");

            if (IsOfficeZipExtension(extension))
            {
                var zipCheck = OfficeArchiveLimits.Validate(content, cancellationToken);
                content.Position = 0;
                if (!zipCheck.Success)
                    return new FileContentProbeResult(false, detected, zipCheck.Error);
            }

            return new FileContentProbeResult(true, detected, null);
        }
        finally
        {
            if (content.CanSeek)
                content.Position = originalPos;
        }
    }

    private static string? Detect(ReadOnlySpan<byte> header, string extension, Stream full)
    {
        // PNG
        if (header.Length >= 8
            && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
            return MatchesExt(extension, ".png") ? "image/png" : null;

        // JPEG
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return MatchesExt(extension, ".jpg", ".jpeg") ? "image/jpeg" : null;

        // PDF
        if (header.Length >= 4
            && header[0] == (byte)'%' && header[1] == (byte)'P' && header[2] == (byte)'D' && header[3] == (byte)'F')
            return MatchesExt(extension, ".pdf") ? "application/pdf" : null;

        // OLE Compound (.doc)
        if (header.Length >= 8
            && header[0] == 0xD0 && header[1] == 0xCF && header[2] == 0x11 && header[3] == 0xE0)
            return MatchesExt(extension, ".doc") ? "application/msword" : null;

        // ZIP-based (docx / xlsx / generic)
        if (header.Length >= 4 && header[0] == (byte)'P' && header[1] == (byte)'K')
        {
            if (MatchesExt(extension, ".docx"))
                return LooksLikeOpenXml(full, "word/") ? "application/vnd.openxmlformats-officedocument.wordprocessingml.document" : null;
            if (MatchesExt(extension, ".xlsx"))
                return LooksLikeOpenXml(full, "xl/") ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" : null;
            // Extension claims image/pdf but content is ZIP → confusion
            return null;
        }

        return null;
    }

    private static bool LooksLikeOpenXml(Stream stream, string requiredPrefix)
    {
        var pos = stream.Position;
        try
        {
            stream.Position = 0;
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var hasContentTypes = zip.Entries.Any(e =>
                string.Equals(e.FullName, "[Content_Types].xml", StringComparison.OrdinalIgnoreCase));
            var hasPart = zip.Entries.Any(e =>
                e.FullName.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase));
            return hasContentTypes && hasPart;
        }
        catch
        {
            return false;
        }
        finally
        {
            stream.Position = pos;
        }
    }

    private static bool MatchesExt(string extension, params string[] allowed) =>
        allowed.Any(a => string.Equals(extension, a, StringComparison.OrdinalIgnoreCase));

    private static bool IsOfficeZipExtension(string extension) =>
        extension.Equals(".docx", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Zip-bomb / nested archive limits for Office Open XML containers.</summary>
public static class OfficeArchiveLimits
{
    public const int MaxEntries = 5_000;
    public const long MaxUncompressedBytes = 100L * 1024 * 1024;
    public const double MaxCompressionRatio = 100.0;
    public const int MaxEntryNameLength = 512;

    public static (bool Success, string? Error) Validate(Stream content, CancellationToken cancellationToken = default)
    {
        var pos = content.Position;
        try
        {
            content.Position = 0;
            using var zip = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
            if (zip.Entries.Count > MaxEntries)
                return (false, "أرشيف المكتب يتجاوز الحد الأقصى للمداخل.");

            long totalUncompressed = 0;
            foreach (var entry in zip.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.FullName.Length > MaxEntryNameLength)
                    return (false, "اسم مدخل الأرشيف طويل جداً.");
                if (entry.FullName.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(entry.FullName))
                    return (false, "مسار مدخل أرشيف غير آمن.");

                totalUncompressed += entry.Length;
                if (totalUncompressed > MaxUncompressedBytes)
                    return (false, "الحجم غير المضغوط لأرشيف المكتب يتجاوز الحد.");

                if (entry.CompressedLength > 0)
                {
                    var ratio = (double)entry.Length / entry.CompressedLength;
                    if (ratio > MaxCompressionRatio && entry.Length > 1_000_000)
                        return (false, "نسبة تضخّم الأرشيف مرتفعة جداً (اشتباه zip bomb).");
                }
            }

            return (true, null);
        }
        catch (InvalidDataException)
        {
            return (false, "أرشيف المكتب تالف أو غير صالح.");
        }
        finally
        {
            content.Position = pos;
        }
    }
}
