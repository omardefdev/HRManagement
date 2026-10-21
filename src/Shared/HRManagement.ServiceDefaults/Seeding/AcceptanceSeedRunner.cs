using Microsoft.EntityFrameworkCore;

namespace HRManagement.ServiceDefaults.Seeding;

public static class AcceptanceSeedRunner
{
    public static async Task ForEachAgencyAsync<TDbContext>(
        TDbContext db,
        Func<TDbContext, CancellationToken, Task> seedAgency,
        CancellationToken ct = default)
        where TDbContext : DbContext
    {
        var serviceName = typeof(TDbContext).Name;
        await AcceptanceSeedReceiptStore.EnsureTableAsync(db, ct);
        foreach (var agency in AcceptanceSeedCatalog.Agencies)
        {
            if (!await AcceptanceSeedReceiptStore.ShouldApplyAsync(db, agency.Id, serviceName, ct))
                continue;

            using var frame = AcceptanceSeedExecution.Push(agency);
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(ct)
                : null;
            try
            {
                await seedAgency(db, ct);
                await AcceptanceSeedReceiptStore.WriteAppliedAsync(db, agency, serviceName, ct);
                if (transaction is not null)
                    await transaction.CommitAsync(ct);
            }
            catch
            {
                if (transaction is not null)
                    await transaction.RollbackAsync(ct);
                throw;
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }
    }
}
