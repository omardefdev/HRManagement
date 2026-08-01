using System.Globalization;
using System.Text;
using HRManagement.Integration.Abstractions;
using Microsoft.Extensions.Options;

namespace HRManagement.Integration.FileAdapters;

internal static class IntegrationFileHelper
{
    public static string ResolveOutputDirectory(ExternalIntegrationsOptions options, string subfolder)
    {
        var root = string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? Path.Combine(AppContext.BaseDirectory, "integration-outbox")
            : Path.IsPathRooted(options.OutputDirectory)
                ? options.OutputDirectory
                : Path.Combine(AppContext.BaseDirectory, options.OutputDirectory);

        var dir = Path.Combine(root, subfolder);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string BuildFileName(string prefix, DateOnly period, string extension) =>
        $"{prefix}-{period:yyyyMM}-{DateTime.UtcNow:yyyyMMddHHmmss}.{extension}";

    public static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    public static string FormatDecimal(decimal value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);

    public static async Task WriteUtf8Async(string path, string content, CancellationToken ct)
    {
        await File.WriteAllTextAsync(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), ct);
    }
}
