using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace HRManagement.IntegrationTests;

/// <summary>
/// HCM-026: one migrated PostgreSQL (Testcontainers) shared by the PLAN-26 integration tests.
/// When Docker is unavailable, <see cref="Available"/> is false and tests skip via <c>Skip.IfNot</c>.
/// Each test uses its own tenant id, so tests never see each other's rows.
/// </summary>
public sealed class Hcm026PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;

    public bool Available { get; private set; }
    public string? UnavailableReason { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
            await _postgres.StartAsync();
            await using var db = CreateContext(Guid.NewGuid());
            await db.Database.MigrateAsync();
            Available = true;
        }
        catch (Exception ex)
        {
            UnavailableReason = $"Docker/PostgreSQL unavailable: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }

    public HRCoreDbContext CreateContext(Guid tenantId) =>
        new(new DbContextOptionsBuilder<HRCoreDbContext>()
                .UseNpgsql(_postgres!.GetConnectionString(), npgsql =>
                    npgsql.MigrationsAssembly(typeof(HRCoreDbContext).Assembly.GetName().Name))
                .Options,
            new FixedTenantContext(tenantId));

    /// <summary>Seeds an org unit and a vacant funded position for <paramref name="tenantId"/>.</summary>
    public static async Task<FundedPosition> SeedVacantPositionAsync(HRCoreDbContext db, Guid tenantId)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var org = new OrgUnit { TenantId = tenantId, Code = $"OU-{suffix}", NameAr = "وحدة", NameEn = "Unit" };
        db.OrgUnits.Add(org);
        var position = new FundedPosition
        {
            TenantId = tenantId,
            PositionCode = $"VAC-{suffix}",
            GradeCode = "G5",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = org.Id
        };
        db.FundedPositions.Add(position);
        await db.SaveChangesAsync();
        return position;
    }

    public static RecruitmentRequisition NewRequisition(Guid tenantId, Guid positionId, RequisitionStatus status) => new()
    {
        TenantId = tenantId,
        RequisitionNumber = $"REQ-{Guid.NewGuid():N}"[..14],
        FundedPositionId = positionId,
        JobTitle = "محلل",
        Status = status,
        RequestedDate = DateTime.UtcNow,
        VacancyCount = 1,
        ApplicationClosingDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30)
    };

    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public bool IsPlatformOperator => false;
        public Guid RequireTenantId() => tenantId;
    }
}
