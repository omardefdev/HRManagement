namespace HRManagement.FileStorage;

public sealed record FileReference(
    Guid FileId,
    string Bucket,
    string ObjectKey,
    string ContentType,
    long SizeBytes,
    string? Checksum = null)
{
    public string StorageKey => $"{Bucket}/{ObjectKey}";

    public static bool TryParseStorageKey(string? storageKey, out FileReference? reference, Guid? fileId = null)
    {
        reference = null;
        if (string.IsNullOrWhiteSpace(storageKey))
            return false;

        var slash = storageKey.IndexOf('/');
        if (slash <= 0 || slash >= storageKey.Length - 1)
            return false;

        var bucket = storageKey[..slash];
        var objectKey = storageKey[(slash + 1)..];
        reference = new FileReference(
            fileId ?? Guid.Empty,
            bucket,
            objectKey,
            "application/octet-stream",
            0);
        return true;
    }
}
