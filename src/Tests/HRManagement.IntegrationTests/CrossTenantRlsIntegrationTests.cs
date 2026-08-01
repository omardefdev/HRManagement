using System.Data;
using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Infrastructure.Data;
using Auth.Directory.Domain.Entities;
using Auth.Directory.Infrastructure.Data;
using Compliance.Domain.Entities;
using Compliance.Infrastructure.Data;
using Dev.Analytics.Domain.Entities;
using Dev.Analytics.Infrastructure.Data;
using File.Storage.Domain.Entities;
using File.Storage.Infrastructure.Data;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Infrastructure.Data;
using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HR.Health.Domain.Entities;
using HR.Health.Infrastructure.Data;
using HR.Movements.Domain.Entities;
using HR.Movements.Infrastructure.Data;
using HR.Training.Domain.Entities;
using HR.Training.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.Tenancy;
using IDCard.Domain.Entities;
using IDCard.Infrastructure.Data;
using IT.Infrastructure.Domain.Entities;
using IT.Infra.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Moq;
using Npgsql;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;
using Records.Archive.Domain.Entities;
using Records.Archive.Infrastructure.Data;

namespace HRManagement.IntegrationTests;

/// <summary>
/// TEN-002/003: EF filters + PostgreSQL RLS isolate tenants across all 14 service databases.
/// </summary>
[Collection("PostgreSql")]
public class CrossTenantRlsIntegrationTests
{
    private readonly PostgreSqlFixture _fixture;

    public CrossTenantRlsIntegrationTests(PostgreSqlFixture fixture) => _fixture = fixture;

