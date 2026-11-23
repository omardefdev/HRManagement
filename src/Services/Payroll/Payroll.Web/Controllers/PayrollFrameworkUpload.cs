using System.Text;
using Payroll.Application.Features.PayrollConfigurations;

namespace Payroll.Web.Controllers;

/// <summary>
/// PLAN-54 (P-4): reading an uploaded legal pack, on the form and on the API: one JSON document of a bounded size, valid UTF-8. The
/// text is handed to the importer and to nothing else: no log line writes the file or a value from it, and an error says what is
/// required without repeating what was sent.
/// </summary>
public static class PayrollFrameworkUpload
{
    /// <summary>The request's limit: the pack, and a form's or an envelope's few other fields.</summary>
    public const long MaxRequestBytes = PayrollFrameworkPackReader.MaxBytes + (64 * 1024);

    public const string FileRequired = "اختر ملف الحزمة القانونية";
    public const string NotJsonFile = "ملف الحزمة يجب أن يكون ملف JSON (‎.json)";
    public const string TooLarge = "ملف الحزمة أكبر من الحد المسموح (512 كيلوبايت)";
    public const string NotUtf8 = "ملف الحزمة ليس نصاً بترميز UTF-8";

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static async Task<(string? Json, string? Error)> ReadAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return (null, FileRequired);
        if (file.Length > PayrollFrameworkPackReader.MaxBytes)
            return (null, TooLarge);
        var isJson = file.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                     || (file.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) ?? false);
        if (!isJson)
            return (null, NotJsonFile);

        await using var stream = file.OpenReadStream();
        return await ReadAsync(stream, PayrollFrameworkPackReader.MaxBytes, ct);
    }

    /// <summary>Reads at most <paramref name="maxBytes"/> bytes of UTF-8 text; one byte more is a refusal.</summary>
    public static async Task<(string? Text, string? Error)> ReadAsync(Stream stream, int maxBytes, CancellationToken ct)
    {
        var buffer = new byte[maxBytes + 1];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read), ct);
            if (n == 0) break;
            read += n;
        }

        if (read > maxBytes)
            return (null, TooLarge);

        try
        {
            var start = read >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF ? 3 : 0;
            return (StrictUtf8.GetString(buffer, start, read - start), null);
        }
        catch (DecoderFallbackException)
        {
            return (null, NotUtf8);
        }
    }
}
