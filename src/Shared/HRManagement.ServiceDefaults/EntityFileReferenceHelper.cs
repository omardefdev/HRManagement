namespace HRManagement.ServiceDefaults;

/// <summary>
/// Holder helpers for file references. Prefer <c>AddFileReferenceOutbox&lt;TContext&gt;()</c>
/// so Register/Release land in FileReferenceOutbox in the same TX as the entity (FILE-001-C);
/// otherwise calls go directly over HTTP via <see cref="IFileReferenceClient"/>.
/// </summary>
public static class EntityFileReferenceHelper
{
    public static async Task ApplyFileIdChangeAsync(
        IFileReferenceClient fileRefs,
        string service,
        string entityType,
        Guid entityId,
        string fieldName,
        Guid? oldFileId,
        Guid? newFileId,
        CancellationToken cancellationToken = default)
    {
        if (oldFileId == newFileId)
            return;

        var holderKey = FileReferenceClientExtensions.BuildHolderKey(service, entityType, entityId, fieldName);

        if (oldFileId.HasValue)
            await fileRefs.ReleaseReferenceAsync(oldFileId.Value, holderKey, cancellationToken);

        if (newFileId.HasValue)
            await fileRefs.RegisterReferenceAsync(newFileId.Value, holderKey, cancellationToken);
    }

    public static async Task ReleaseEntityFileAsync(
        IFileReferenceClient fileRefs,
        string service,
        string entityType,
        Guid entityId,
        string fieldName,
        Guid? fileId,
        CancellationToken cancellationToken = default)
    {
        if (!fileId.HasValue)
            return;

        var holderKey = FileReferenceClientExtensions.BuildHolderKey(service, entityType, entityId, fieldName);
        await fileRefs.ReleaseReferenceAsync(fileId.Value, holderKey, cancellationToken);
    }
}
