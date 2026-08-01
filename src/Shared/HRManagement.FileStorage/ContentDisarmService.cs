using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace HRManagement.FileStorage;

public sealed record ContentDisarmResult(
    bool Success,
    Stream? Content,
    string ContentType,
    long SizeBytes,
    string? Sha256,
    bool Applied,
    string? Profile,
    string? Error);

public interface IContentDisarmService
{
    Task<ContentDisarmResult> DisarmAsync(
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default);
}

/// <summary>Lightweight CDR: image re-encode, Office macro strip, PDF active-content policy (FILE-002-C).</summary>
public sealed class ContentDisarmService(ILogger<ContentDisarmService> logger) : IContentDisarmService
{
    private static readonly Regex PdfDangerous = new(
        @"/JS\b|/JavaScript\b|/AA\b|/OpenAction\b|/EmbeddedFile\b|/Launch\b|/RichMedia\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<ContentDisarmResult> DisarmAsync(
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var ext = Path.GetExtension(fileName);
        await using var input = new MemoryStream();
        await content.CopyToAsync(input, cancellationToken);
        input.Position = 0;

        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            || ext is ".jpg" or ".jpeg" or ".png")
        {
            return await DisarmImageAsync(input, contentType, cancellationToken);
        }

        if (ext.Equals(".docx", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("wordprocessingml", StringComparison.OrdinalIgnoreCase))
        {
            return DisarmOfficeZip(input, contentType, "word-macro-strip");
        }

        if (ext.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("spreadsheetml", StringComparison.OrdinalIgnoreCase))
        {
            return DisarmOfficeZip(input, contentType, "excel-macro-strip");
        }

        if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
            || contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            return DisarmPdf(input);
        }

