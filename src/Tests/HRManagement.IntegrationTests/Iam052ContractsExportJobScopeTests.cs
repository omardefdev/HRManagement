using System.Security.Claims;
using System.Text;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Infrastructure.Data;
using HR.Contracts.Infrastructure.ExportJobs;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Exports;
using HRManagement.SharedKernel.Tenancy;
using MassTransit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-52 (P-7): an export job runs without the user (<c>ExportJobRequestedConsumer</c>), so it records the requester's scope when it is
/// queued and applies it. A job queued by a restricted user holds his scope's rows only; an unrestricted requester's holds every row; a
/// restricted requester's job with no recorded scope (queued before PLAN-52) exports nothing.
/// </summary>
public sealed class Iam052ContractsExportJobScopeTests : IAsyncLifetime
{
    private static readonly Guid Tenant = TenantDefaults.DefaultTenantId;
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private ContractsDbContext _db = null!;
    private readonly Guid _branch = Guid.NewGuid();
    private readonly Guid _other = Guid.NewGuid();
    private readonly Guid _own = Guid.NewGuid();
    private readonly Guid _foreign = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new ContractsDbContext(new DbContextOptionsBuilder<ContractsDbContext>().UseSqlite(_connection).Options, new Msg040FixedTenant(Tenant));
        await _db.Database.EnsureCreatedAsync();
        _db.OrgUnitSnapshots.AddRange(
            new OrgUnitSnapshot { Id = _branch, TenantId = Tenant, Code = "B1", NameAr = "B1", MaterializedPath = $"/{_branch:D}/" },
            new OrgUnitSnapshot { Id = _other, TenantId = Tenant, Code = "B2", NameAr = "B2", MaterializedPath = $"/{_other:D}/" });
        _db.EmployeeOrgUnitSnapshots.AddRange(
            new EmployeeOrgUnitSnapshot { EmployeeId = _own, TenantId = Tenant, PrimaryOrgUnitId = _branch },
            new EmployeeOrgUnitSnapshot { EmployeeId = _foreign, TenantId = Tenant, PrimaryOrgUnitId = _other });
        var day = new DateOnly(2026, 4, 1);
        _db.LeaveRequests.AddRange(
            new LeaveRequest { TenantId = Tenant, EmployeeId = _own, StartDate = day, EndDate = day, DurationDays = 1 },
            new LeaveRequest { TenantId = Tenant, EmployeeId = _foreign, StartDate = day, EndDate = day, DurationDays = 1 });
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private sealed class MemoryArtifacts : IExportArtifactStore
    {
        public Dictionary<string, byte[]> Files { get; } = new();

        public async Task<ExportArtifactRef> SaveAsync(Guid tenantId, Guid jobId, string fileName, string contentType, Stream content, long contentLength,
            CancellationToken cancellationToken = default)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            var path = $"{jobId:N}/{fileName}";
            Files[path] = copy.ToArray();
            return new ExportArtifactRef(Guid.NewGuid(), path, contentType, fileName);
        }

        public Task<Stream> OpenReadAsync(ExportArtifactRef reference, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(Files[reference.RelativePath]));

        public Task DeleteAsync(ExportArtifactRef reference, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static ClaimsPrincipal User(params Claim[] extra) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), .. extra], "test", ClaimTypes.Name, ClaimTypes.Role));

    private async Task<string> ExportAsync(ClaimsPrincipal user, Action<ExportJob>? tamper = null)
    {
        var artifacts = new MemoryArtifacts();
        var service = new ContractsExportJobService(_db, Mock.Of<IPublishEndpoint>(), artifacts, Options.Create(new ExportJobOptions()), new Msg040FixedTenant(Tenant));
        var (ok, jobId, error) = await service.EnqueueAsync(ExportJobTemplates.ContractsLeaveCsv, new { }, user);
        Assert.True(ok, error);
        if (tamper is not null)
        {
            var job = await _db.ExportJobs.SingleAsync(j => j.Id == jobId);
            tamper(job);
            await _db.SaveChangesAsync();
        }

        await service.ProcessAsync(jobId!.Value);
        var done = await _db.ExportJobs.AsNoTracking().SingleAsync(j => j.Id == jobId);
        Assert.Equal(ExportJobStatus.Completed, done.Status);
        return Encoding.UTF8.GetString(artifacts.Files.Values.Single());
    }

    [Fact]
    public async Task A_restricted_requesters_job_exports_his_scope_only()
    {
        var csv = await ExportAsync(User(new Claim(ClaimTypes.Role, HrManagementRoles.BranchManager), new Claim(ClaimTypes.Role, HrManagementRoles.HrContracts),
            new Claim(HrOrgScopeVisibility.ScopeOrgUnitClaimType, _branch.ToString())));
        Assert.Contains(_own.ToString(), csv, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_foreign.ToString(), csv, StringComparison.OrdinalIgnoreCase);
        var snapshot = ExportJobAuth.Deserialize((await _db.ExportJobs.AsNoTracking().SingleAsync()).AuthSnapshotJson);
        Assert.Equal(ExportScopeKinds.Restricted, snapshot!.ScopeKind);
        Assert.Equal(_branch, snapshot.ScopeOrgUnitId);
    }

    [Fact]
    public async Task An_unrestricted_requesters_job_exports_every_row()
    {
        var csv = await ExportAsync(User(new Claim(ClaimTypes.Role, HrManagementRoles.HrContracts)));
        Assert.Contains(_own.ToString(), csv, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(_foreign.ToString(), csv, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_restricted_requesters_job_without_a_recorded_scope_exports_nothing()
    {
        // A job queued before PLAN-52: its snapshot carries the roles but no scope.
        var csv = await ExportAsync(
            User(new Claim(ClaimTypes.Role, HrManagementRoles.BranchManager), new Claim(ClaimTypes.Role, HrManagementRoles.HrContracts),
                new Claim(HrOrgScopeVisibility.ScopeOrgUnitClaimType, _branch.ToString())),
            job =>
            {
                var snapshot = ExportJobAuth.Deserialize(job.AuthSnapshotJson)!;
                snapshot.ScopeKind = null;
                snapshot.ScopeOrgUnitId = null;
                job.AuthSnapshotJson = ExportJobAuth.Serialize(snapshot);
            });
        Assert.DoesNotContain(_own.ToString(), csv, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_foreign.ToString(), csv, StringComparison.OrdinalIgnoreCase);
    }
}
