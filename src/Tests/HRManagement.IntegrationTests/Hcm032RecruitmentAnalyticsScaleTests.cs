using System.Data.Common;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Recruitment.Analytics;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Testcontainers.PostgreSql;

namespace HRManagement.IntegrationTests;

/// <summary>
/// HCM-032 (PLAN-32, P5): recruitment analytics at scale on a migrated PostgreSQL — 50,000 applications, 250,000 events,
/// 2,000 requisitions, 20,000 offers, 5,000 workflow instances, 20 org units. The <c>Index</c> set of metric calls for a
/// 12-month period must take &lt; 3 s unrestricted and &lt; 1.5 s for one branch (measured after one warm-up run, which
/// compiles the EF queries; the cold run is recorded too). Every SQL statement of the measured run is captured and
/// <c>EXPLAIN</c>ed; plans and timings go to <c>artifacts/hcm032-perf/</c>.
/// Opt-in: <c>HCM032_PERF=1</c> (<c>perf/hcm032-recruitment-analytics-scale.ps1</c>).
/// </summary>
[Trait("Category", "Hcm032Scale")]
public class Hcm032RecruitmentAnalyticsScaleTests : IAsyncLifetime
{
    private const int Units = 20;
    private const int Requisitions = 2_000;
    private const int ApplicationsPerRequisition = 25;
    private const int EventsPerApplication = 5;
    private const int OffersPerRequisition = 10;
    private const int VacantWithoutRequisition = 200;
    private static readonly TimeSpan UnrestrictedBudget = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan BranchBudget = TimeSpan.FromMilliseconds(1500);
    private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

