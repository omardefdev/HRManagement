namespace HRManagement.FileStorage;

public sealed record FileUploadConstraints(
    long MaxBytes,
    IReadOnlySet<string> AllowedExtensions,
    IReadOnlySet<string>? AllowedContentTypes = null)
{
    public static FileUploadConstraints Photo { get; } = new(
        2 * 1024 * 1024,
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp" },
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png", "image/webp" });

    public static FileUploadConstraints Document { get; } = new(
        25 * 1024 * 1024,
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pdf", ".doc", ".docx", ".jpg", ".jpeg", ".png" },
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "application/pdf",
            "application/msword",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "image/jpeg",
            "image/png"
        });

    public static FileUploadConstraints Certificate { get; } = new(
        10 * 1024 * 1024,
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pdf", ".jpg", ".jpeg", ".png" },
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "application/pdf",
            "image/jpeg",
            "image/png"
        });
}