    private static ITenantContext Ctx(Guid tenantId, bool platform = false)
    {
        var m = new Mock<ITenantContext>();
        m.Setup(t => t.TenantId).Returns(tenantId);
        m.Setup(t => t.IsPlatformOperator).Returns(platform);
        m.Setup(t => t.RequireTenantId()).Returns(tenantId);
        return m.Object;
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Rls_hides_rows_when_session_tenant_differs()
    {
        Skip.IfNot(PostgreSqlFixture.IsEnabled, "Set RUN_TESTCONTAINERS=true and start Docker to run this test.");

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var dbName = TestDatabaseNames.Unique("ten3_basic_");
        var ownerCs = await _fixture.CreateDatabaseAsync(dbName);

        var optionsA = new DbContextOptionsBuilder<ContractsDbContext>()
            .UseNpgsql(ownerCs, npgsql =>
                npgsql.MigrationsAssembly(typeof(ContractsDbContext).Assembly.GetName().Name))
            .AddInterceptors(
                new TenantIdStampInterceptor(Ctx(tenantA)),
                new NpgsqlTenantSessionInterceptor(Ctx(tenantA)))
            .Options;

        await using (var db = new ContractsDbContext(optionsA, Ctx(tenantA)))
        {
            await db.Database.MigrateAsync();
            db.ContractTypes.Add(new ContractType
            {
                TenantId = tenantA,
                Code = TestDatabaseNames.Truncate("A-" + Guid.NewGuid().ToString("N"), 12),
                NameAr = "A",
                NameEn = "A"
            });
            await db.SaveChangesAsync();
        }

        await using var conn = new NpgsqlConnection(await _fixture.CreateAppConnectionAsync(dbName));
        await conn.OpenAsync();
        await using (var set = conn.CreateCommand())
        {
            set.CommandText = $"SELECT set_config('app.current_tenant', '{tenantB:D}', false)";
            await set.ExecuteNonQueryAsync();
        }

        await using var countCmd = conn.CreateCommand();
        countCmd.CommandText = """SELECT COUNT(*) FROM "ContractTypes" WHERE "NameAr" = 'A'""";
        var count = Convert.ToInt64(await countCmd.ExecuteScalarAsync());
        Assert.Equal(0, count);

        await using (var setA = conn.CreateCommand())
        {
            setA.CommandText = $"SELECT set_config('app.current_tenant', '{tenantA:D}', false)";
            await setA.ExecuteNonQueryAsync();
        }

        countCmd.CommandText = """SELECT COUNT(*) FROM "ContractTypes" WHERE "NameAr" = 'A'""";
        var countA = Convert.ToInt64(await countCmd.ExecuteScalarAsync());
        Assert.True(countA >= 1);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableTheory]
    [InlineData("HRCore")]
    [InlineData("HRContracts")]
    [InlineData("Payroll")]
    [InlineData("HRMovements")]
    [InlineData("HRTraining")]
    [InlineData("HRHealth")]
    [InlineData("AuthDirectory")]
    [InlineData("FileStorage")]
    [InlineData("IDCard")]
    [InlineData("Compliance")]
    [InlineData("RecordsArchive")]
    [InlineData("AdminLogistics")]
    [InlineData("ITInfra")]
    [InlineData("DevAnalytics")]
    public async Task Rls_isolates_all_fourteen_service_databases(string serviceName)
    {
        Skip.IfNot(PostgreSqlFixture.IsEnabled, "Set RUN_TESTCONTAINERS=true and start Docker to run this test.");

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var marker = TestDatabaseNames.Truncate($"RLS-{serviceName}-{Guid.NewGuid():N}", 40);
        var dbName = TestDatabaseNames.Unique($"ten3_{serviceName.ToLowerInvariant()}_");
        var cs = await _fixture.CreateDatabaseAsync(dbName);

        var (table, seed) = await MigrateAndSeedAsync(serviceName, cs, tenantA, marker);

        await using var conn = new NpgsqlConnection(await _fixture.CreateAppConnectionAsync(dbName));
        await conn.OpenAsync();

        await using (var setB = conn.CreateCommand())
        {
            setB.CommandText = $"SELECT set_config('app.current_tenant', '{tenantB:D}', false)";
            await setB.ExecuteNonQueryAsync();
        }

        await using var countCmd = conn.CreateCommand();
        countCmd.CommandText = table switch
        {
            "Employees" => $"""SELECT COUNT(*) FROM "{table}" WHERE "FullName" = '{marker}'""",
            "ContractTypes" => $"""SELECT COUNT(*) FROM "{table}" WHERE "NameAr" = '{marker}'""",
            "MovementNotices" => $"""SELECT COUNT(*) FROM "{table}" WHERE "Title" = '{marker}'""",
            "TrainingCourses" => $"""SELECT COUNT(*) FROM "{table}" WHERE "TitleAr" = '{marker}'""",
            "MedicalRecords" => $"""SELECT COUNT(*) FROM "{table}" WHERE "MedicalHistory" = '{marker}'""",
            "Roles" => $"""SELECT COUNT(*) FROM "{table}" WHERE "Name" = '{marker}'""",
            "Files" => $"""SELECT COUNT(*) FROM "{table}" WHERE "OriginalName" = '{marker}'""",
            "CardDesigns" => $"""SELECT COUNT(*) FROM "{table}" WHERE "Name" = '{marker}'""",
            "Complaints" => $"""SELECT COUNT(*) FROM "{table}" WHERE "Subject" = '{marker}'""",
            "ArchiveItems" => $"""SELECT COUNT(*) FROM "{table}" WHERE "Title" = '{marker}'""",
            "Vendors" => $"""SELECT COUNT(*) FROM "{table}" WHERE "Name" = '{marker}'""",
            "ServerDevices" => $"""SELECT COUNT(*) FROM "{table}" WHERE "Hostname" = '{marker}'""",
            "Reports" => $"""SELECT COUNT(*) FROM "{table}" WHERE "Title" = '{marker}'""",
            "PayrollRuns" => $"""SELECT COUNT(*) FROM "{table}" WHERE "TenantId" = '{tenantA:D}'""",
            _ => $"""SELECT COUNT(*) FROM "{table}" WHERE "TenantId" = '{tenantA:D}'"""
        };

        var hidden = Convert.ToInt64(await countCmd.ExecuteScalarAsync());
        Assert.Equal(0, hidden);

        await using (var setA = conn.CreateCommand())
        {
            setA.CommandText = $"SELECT set_config('app.current_tenant', '{tenantA:D}', false)";
            await setA.ExecuteNonQueryAsync();
        }

        var visible = Convert.ToInt64(await countCmd.ExecuteScalarAsync());
        Assert.True(visible >= 1, $"{serviceName}: expected RLS to show tenant A rows on {table}");
        _ = seed;
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Rls_covers_OrgUnitSnapshots_separately()
    {
        Skip.IfNot(PostgreSqlFixture.IsEnabled, "Set RUN_TESTCONTAINERS=true and start Docker to run this test.");

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var dbName = TestDatabaseNames.Unique("ten3_ousnap_");
        var cs = await _fixture.CreateDatabaseAsync(dbName);

        var options = new DbContextOptionsBuilder<ContractsDbContext>()
            .UseNpgsql(cs, npgsql => npgsql.MigrationsAssembly(typeof(ContractsDbContext).Assembly.GetName().Name))
            .AddInterceptors(
                new TenantIdStampInterceptor(Ctx(tenantA)),
                new NpgsqlTenantSessionInterceptor(Ctx(tenantA)))
            .Options;

        await using (var db = new ContractsDbContext(options, Ctx(tenantA)))
        {
            await db.Database.MigrateAsync();
            db.OrgUnitSnapshots.Add(new OrgUnitSnapshot
            {
                Id = Guid.NewGuid(),
                TenantId = tenantA,
                Code = "ROOT",
                NameAr = "SnapA",
                MaterializedPath = "/"
            });
            await db.SaveChangesAsync();
        }

        await using var conn = new NpgsqlConnection(await _fixture.CreateAppConnectionAsync(dbName));
        await conn.OpenAsync();
        await using (var set = conn.CreateCommand())
        {
            set.CommandText = $"SELECT set_config('app.current_tenant', '{tenantB:D}', false)";
            await set.ExecuteNonQueryAsync();
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """SELECT COUNT(*) FROM "OrgUnitSnapshots" WHERE "NameAr" = 'SnapA'""";
        Assert.Equal(0L, Convert.ToInt64(await cmd.ExecuteScalarAsync()));

        await using (var setA = conn.CreateCommand())
        {
            setA.CommandText = $"SELECT set_config('app.current_tenant', '{tenantA:D}', false)";
            await setA.ExecuteNonQueryAsync();
        }

        Assert.True(Convert.ToInt64(await cmd.ExecuteScalarAsync()) >= 1);
    }

    /// <summary>
    /// TEN-002C: a USING-only policy still allows writing another tenant's rows.
    /// WITH CHECK must reject the insert outright.
    /// </summary>
    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Rls_rejects_cross_tenant_insert()
    {
        Skip.IfNot(PostgreSqlFixture.IsEnabled, "Set RUN_TESTCONTAINERS=true and start Docker to run this test.");

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var dbName = TestDatabaseNames.Unique("ten3_wcheck_");
        var ownerCs = await _fixture.CreateDatabaseAsync(dbName);

        await (await MigrateAsync<ContractsDbContext>(ownerCs, tenantA, typeof(ContractsDbContext))).DisposeAsync();

        await using var conn = new NpgsqlConnection(await _fixture.CreateAppConnectionAsync(dbName));
        await conn.OpenAsync();
        await using (var set = conn.CreateCommand())
        {
            set.CommandText = $"SELECT set_config('app.current_tenant', '{tenantA:D}', false)";
            await set.ExecuteNonQueryAsync();
        }

        await using var insert = conn.CreateCommand();
        insert.CommandText =
            """
            INSERT INTO "ContractTypes" ("Id", "TenantId", "Code", "NameAr", "NameEn", "CreatedAt", "UpdatedAt")
            VALUES (gen_random_uuid(), @tenant, 'XT-1', 'x', 'x', now(), now())
            """;
        insert.Parameters.Add(new NpgsqlParameter("tenant", tenantB));

        var ex = await Assert.ThrowsAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
    }

    /// <summary>
    /// Guards the root cause of TEN-002C: RLS enabled but not forced, or a policy without
    /// WITH CHECK, leaves isolation inert for the owning role.
    /// </summary>
    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Every_tenant_table_is_rls_forced_with_check()
    {
        Skip.IfNot(PostgreSqlFixture.IsEnabled, "Set RUN_TESTCONTAINERS=true and start Docker to run this test.");

        var dbName = TestDatabaseNames.Unique("ten3_verify_");
        var cs = await _fixture.CreateDatabaseAsync(dbName);

        await using var db = await MigrateAsync<ContractsDbContext>(cs, Guid.NewGuid(), typeof(ContractsDbContext));

        var unhardened = await DatabaseMigrationExtensions.ListUnhardenedTenantTablesAsync(db, CancellationToken.None);
        Assert.True(
            unhardened.Count == 0,
            $"Tenant tables missing ENABLE/FORCE/WITH CHECK: {string.Join(", ", unhardened)}");
    }

    private static async Task<(string Table, Guid SeedId)> MigrateAndSeedAsync(
        string serviceName, string cs, Guid tenantA, string marker)
    {
        switch (serviceName)
        {
            case "HRCore":
                {
                    var db = await MigrateAsync<HRCoreDbContext>(cs, tenantA, typeof(HRCoreDbContext));
                    var e = new Employee
                    {
                        TenantId = tenantA,
                        FullName = marker,
                        FirstName = "A",
                        LastName = "B",
                        NationalId = marker[..Math.Min(20, marker.Length)],
                        BirthDate = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                        Gender = Gender.Male
                    };
                    db.Employees.Add(e);
                    await db.SaveChangesAsync();
                    await db.DisposeAsync();
                    return ("Employees", e.Id);
                }
            case "HRContracts":
                {
                    var db = await MigrateAsync<ContractsDbContext>(cs, tenantA, typeof(ContractsDbContext));
                    var e = new ContractType { TenantId = tenantA, Code = marker[..12], NameAr = marker, NameEn = "A" };
                    db.ContractTypes.Add(e);
                    await db.SaveChangesAsync();
                    await db.DisposeAsync();
                    return ("ContractTypes", e.Id);
                }
            case "Payroll":
                {
                    var db = await MigrateAsync<PayrollDbContext>(cs, tenantA, typeof(PayrollDbContext));
                    var e = new PayrollRun
                    {
                        TenantId = tenantA,
                        PeriodStart = DateTime.UtcNow.Date,
                        PeriodEnd = DateTime.UtcNow.Date.AddDays(30),
                        RunDate = DateTime.UtcNow.Date,
                        Status = PayrollRunStatus.Draft
                    };
                    db.PayrollRuns.Add(e);
                    await db.SaveChangesAsync();
                    await db.DisposeAsync();
                    return ("PayrollRuns", e.Id);
                }
            case "HRMovements":
                {
                    var db = await MigrateAsync<MovementsDbContext>(cs, tenantA, typeof(MovementsDbContext));
                    var e = new MovementNotice { TenantId = tenantA, Title = marker, IssueDate = DateOnly.FromDateTime(DateTime.UtcNow) };
                    db.MovementNotices.Add(e);
                    await db.SaveChangesAsync();
                    await db.DisposeAsync();
                    return ("MovementNotices", e.Id);
                }
            case "HRTraining":
                {
                    var db = await MigrateAsync<TrainingDbContext>(cs, tenantA, typeof(TrainingDbContext));
                    var e = new TrainingCourse { TenantId = tenantA, Code = marker[..12], TitleAr = marker, DurationDays = 1 };
                    db.TrainingCourses.Add(e);
                    await db.SaveChangesAsync();
                    await db.DisposeAsync();
                    return ("TrainingCourses", e.Id);
                }
            case "HRHealth":
                {
                    var db = await MigrateAsync<HealthDbContext>(cs, tenantA, typeof(HealthDbContext));
                    var e = new MedicalRecord { TenantId = tenantA, EmployeeId = Guid.NewGuid(), MedicalHistory = marker };
                    db.MedicalRecords.Add(e);
                    await db.SaveChangesAsync();
                    await db.DisposeAsync();
                    return ("MedicalRecords", e.Id);
                }
            case "AuthDirectory":
                {
                    var db = await MigrateAsync<AuthDirectoryDbContext>(cs, tenantA, typeof(AuthDirectoryDbContext));
                    var e = new Role { TenantId = tenantA, Name = marker };
                    db.Roles.Add(e);
                    await db.SaveChangesAsync();
                    await db.DisposeAsync();
                    return ("Roles", e.Id);
                }
            case "FileStorage":
                {
                    var db = await MigrateAsync<FileStorageDbContext>(cs, tenantA, typeof(FileStorageDbContext));
                    var e = new FileRecord
                    {
                        TenantId = tenantA,
                        OriginalName = marker,
                        Extension = ".bin",
                        Bucket = "t",
                        ObjectKey = Guid.NewGuid().ToString("N"),
                        Size = 1
                    };
                    db.Files.Add(e);
                    await db.SaveChangesAsync();
                    await db.DisposeAsync();
                    return ("Files", e.Id);
                }
            case "IDCard":
                {
                    var db = await MigrateAsync<IDCardDbContext>(cs, tenantA, typeof(IDCardDbContext));
                    var e = new CardDesign { TenantId = tenantA, Name = marker, ValidityDays = 365 };
                    db.CardDesigns.Add(e);
                    await db.SaveChangesAsync();
                    await db.DisposeAsync();
                    return ("CardDesigns", e.Id);
                }
            case "Compliance":
                {
                    var db = await MigrateAsync<ComplianceDbContext>(cs, tenantA, typeof(ComplianceDbContext));
                    var e = new Complaint
                    {
                        TenantId = tenantA,
                        FiledBy = "tester",
                        Subject = marker,
                        FiledAt = DateTime.UtcNow
                    };
                    db.Complaints.Add(e);
                    await db.SaveChangesAsync();
                    await db.DisposeAsync();
                    return ("Complaints", e.Id);
                }
            case "RecordsArchive":
                {
                    var db = await MigrateAsync<ArchiveDbContext>(cs, tenantA, typeof(ArchiveDbContext));
                    var e = new ArchiveItem
                    {
                        TenantId = tenantA,
                        Title = marker,
                        ReferenceNumber = marker[..Math.Min(20, marker.Length)]
                    };
                    db.ArchiveItems.Add(e);
                    await db.SaveChangesAsync();
                    await db.DisposeAsync();
                    return ("ArchiveItems", e.Id);
                }
            case "AdminLogistics":
                {
                    var db = await MigrateAsync<AdminLogisticsDbContext>(cs, tenantA, typeof(AdminLogisticsDbContext));
                    var e = new Vendor { TenantId = tenantA, Name = marker };
                    db.Vendors.Add(e);
                    await db.SaveChangesAsync();
                    await db.DisposeAsync();
                    return ("Vendors", e.Id);
                }
            case "ITInfra":
                {
                    var db = await MigrateAsync<ITInfraDbContext>(cs, tenantA, typeof(ITInfraDbContext));
                    var e = new ServerDevice { TenantId = tenantA, Hostname = marker };
                    db.ServerDevices.Add(e);
                    await db.SaveChangesAsync();
                    await db.DisposeAsync();
                    return ("ServerDevices", e.Id);
                }
            case "DevAnalytics":
                {
                    var db = await MigrateAsync<AnalyticsDbContext>(cs, tenantA, typeof(AnalyticsDbContext));
                    var e = new Report { TenantId = tenantA, Title = marker };
                    db.Reports.Add(e);
                    await db.SaveChangesAsync();
                    await db.DisposeAsync();
                    return ("Reports", e.Id);
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(serviceName), serviceName, null);
        }
    }

    private static async Task<TContext> MigrateAsync<TContext>(string cs, Guid tenantA, Type ctxType)
        where TContext : DbContext
    {
        var optionsBuilder = new DbContextOptionsBuilder<TContext>()
            .UseNpgsql(cs, npgsql => npgsql.MigrationsAssembly(ctxType.Assembly.GetName().Name))
            .AddInterceptors(
                new TenantIdStampInterceptor(Ctx(tenantA)),
                new NpgsqlTenantSessionInterceptor(Ctx(tenantA)));

        var db = (TContext)Activator.CreateInstance(typeof(TContext), optionsBuilder.Options, Ctx(tenantA))!;
        await db.Database.MigrateAsync();
        return db;
    }
}
