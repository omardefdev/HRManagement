using HRManagement.ServiceDefaults.CodeGeneration;
using HRManagement.SharedKernel.CodeGeneration;
using Microsoft.EntityFrameworkCore;
using Records.Archive.Domain.Entities;
using Records.Archive.Domain.Enums;
using Records.Archive.Infrastructure.Data;

namespace Records.Archive.Infrastructure.Consumers;

internal static class ArchiveReferenceFactory
{
    public static async Task<string?> CreateReferenceAsync(
        ICodeGenerationClient codeClient,
        string entityPrefix,
        CancellationToken ct)
    {
        var (ok, code, _) = await CodeGenerationGateway.GenerateOrErrorAsync(
            codeClient,
            CodeGenerationRequests.ArchiveReference(entityPrefix),
            ct);
        return ok ? code : null;
    }

    public static async Task<bool> ExistsByTitleAsync(
        ArchiveDbContext db,
        string title,
        CancellationToken ct) =>
        await db.ArchiveItems.AnyAsync(x => x.Title == title, ct);

    public static ArchiveItem CreateItem(string title, string reference, string storageLocation, int retentionYears = 10) =>
        new()
        {
            Title = title,
            ReferenceNumber = reference,
            Format = ArchiveFormat.Digital,
            StorageLocation = storageLocation,
            RetentionPeriodYears = retentionYears
        };
}
