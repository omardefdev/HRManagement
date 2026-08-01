namespace Records.Archive.Domain.Enums;

public enum ArchiveChecklistStep
{
    Classified = 0,
    Ingested = 1,
    Held = 2,
    Released = 3,
    DisposeRequested = 4,
    DisposeApproved = 5,
    Disposed = 6
}
