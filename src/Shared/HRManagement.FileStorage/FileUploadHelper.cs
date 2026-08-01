using Microsoft.AspNetCore.Http;

namespace HRManagement.FileStorage;

public static class FileUploadHelper
{
    public static (bool Success, string? Error) Validate(IFormFile file, FileUploadConstraints constraints)
    {
        if (file.Length == 0)
            return (false, "الملف فارغ.");

        if (file.Length > constraints.MaxBytes)
            return (false, $"حجم الملف يجب ألا يتجاوز {constraints.MaxBytes / (1024 * 1024)} ميغابايت.");

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(extension) || !constraints.AllowedExtensions.Contains(extension))
            return (false, "نوع الملف غير مدعوم.");

        if (constraints.AllowedContentTypes is not null
            && !string.IsNullOrWhiteSpace(file.ContentType)
            && !constraints.AllowedContentTypes.Contains(file.ContentType))
            return (false, "نوع المحتوى غير مدعوم.");

        return (true, null);
    }

    public static async Task<(bool Success, FileReference? Reference, string? Error)> TryUploadAsync(
        IFileStorageService storage,
        string bucket,
        string prefix,
        IFormFile? file,
        FileUploadConstraints constraints,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
            return (true, null, null);

        var validation = Validate(file, constraints);
        if (!validation.Success)
            return (false, null, validation.Error);

        try
        {
            var reference = await storage.UploadFormFileAsync(bucket, prefix, file, constraints, cancellationToken);
            return (true, reference, null);
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    public static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "file.bin" : name;
    }

    public static FileReference ToReference(Guid fileId, string? storageKey, string contentType = "application/octet-stream", long sizeBytes = 0)
    {
        if (!FileReference.TryParseStorageKey(storageKey, out var reference, fileId) || reference is null)
            throw new InvalidOperationException("Storage key غير صالح.");
        return reference with { FileId = fileId, ContentType = contentType, SizeBytes = sizeBytes };
    }
}
