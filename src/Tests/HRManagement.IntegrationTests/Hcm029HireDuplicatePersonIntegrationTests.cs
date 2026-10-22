using System.Data.Common;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.Legal;
using HRManagement.SharedKernel.Tenancy;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.IntegrationTests;

/// <summary>
/// HCM-029 (A4): on PostgreSQL, a person inserted concurrently (after the closure's national-id check) makes the
/// employee insert hit the unique <c>(TenantId, NationalIdHash)</c> index. The closure maps the 23505 to
/// <see cref="HireClosureErrorKind.DuplicatePerson"/>, rolls back, and leaves no row in the MassTransit outbox.
/// Skips without Docker.
/// </summary>
[Trait("Category", "Testcontainers")]
public class Hcm029HireDuplicatePersonIntegrationTests(Hcm026PostgresFixture fixture) : IClassFixture<Hcm026PostgresFixture>
{
    [SkippableFact]
    public async Task Unique_violation_from_a_concurrent_insert_is_a_duplicate_person_and_writes_no_outbox_message()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);

        var tenantId = Guid.NewGuid();
        var (raceAppId, raceNationalId) = await SeedAcceptedOfferAsync(tenantId, "RACE");
        var (okAppId, okNationalId) = await SeedAcceptedOfferAsync(tenantId, "OK");

        var concurrentInsert = new ConcurrentPersonInterceptor(async () =>
        {
            await using var other = fixture.CreateContext(tenantId);
            other.Employees.Add(NewEmployee(tenantId, raceNationalId, "موظف متزامن"));
            await other.SaveChangesAsync();
        });

        await using var provider = BuildProvider(tenantId, concurrentInsert);
        var bus = provider.GetRequiredService<IBusControl>();
        await bus.StartAsync();
        try
        {
            var outboxBefore = await CountOutboxAsync(tenantId);

            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HRCoreDbContext>();
                var closure = await CreateClosureAsync(scope.ServiceProvider);
                var app = await db.RecruitmentApplications.SingleAsync(a => a.Id == raceAppId);

                var ex = await Assert.ThrowsAsync<HireClosureException>(() => closure.HireAndCreateEmployeeAsync(
                    app, raceNationalId, "مرشح", null, "السباق", DateTime.UtcNow.AddYears(-30), Gender.Male, null,
                    DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), "PERMANENT"));

                Assert.Equal(HireClosureErrorKind.DuplicatePerson, ex.Kind);
                Assert.IsType<Npgsql.PostgresException>(ex.InnerException?.InnerException);
                Assert.True(concurrentInsert.Fired);

                // Nothing left to flush: a later SaveChanges in the same scope writes nothing.
                Assert.Equal(0, await db.SaveChangesAsync());
            }

            Assert.Equal(outboxBefore, await CountOutboxAsync(tenantId));
            await using (var verify = fixture.CreateContext(tenantId))
            {
                var app = await verify.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == raceAppId);
                Assert.Equal(RecruitmentApplicationStatus.OfferAccepted, app.Status);
                Assert.Null(app.HiredEmployeeId);
                Assert.Equal(1, await verify.Employees.CountAsync(e => e.NationalIdHash == PiiMask.Sha256Hex(raceNationalId)));
            }

            // Control: a normal hire through the same outbox does write messages.
            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HRCoreDbContext>();
                var closure = await CreateClosureAsync(scope.ServiceProvider);
                var app = await db.RecruitmentApplications.SingleAsync(a => a.Id == okAppId);
                await closure.HireAndCreateEmployeeAsync(
                    app, okNationalId, "مرشح", null, "ناجح", DateTime.UtcNow.AddYears(-30), Gender.Female, null,
                    DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), "PERMANENT");
            }

            Assert.True(await CountOutboxAsync(tenantId) >= outboxBefore + 2);
        }
        finally
        {
            await bus.StopAsync();
        }
    }

    private ServiceProvider BuildProvider(Guid tenantId, ConcurrentPersonInterceptor interceptor)
    {
        var connectionString = ConnectionString(tenantId);
        var tenant = new FixedTenant(tenantId);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenant);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Recruitment:RejectSiblingApplicationsOnHire"] = "true",
                ["Recruitment:LegacyHireWithoutOffer"] = "false"
            })
            .Build());
        services.AddScoped(_ => new HRCoreDbContext(
            new DbContextOptionsBuilder<HRCoreDbContext>()
                .UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsAssembly(typeof(HRCoreDbContext).Assembly.GetName().Name))
                .AddInterceptors(new TenantIdStampInterceptor(tenant), interceptor)
                .Options,
            tenant));
        services.AddMassTransit(x =>
        {
            x.AddEntityFrameworkOutbox<HRCoreDbContext>(o =>
            {
                o.UsePostgres();
                o.UseBusOutbox(b => b.DisableDeliveryService());
            });
            x.UsingInMemory((context, cfg) => cfg.ConfigureEndpoints(context));
        });
        return services.BuildServiceProvider();
    }

    private static Task<RecruitmentHireClosureService> CreateClosureAsync(IServiceProvider sp) =>
        RecruitmentLegalSourceTestSupport.CreateHireClosureServiceAsync(
            sp.GetRequiredService<HRCoreDbContext>(),
            sp.GetRequiredService<IPublishEndpoint>(),
            sp.GetRequiredService<IConfiguration>());

    private string ConnectionString(Guid tenantId)
    {
        using var db = fixture.CreateContext(tenantId);
        return db.Database.GetConnectionString()!;
    }

    private async Task<long> CountOutboxAsync(Guid tenantId)
    {
        await using var db = fixture.CreateContext(tenantId);
        await using var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT COUNT(*) FROM "OutboxMessage" """;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private async Task<(Guid AppId, string NationalId)> SeedAcceptedOfferAsync(Guid tenantId, string suffix)
    {
        await using var db = fixture.CreateContext(tenantId);
        await RecruitmentLegalSourceTestSupport.SeedLegislationAndRegimesAsync(db);
        var position = await Hcm026PostgresFixture.SeedVacantPositionAsync(db, tenantId);
        var requisition = Hcm026PostgresFixture.NewRequisition(tenantId, position.Id, RequisitionStatus.Published);
        requisition.LegalSourceId = Syr001LegislationIds.Law50_2004;
        db.RecruitmentRequisitions.Add(requisition);

        var nationalId = $"29{Math.Abs(Guid.NewGuid().GetHashCode()) % 100_000_000:D8}";
        var app = new RecruitmentApplication
        {
            TenantId = tenantId,
            ApplicantFullName = $"مرشح {suffix}",
            NationalId = nationalId,
            RequisitionId = requisition.Id,
            AppliedPosition = requisition.JobTitle,
            AppliedAt = DateTime.UtcNow,
            Status = RecruitmentApplicationStatus.OfferAccepted
        };
        db.RecruitmentApplications.Add(app);
        await db.SaveChangesAsync();

        var offer = new OfferLetter
        {
            TenantId = tenantId,
            RecruitmentApplicationId = app.Id,
            FundedPositionId = position.Id,
            OfferNumber = $"OFF-H29-{suffix}-{Guid.NewGuid():N}"[..30],
            Status = OfferStatus.Accepted,
            OfferedSalary = 5000,
            GradeCode = "G5",
            ProposedStartDate = DateTime.UtcNow.AddDays(30),
            AcceptedDate = DateTime.UtcNow,
            LegalSourceId = Syr001LegislationIds.Law50_2004
        };
        db.OfferLetters.Add(offer);
        await db.SaveChangesAsync();
        app.ActiveOfferLetterId = offer.Id;
        await db.SaveChangesAsync();
        return (app.Id, nationalId);
    }

    private static Employee NewEmployee(Guid tenantId, string nationalId, string fullName) => new()
    {
        TenantId = tenantId,
        NationalId = nationalId,
        NationalIdHash = PiiMask.Sha256Hex(nationalId),
        FirstName = fullName.Split(' ')[0],
        LastName = fullName.Split(' ')[^1],
        FullName = fullName,
        BirthDate = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        CurrentStatus = EmploymentStatus.Active
    };

    /// <summary>Runs <paramref name="onCheck"/> once, right after the closure's national-id lookup executed.</summary>
    private sealed class ConcurrentPersonInterceptor(Func<Task> onCheck) : DbCommandInterceptor
    {
        public bool Fired { get; private set; }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (!Fired && command.CommandText.Contains(HirePersonMatchService.NationalIdQueryTag, StringComparison.Ordinal))
            {
                Fired = true;
                await onCheck();
            }

            return result;
        }
    }

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId => tenantId;
        public bool IsPlatformOperator => false;
        public Guid RequireTenantId() => tenantId;
    }
}