    private PostgreSqlContainer? _postgres;
    private string? _unavailable;
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly List<Guid> _units = [];

    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("HCM032_PERF") != "1")
            return;
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
            await _postgres.StartAsync();
            await using var db = CreateContext();
            await db.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            _unavailable = $"Docker/PostgreSQL unavailable: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }

    private HRCoreDbContext CreateContext(SqlCapture? capture = null)
    {
        var builder = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseNpgsql(_postgres!.GetConnectionString(), o => o.MigrationsAssembly(typeof(HRCoreDbContext).Assembly.GetName().Name));
        if (capture is not null)
            builder.AddInterceptors(capture);
        return new HRCoreDbContext(builder.Options, new FixedTenant(_tenant));
    }

    [SkippableFact]
    public async Task Index_metric_calls_stay_within_the_budget()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("HCM032_PERF") == "1", "Set HCM032_PERF=1 (perf/hcm032-recruitment-analytics-scale.ps1).");
        Skip.If(_unavailable is not null, _unavailable);

        var seeding = Stopwatch.StartNew();
        var counts = await SeedAsync();
        seeding.Stop();
        await using (var db = CreateContext())
            await db.Database.ExecuteSqlRawAsync("ANALYZE");

        var filter = RecruitmentMetricsFilter.DefaultPeriod(Now, 12);
        var unrestricted = OrgAccessScope.Unrestricted();
        var branch = OrgAccessScope.Restricted(new HashSet<Guid> { _units[0] });

        var cold = await MeasureAsync(filter, unrestricted, null);
        var capture = new SqlCapture();
        var warm = await MeasureAsync(filter, unrestricted, capture);
        var branchCold = await MeasureAsync(filter, branch, null);
        var branchWarm = await MeasureAsync(filter, branch, null);
        var plans = await ExplainAsync(capture);
        var seqScans = plans.Where(p => p.Plan.Contains("Seq Scan on \"Recruitment", StringComparison.Ordinal)
                || p.Plan.Contains("Seq Scan on \"OfferLetters\"", StringComparison.Ordinal)
                || p.Plan.Contains("Seq Scan on \"WorkflowInstances\"", StringComparison.Ordinal))
            .Select(p => p.Sql.Length > 160 ? p.Sql[..160] : p.Sql)
            .ToList();

        var passed = warm.Elapsed < UnrestrictedBudget && branchWarm.Elapsed < BranchBudget;
        WriteArtifacts(new
        {
            scenario = "hcm032-recruitment-analytics-scale",
            measuredAtUtc = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
            counts,
            period = new { from = filter.From.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), to = filter.To.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) },
            seedingMs = seeding.ElapsedMilliseconds,
            unrestrictedColdMs = cold.Elapsed.TotalMilliseconds,
            unrestrictedMs = warm.Elapsed.TotalMilliseconds,
            unrestrictedSteps = warm.Steps,
            branchColdMs = branchCold.Elapsed.TotalMilliseconds,
            branchMs = branchWarm.Elapsed.TotalMilliseconds,
            branchSteps = branchWarm.Steps,
            budgetUnrestrictedMs = UnrestrictedBudget.TotalMilliseconds,
            budgetBranchMs = BranchBudget.TotalMilliseconds,
            statements = capture.Commands.Count,
            seqScansOnRecruitmentTables = seqScans,
            cohort = warm.Cohort,
            passed
        }, plans);

        Assert.Equal(Requisitions * ApplicationsPerRequisition, counts.Applications);
        Assert.True(warm.Cohort > 0);
        Assert.True(warm.Elapsed < UnrestrictedBudget, $"unrestricted {warm.Elapsed.TotalMilliseconds:0} ms ≥ {UnrestrictedBudget.TotalMilliseconds} ms");
        Assert.True(branchWarm.Elapsed < BranchBudget, $"branch {branchWarm.Elapsed.TotalMilliseconds:0} ms ≥ {BranchBudget.TotalMilliseconds} ms");
    }

    private sealed record Measurement(TimeSpan Elapsed, Dictionary<string, double> Steps, int Cohort);

    /// <summary>The metric calls of <c>RecruitmentReportsController.Index</c>, in its order, on a fresh service (no memo).</summary>
    private async Task<Measurement> MeasureAsync(RecruitmentMetricsFilter filter, OrgAccessScope scope, SqlCapture? capture)
    {
        await using var db = CreateContext(capture);
        var service = new RecruitmentMetricsService(db, Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions()), new FixedClock(Now));
        var steps = new Dictionary<string, double>();
        var total = Stopwatch.StartNew();
        var step = Stopwatch.StartNew();
        async Task<T> Time<T>(string name, Func<Task<T>> call)
        {
            step.Restart();
            var result = await call();
            steps[name] = step.Elapsed.TotalMilliseconds;
            return result;
        }

        var funnel = await Time("funnel", () => service.GetFunnelAsync(filter, scope));
        await Time("channels", () => service.GetChannelMixAsync(filter, scope));
        await Time("timeToFill", () => service.GetTimeToFillAsync(filter, scope));
        await Time("timeToHire", () => service.GetTimeToHireAsync(filter, scope));
        await Time("stageTime", () => service.GetStageTimeAsync(filter, scope));
        await Time("offers", () => service.GetOfferOutcomesAsync(filter, scope));
        await Time("declines", () => service.GetDeclineReasonsAsync(filter, scope));
        await Time("appeals", () => service.GetAppealsAsync(filter, scope));
        await Time("merit", () => service.GetMeritOverridesAsync(filter, scope));
        await Time("vacancies", () => service.GetOpenVacanciesAsync(filter, scope));
        await Time("approvals", () => service.GetApprovalSlaAsync(filter, scope));
        await Time("competitions", () => service.GetCompetitionsAsync(filter, scope));
        await Time("requisitions", () => service.GetRequisitionTableAsync(filter, scope));
        total.Stop();
        return new Measurement(total.Elapsed, steps, funnel.CohortCount);
    }

    // ------------------------------------------------------------------ seed

    public sealed record SeedCounts(int Units, int Requisitions, int Applications, int Events, int Offers, int WorkflowInstances, int Positions);

    private async Task<SeedCounts> SeedAsync()
    {
        var events = 0;
        var offers = 0;
        var instances = 0;
        var requisitionDefinition = Guid.NewGuid();
        var offerDefinition = Guid.NewGuid();
        var requisitions = new List<(Guid Id, Guid Unit, DateTime Approved)>();

        await using (var db = CreateContext())
        {
            for (var u = 0; u < Units; u++)
            {
                var unit = new OrgUnit { TenantId = _tenant, Code = $"OU-S32-{u:00}", NameAr = $"وحدة {u}", NameEn = $"Unit {u}" };
                unit.MaterializedPath = $"/{unit.Id:D}/";
                db.OrgUnits.Add(unit);
                _units.Add(unit.Id);
            }
            db.WorkflowDefinitions.AddRange(
                Definition(requisitionDefinition, "RECRUITMENT-REQUISITION-APPROVAL", nameof(RecruitmentRequisition), 2),
                Definition(offerDefinition, "RECRUITMENT-OFFER-APPROVAL", nameof(OfferLetter), 1));
            for (var p = 0; p < VacantWithoutRequisition; p++)
                db.FundedPositions.Add(Position(_units[p % Units], FundedPositionStatus.Vacant, $"VAC-S32-{p:0000}"));
            await db.SaveChangesAsync();
        }

        // Requisitions, applications, events, offers and workflow instances in chunks of 100 requisitions.
        var rnd = new Random(32);
        foreach (var chunk in Enumerable.Range(0, Requisitions).Chunk(100))
        {
            await using var db = CreateContext();
            using var _ = db.SuppressApplicationEvents();
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            foreach (var r in chunk)
            {
                var unit = _units[r % Units];
                var requested = Now.AddDays(-30 - (r % 420));
                var approved = requested.AddDays(3);
                var status = (r % 20) switch { < 10 => RequisitionStatus.Closed, < 18 => RequisitionStatus.Published, 18 => RequisitionStatus.Cancelled, _ => RequisitionStatus.Approved };
                var position = Position(unit, status == RequisitionStatus.Closed ? FundedPositionStatus.Occupied : FundedPositionStatus.Vacant, $"POS-S32-{r:0000}");
                db.FundedPositions.Add(position);
                var requisition = new RecruitmentRequisition
                {
                    TenantId = _tenant, RequisitionNumber = $"REQ-S32-{r:0000}", FundedPositionId = position.Id, JobTitle = "محلل",
                    Status = status, RequestedDate = requested, ApprovedDate = r % 7 == 0 ? null : approved,
                    PublishedAt = status is RequisitionStatus.Published or RequisitionStatus.Closed ? approved.AddDays(2) : null,
                    ClosedDate = status == RequisitionStatus.Closed ? approved.AddDays(40) : null,
                    CancelledAt = status == RequisitionStatus.Cancelled ? approved.AddDays(20) : null, VacancyCount = 1
                };
                db.RecruitmentRequisitions.Add(requisition);
                requisitions.Add((requisition.Id, unit, approved));

                db.WorkflowInstances.Add(Instance(requisitionDefinition, nameof(RecruitmentRequisition), requisition.Id, requested,
                    r % 25 == 0 ? WorkflowStatus.InProgress : WorkflowStatus.Completed, 2));
                instances++;

                for (var a = 0; a < ApplicationsPerRequisition; a++)
                {
                    var applied = approved.AddDays(3 + a % 10);
                    var hired = status == RequisitionStatus.Closed && a == 0;
                    var appStatus = hired ? RecruitmentApplicationStatus.Hired : (a % 6) switch
                    {
                        0 => RecruitmentApplicationStatus.Rejected,
                        1 => RecruitmentApplicationStatus.UnderReview,
                        2 => RecruitmentApplicationStatus.Shortlisted,
                        3 => RecruitmentApplicationStatus.Ranked,
                        4 => RecruitmentApplicationStatus.OfferDeclined,
                        _ => RecruitmentApplicationStatus.Withdrawn
                    };
                    var app = new RecruitmentApplication
                    {
                        TenantId = _tenant, ApplicantFullName = $"متقدم {r}-{a}", RequisitionId = requisition.Id, AppliedPosition = "محلل",
                        IntakeChannel = (RecruitmentIntakeChannel)(a % 4), AppliedAt = applied, Status = appStatus,
                        RejectedAt = appStatus == RecruitmentApplicationStatus.Rejected ? applied.AddDays(9) : null,
                        RejectionKind = appStatus == RecruitmentApplicationStatus.Rejected ? RecruitmentRejectionKind.SelectionDecision : null,
                        ScreenedAt = applied.AddDays(2)
                    };
                    db.RecruitmentApplications.Add(app);

                    RecruitmentApplicationStatus[] path = hired
                        ? [RecruitmentApplicationStatus.Submitted, RecruitmentApplicationStatus.UnderReview, RecruitmentApplicationStatus.Shortlisted, RecruitmentApplicationStatus.Ranked, RecruitmentApplicationStatus.Hired]
                        : [RecruitmentApplicationStatus.Submitted, RecruitmentApplicationStatus.UnderReview, RecruitmentApplicationStatus.Shortlisted, RecruitmentApplicationStatus.Ranked, appStatus];
                    for (var e = 0; e < EventsPerApplication; e++)
                    {
                        db.RecruitmentApplicationEvents.Add(new RecruitmentApplicationEvent
                        {
                            TenantId = _tenant, RecruitmentApplicationId = app.Id,
                            EventType = e == 0 ? RecruitmentApplicationEventTypes.Received : RecruitmentApplicationEventTypes.StatusChanged,
                            FromStatus = e == 0 ? null : (int)path[e - 1], ToStatus = (int)path[e],
                            OccurredAt = applied.AddDays(e * 2 + rnd.Next(0, 2)), NotificationsPlannedAt = applied
                        });
                        events++;
                    }

                    if (a < OffersPerRequisition)
                    {
                        var created = applied.AddDays(10);
                        var offerStatus = hired ? OfferStatus.Accepted : (a % 5) switch
                        {
                            0 => OfferStatus.Declined, 1 => OfferStatus.Expired, 2 => OfferStatus.Revoked, 3 => OfferStatus.Accepted, _ => OfferStatus.Draft
                        };
                        var offer = new OfferLetter
                        {
                            TenantId = _tenant, RecruitmentApplicationId = app.Id, FundedPositionId = position.Id, OfferNumber = $"OFF-S32-{r:0000}-{a:00}",
                            Status = offerStatus, CreatedAt = created, IssuedDate = offerStatus == OfferStatus.Draft ? null : created.AddDays(1),
                            AcceptedDate = offerStatus == OfferStatus.Accepted ? created.AddDays(3) : null,
                            DeclinedAt = offerStatus == OfferStatus.Declined ? created.AddDays(4) : null,
                            DeclinedBy = offerStatus == OfferStatus.Declined ? OfferDeclinedBy.Candidate : null,
                            DeclineReasonCategory = offerStatus == OfferStatus.Declined ? (OfferDeclineReasonCategory)(r % 6) : null,
                            ExpiredAt = offerStatus == OfferStatus.Expired ? created.AddDays(15) : null,
                            RevokedAt = offerStatus == OfferStatus.Revoked ? created.AddDays(2) : null,
                            IsMeritOverride = a == 7, OfferedSalary = 500000, ProposedStartDate = created.AddDays(30)
                        };
                        db.OfferLetters.Add(offer);
                        offers++;
                        if (instances < 5_000 && a < 2)
                        {
                            db.WorkflowInstances.Add(Instance(offerDefinition, nameof(OfferLetter), offer.Id, created,
                                a == 1 && r % 10 == 0 ? WorkflowStatus.InProgress : WorkflowStatus.Completed, 1));
                            instances++;
                        }
                    }
                }
            }
            db.ChangeTracker.DetectChanges();
            await db.SaveChangesAsync();
        }

        return new SeedCounts(Units, Requisitions, Requisitions * ApplicationsPerRequisition, events, offers, instances,
            Requisitions + VacantWithoutRequisition);
    }

    private FundedPosition Position(Guid unit, FundedPositionStatus status, string code) => new()
    {
        TenantId = _tenant, PositionCode = code, GradeCode = "G5", BudgetYear = 2026, Status = status, OrgUnitId = unit
    };

    private WorkflowDefinition Definition(Guid id, string code, string entityType, int steps) => new()
    {
        Id = id, TenantId = _tenant, Code = code, Name = code, Version = 1, IsPublished = true, DomainEntityType = entityType,
        StepsJson = JsonSerializer.Serialize(Enumerable.Range(1, steps)
            .Select(l => new WorkflowStepSnapshot { Level = l, Name = $"الخطوة {l}", RoleRequired = "hr-core" }).ToList())
    };

    private WorkflowInstance Instance(Guid definition, string entityType, Guid entityId, DateTime started, WorkflowStatus status, int steps)
    {
        var history = Enumerable.Range(1, status == WorkflowStatus.InProgress ? steps - 1 : steps)
            .Select(l => new WorkflowStepHistoryEntry { Level = l, StepName = $"الخطوة {l}", Status = WorkflowStepStatus.Approved, ActionAt = started.AddHours(20 * l) })
            .ToList();
        return new WorkflowInstance
        {
            TenantId = _tenant, DefinitionId = definition, DefinitionVersion = 1, EntityType = entityType, EntityId = entityId,
            StartedAt = started, Status = status, CurrentStepLevel = status == WorkflowStatus.InProgress ? steps : steps,
            CompletedAt = status == WorkflowStatus.Completed ? started.AddHours(20 * steps) : null,
            StepHistoryJson = JsonSerializer.Serialize(history)
        };
    }

    // ------------------------------------------------------------------ plans

    private sealed record CapturedCommand(string Sql, IReadOnlyList<NpgsqlParameter> Parameters);

    private sealed class SqlCapture : DbCommandInterceptor
    {
        public List<CapturedCommand> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                Commands.Add(new CapturedCommand(command.CommandText, command.Parameters.Cast<NpgsqlParameter>().Select(p => p.Clone()).ToList()));
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed record ExplainedCommand(string Sql, string Plan);

    private async Task<List<ExplainedCommand>> ExplainAsync(SqlCapture capture)
    {
        var plans = new List<ExplainedCommand>();
        await using var connection = new NpgsqlConnection(_postgres!.GetConnectionString());
        await connection.OpenAsync();
        foreach (var command in capture.Commands.DistinctBy(c => c.Sql))
        {
            await using var explain = new NpgsqlCommand("EXPLAIN (ANALYZE, BUFFERS OFF) " + command.Sql, connection);
            foreach (var parameter in command.Parameters)
                explain.Parameters.Add(parameter.Clone());
            var plan = new StringBuilder();
            await using var reader = await explain.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                plan.AppendLine(reader.GetString(0));
            plans.Add(new ExplainedCommand(command.Sql, plan.ToString()));
        }
        return plans;
    }

    private static void WriteArtifacts(object timings, IReadOnlyList<ExplainedCommand> plans)
    {
        var dir = Environment.GetEnvironmentVariable("HCM032_PERF_OUT")
            ?? Path.Combine(HRManagement.TestSupport.RepoPaths.Root, "artifacts", "hcm032-perf");
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(timings, new JsonSerializerOptions { WriteIndented = true });
        System.IO.File.WriteAllText(Path.Combine(dir, "latest.json"), json);
        System.IO.File.WriteAllText(Path.Combine(dir, "run-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".json"), json);
        var text = new StringBuilder();
        foreach (var plan in plans)
            text.AppendLine("-- SQL").AppendLine(plan.Sql).AppendLine("-- PLAN").AppendLine(plan.Plan).AppendLine();
        System.IO.File.WriteAllText(Path.Combine(dir, "plans.txt"), text.ToString());
    }

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public bool IsPlatformOperator => false;
        public Guid RequireTenantId() => tenantId;
    }

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
