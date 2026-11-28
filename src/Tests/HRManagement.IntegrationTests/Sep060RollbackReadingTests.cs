using HR.Contracts.Domain.Enums;
using HR.Contracts.Infrastructure.Data;
using HR.Contracts.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-60 (P-5, P-18): after a rollback, PLAN-59's tag runs on the SEP060 schema. Its inserts name only the columns its model has, and
/// they succeed — the new columns take their defaults (a legacy Draft, an item marked Manual until reconciled, no link) — and its reads of
/// those columns return the rows as it wrote them. It moves only the old flags; when this tag starts again, the reconciler puts back in
/// line exactly what disagrees, and a second pass changes nothing.
/// </summary>
[Collection("PostgreSql")]
public sealed class Sep060RollbackReadingTests(PostgreSqlFixture fixture)
{
    private static readonly Guid Tenant = Guid.Parse("a0000060-0000-4000-8000-0000000000c1");

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_previous_tag_writes_and_reads_on_this_schema_and_the_reconciler_puts_its_changes_back_in_line()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var admin = await fixture.CreateDatabaseAsync($"sep060r_{Guid.NewGuid():N}"[..40]);
        await using (var db = new ContractsDbContext(new DbContextOptionsBuilder<ContractsDbContext>().UseNpgsql(admin).Options))
            await db.Database.MigrateAsync();

        // The previous tag's insert of a record, its six items and its transaction, in its own columns only.
        var employee = Guid.NewGuid();
        var created = DateTime.UtcNow.AddMinutes(-10);
        var record = await Sep060MigrationPostgreSqlTests.RecordAsync(admin, Tenant, employee, published: false, createdAt: created);
        await Sep060MigrationPostgreSqlTests.OldItemsAsync(admin, Tenant, record);
        var transaction = await Sep060MigrationPostgreSqlTests.TransactionAsync(admin, Tenant, employee, created.AddSeconds(2));

        Assert.Equal("Draft|true|0", await Pol047Pg.ScalarAsync<string>(admin,
            $"""SELECT "Status" || '|' || "IsLegacyDraft"::text || '|' || "SubmissionNo"::text FROM "SeparationRecords" WHERE "Id" = '{record}'"""));
        Assert.Equal(6L, await Pol047Pg.ScalarAsync<long>(admin,
            $"""SELECT count(*) FROM "SeparationChecklists" WHERE "SeparationRecordId" = '{record}' AND "CompletionMode" = 'Manual'"""));

        // Its reads, in its columns, return what it wrote.
        Assert.Equal($"{employee}|Resignation|2026-10-01|false|false", await Pol047Pg.ScalarAsync<string>(admin, $"""
            SELECT "EmployeeId"::text || '|' || "SeparationType" || '|' || "EffectiveDate"::text || '|' || "IsCancelled"::text || '|' || "SeparationEventPublished"::text
            FROM "SeparationRecords" WHERE "Id" = '{record}'
            """));

        // The previous tag publishes it (its publication moved the flag only), and cancels another one.
        await Pol047Pg.ExecAsync(admin, $"""UPDATE "SeparationRecords" SET "SeparationEventPublished" = true WHERE "Id" = '{record}'""");
        await Sep060MigrationPostgreSqlTests.SagaAsync(admin, Tenant, record, "AwaitingParties");
        var other = await Sep060MigrationPostgreSqlTests.RecordAsync(admin, Tenant, Guid.NewGuid(), published: false);
        await Pol047Pg.ExecAsync(admin, $"""UPDATE "SeparationRecords" SET "IsCancelled" = true, "CancelledAt" = now() WHERE "Id" = '{other}'""");

        // What the partial index changes for it: a second open record of one employee is refused (it never checked).
        var duplicate = await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
            Sep060MigrationPostgreSqlTests.RecordAsync(admin, Tenant, employee, published: false));
        Assert.Equal("23505", duplicate.SqlState);
        Assert.Equal(ContractsDbContext.SeparationRecordOpenIndexName, duplicate.ConstraintName);

        // This tag starts again: the reconciler, inside the tenant.
        HRManagement.ServiceDefaults.TenantIdFlow.Set(Tenant);
        try
        {
            SeparationReconcileResult first, second;
            await using (var db = new ContractsDbContext(
                             new DbContextOptionsBuilder<ContractsDbContext>().UseNpgsql(admin).Options, new Msg040FixedTenant(Tenant)))
                first = await SeparationStatusReconciler.ReconcileAsync(db);
            await using (var db = new ContractsDbContext(
                             new DbContextOptionsBuilder<ContractsDbContext>().UseNpgsql(admin).Options, new Msg040FixedTenant(Tenant)))
                second = await SeparationStatusReconciler.ReconcileAsync(db);

            Assert.Equal(2, first.Records);
            Assert.Equal(6, first.Items);
            Assert.Equal(4, first.ItemsAdded);
            Assert.Equal(1, first.Links);
            Assert.Equal(0, second.Total);
        }
        finally
        {
            HRManagement.ServiceDefaults.TenantIdFlow.Set(null);
        }

        Assert.Equal(nameof(SeparationStatus.Executing), await Pol047Pg.ScalarAsync<string>(admin,
            $"""SELECT "Status" FROM "SeparationRecords" WHERE "Id" = '{record}'"""));
        Assert.Equal(nameof(SeparationStatus.Cancelled), await Pol047Pg.ScalarAsync<string>(admin,
            $"""SELECT "Status" FROM "SeparationRecords" WHERE "Id" = '{other}'"""));
        Assert.Equal(record, await Pol047Pg.ScalarAsync<Guid>(admin,
            $"""SELECT "SeparationRecordId" FROM "ServiceTransactions" WHERE "Id" = '{transaction}'"""));
        Assert.Equal("PartyAck:Payroll", await Pol047Pg.ScalarAsync<string>(admin,
            $"""SELECT "CompletionMode" || ':' || "PartyName" FROM "SeparationChecklists" WHERE "SeparationRecordId" = '{record}' AND "Step" = 'PayrollSettled'"""));
        Assert.Equal(10L, await Pol047Pg.ScalarAsync<long>(admin,
            $"""SELECT count(*) FROM "SeparationChecklists" WHERE "SeparationRecordId" = '{record}'"""));
    }
}