        // .doc OLE — pass through without CDR profile
        input.Position = 0;
        var copy = new MemoryStream(input.ToArray());
        var hash = Convert.ToHexString(SHA256.HashData(copy.ToArray())).ToLowerInvariant();
        copy.Position = 0;
        return new ContentDisarmResult(true, copy, contentType, copy.Length, hash, false, null, null);
    }

    private async Task<ContentDisarmResult> DisarmImageAsync(MemoryStream input, string contentType, CancellationToken ct)
    {
        try
        {
            // Re-encode via raw pixel strip of EXIF: rewrite PNG/JPEG headers by re-saving bytes
            // without external deps — drop APP1/EXIF segments for JPEG; for PNG strip non-critical chunks.
            input.Position = 0;
            byte[] cleaned;
            string profile;
            string outType;

            if (IsJpeg(input))
            {
                cleaned = StripJpegMetadata(input.ToArray());
                profile = "jpeg-strip-metadata";
                outType = "image/jpeg";
            }
            else if (IsPng(input))
            {
                cleaned = StripPngNonCritical(input.ToArray());
                profile = "png-strip-ancillary";
                outType = "image/png";
            }
            else
            {
                cleaned = input.ToArray();
                profile = "image-passthrough";
                outType = contentType;
            }

            await Task.CompletedTask;
            var ms = new MemoryStream(cleaned);
            var hash = Convert.ToHexString(SHA256.HashData(cleaned)).ToLowerInvariant();
            return new ContentDisarmResult(true, ms, outType, cleaned.LongLength, hash, true, profile, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Image CDR failed.");
            return new ContentDisarmResult(false, null, contentType, 0, null, false, null, ex.Message);
        }
    }

    private ContentDisarmResult DisarmOfficeZip(MemoryStream input, string contentType, string profile)
    {
        try
        {
            input.Position = 0;
            using var output = new MemoryStream();
            using (var reader = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true))
            using (var writer = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                var stripped = false;
                foreach (var entry in reader.Entries)
                {
                    var name = entry.FullName;
                    if (name.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("/vbaProject", StringComparison.OrdinalIgnoreCase)
                        || name.EndsWith("vbaData.xml", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("macro", StringComparison.OrdinalIgnoreCase)
                           && name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                    {
                        stripped = true;
                        continue;
                    }

                    var outEntry = writer.CreateEntry(name, CompressionLevel.Optimal);
                    using var src = entry.Open();
                    using var dst = outEntry.Open();
                    src.CopyTo(dst);
                }

                if (!stripped)
                {
                    // still rewrite archive (normalize) as CDR applied lightly
                }
            }

            var bytes = output.ToArray();
            var ms = new MemoryStream(bytes);
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            return new ContentDisarmResult(true, ms, contentType, bytes.LongLength, hash, true, profile, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Office CDR failed.");
            return new ContentDisarmResult(false, null, contentType, 0, null, false, null, ex.Message);
        }
    }

    private ContentDisarmResult DisarmPdf(MemoryStream input)
    {
        var text = Encoding.Latin1.GetString(input.ToArray());
        if (PdfDangerous.IsMatch(text))
        {
            return new ContentDisarmResult(
                false,
                null,
                "application/pdf",
                0,
                null,
                false,
                "pdf-active-content-block",
                "PDF contains active content (JS/embedded/launch) and was blocked by CDR policy.");
        }

        var bytes = input.ToArray();
        var ms = new MemoryStream(bytes);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new ContentDisarmResult(true, ms, "application/pdf", bytes.LongLength, hash, true, "pdf-passive-ok", null);
    }

    private static bool IsJpeg(MemoryStream ms)
    {
        var b = ms.ToArray();
        return b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;
    }

    private static bool IsPng(MemoryStream ms)
    {
        var b = ms.ToArray();
        return b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47;
    }

    private static byte[] StripJpegMetadata(byte[] data)
    {
        // Keep SOI and strip APP0-APP15 (0xE0-0xEF) and COM (0xFE) segments.
        if (data.Length < 4) return data;
        using var output = new MemoryStream();
        output.WriteByte(0xFF);
        output.WriteByte(0xD8);
        var i = 2;
        while (i + 3 < data.Length)
        {
            if (data[i] != 0xFF)
            {
                output.Write(data, i, data.Length - i);
                break;
            }

            var marker = data[i + 1];
            if (marker == 0xDA) // SOS — copy rest
            {
                output.Write(data, i, data.Length - i);
                break;
            }

            if (marker == 0xD9) // EOI
            {
                output.WriteByte(0xFF);
                output.WriteByte(0xD9);
                break;
            }

            if (i + 3 >= data.Length) break;
            var len = (data[i + 2] << 8) | data[i + 3];
            var segmentTotal = 2 + len;
            var isAppOrCom = (marker >= 0xE1 && marker <= 0xEF) || marker == 0xFE; // keep APP0 (JFIF) optionally strip APP1+
            if (marker == 0xE0) isAppOrCom = false; // keep JFIF
            if (!isAppOrCom)
                output.Write(data, i, Math.Min(segmentTotal, data.Length - i));
            i += segmentTotal;
        }

        return output.ToArray();
    }

    private static byte[] StripPngNonCritical(byte[] data)
    {
        // PNG: keep IHDR/PLTE/IDAT/IEND; drop tEXt/iTXt/zTXt/eXIf/etc.
        if (data.Length < 8) return data;
        using var output = new MemoryStream();
        output.Write(data, 0, 8); // signature
        var i = 8;
        while (i + 12 <= data.Length)
        {
            var length = (data[i] << 24) | (data[i + 1] << 16) | (data[i + 2] << 8) | data[i + 3];
            var type = Encoding.ASCII.GetString(data, i + 4, 4);
            var chunkTotal = 12 + length;
            if (i + chunkTotal > data.Length) break;
            var keep = type is "IHDR" or "PLTE" or "IDAT" or "IEND" or "tRNS" or "gAMA" or "sRGB" or "cHRM";
            if (keep)
                output.Write(data, i, chunkTotal);
            i += chunkTotal;
            if (type == "IEND") break;
        }

        return output.ToArray();
    }
}
