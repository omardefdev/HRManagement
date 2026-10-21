using Auth.Directory.Domain.Entities;
using Auth.Directory.Infrastructure.Data;
using File.Storage.Domain.Entities;
using File.Storage.Infrastructure.Data;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
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
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Moq;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;

namespace HRManagement.UnitTests;

/// <summary>
/// TEN-003: representative entity isolation across bounded contexts.
/// Pattern: seed Tenant A, query as Tenant B → empty; platform-admin → sees all.
/// </summary>
public class CrossTenantBoundedContextIsolationTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-4aaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-4bbb-bbbb-bbbbbbbbbbbb");

    private static ITenantContext FixedTenant(Guid tenantId, bool platformAdmin = false)
    {
        var mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(tenantId);
        mock.Setup(t => t.IsPlatformOperator).Returns(platformAdmin);
        mock.Setup(t => t.IsPlatformImpersonating).Returns(platformAdmin && tenantId != Guid.Empty);
        mock.Setup(t => t.RequireTenantId()).Returns(tenantId);
        return mock.Object;
    }

    private static DbContextOptions<T> Options<T>(ITenantContext tenant, string dbName) where T : DbContext =>
        new DbContextOptionsBuilder<T>()
            .UseInMemoryDatabase(dbName)
            .AddInterceptors(new TenantIdStampInterceptor(tenant))
            .Options;

    private static async Task AssertIsolationAsync<TContext, TEntity>(
        Func<ITenantContext, string, TContext> factory,
        Func<TContext, DbSet<TEntity>> set,
        Func<Guid, TEntity> create,
        Func<TEntity, string> key)
        where TContext : DbContext
        where TEntity : class
    {
        var dbName = $"bc-{typeof(TEntity).Name}-{Guid.NewGuid()}";

        await using (var seedA = factory(FixedTenant(TenantA), dbName))
        {
            set(seedA).Add(create(TenantA));
            await seedA.SaveChangesAsync();
        }

        await using (var seedB = factory(FixedTenant(TenantB), dbName))
        {
            set(seedB).Add(create(TenantB));
            await seedB.SaveChangesAsync();
        }

        await using (var readB = factory(FixedTenant(TenantB), dbName))
        {
            var rows = await set(readB).ToListAsync();
            var keys = rows.Select(key).ToList();
            Assert.DoesNotContain(keys, k => k.StartsWith("A-", StringComparison.Ordinal));
            Assert.Contains(keys, k => k.StartsWith("B-", StringComparison.Ordinal));
        }

        await using (var readA = factory(FixedTenant(TenantA), dbName))
        {
            var rows = await set(readA).ToListAsync();
            var keys = rows.Select(key).ToList();
            Assert.Equal(["A-row"], keys);
        }

        var admin = FixedTenant(TenantA, platformAdmin: true);
        await using var adminDb = factory(admin, dbName);
        var impersonatingRows = await set(adminDb).ToListAsync();
        var impersonatingKeys = impersonatingRows.Select(key).OrderBy(x => x).ToList();
        Assert.Equal(["A-row"], impersonatingKeys);

        var allRows = await set(adminDb).IgnoreTenantFilter(admin).ToListAsync();
        var all = allRows.Select(key).OrderBy(x => x).ToList();
        Assert.Equal(["A-row", "B-row"], all);
    }

    [Fact]
    public async Task HRCore_Employee_OrgUnit_ApprovalRequest_isolated()
    {
        await AssertIsolationAsync(
            (t, n) => new HRCoreDbContext(Options<HRCoreDbContext>(t, n), t),
            db => db.Employees,
            tid => new Employee
            {
                TenantId = tid,
                FullName = tid == TenantA ? "A-row" : "B-row",
                NationalId = tid == TenantA ? "A-nid" : "B-nid",
                FirstName = "F",
                LastName = "L",
                BirthDate = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Gender = Gender.Male
            },
            e => e.FullName);

        await AssertIsolationAsync(
            (t, n) => new HRCoreDbContext(Options<HRCoreDbContext>(t, n), t),
            db => db.OrgUnits,
            tid => new OrgUnit
            {
                TenantId = tid,
                Code = tid == TenantA ? "A" : "B",
                NameAr = tid == TenantA ? "A-row" : "B-row",
                MaterializedPath = "/"
            },
            e => e.NameAr);

        await AssertIsolationAsync(
            (t, n) => new HRCoreDbContext(Options<HRCoreDbContext>(t, n), t),
            db => db.ApprovalRequests,
            tid => new ApprovalRequest
            {
                TenantId = tid,
                EntityType = "Employee",
                EntityId = Guid.NewGuid(),
                Description = tid == TenantA ? "A-row" : "B-row"
            },
            e => e.Description!);
    }

    [Fact]
    public async Task Contracts_Contract_LeaveRequest_SeparationRecord_isolated()
    {
        await AssertIsolationAsync(
            (t, n) => new ContractsDbContext(Options<ContractsDbContext>(t, n), t),
            db => db.LeaveRequests,
            tid => new LeaveRequest
            {
                TenantId = tid,
                EmployeeId = Guid.NewGuid(),
                LeaveType = LeaveType.Annual,
                StartDate = new DateOnly(2026, 1, 1),
                EndDate = new DateOnly(2026, 1, 2),
                DurationDays = 1,
                Status = ApprovalStatus.Pending,
                Notes = tid == TenantA ? "A-row" : "B-row"
            },
            e => e.Notes!);

        await AssertIsolationAsync(
            (t, n) => new ContractsDbContext(Options<ContractsDbContext>(t, n), t),
            db => db.SeparationRecords,
            tid => new SeparationRecord
            {
                TenantId = tid,
                EmployeeId = Guid.NewGuid(),
                SeparationType = SeparationType.Resignation,
                EffectiveDate = new DateOnly(2026, 1, 1),
                Reason = tid == TenantA ? "A-row" : "B-row"
            },
            e => e.Reason!);

        // Contract requires ContractType — seed type + contract in same tenant
        var dbName = $"contract-{Guid.NewGuid()}";
        Guid typeA, typeB, contractA, contractB;
        await using (var seedA = new ContractsDbContext(Options<ContractsDbContext>(FixedTenant(TenantA), dbName), FixedTenant(TenantA)))
        {
            var ct = new ContractType { TenantId = TenantA, Code = "CA", NameAr = "A", NameEn = "A" };
            seedA.ContractTypes.Add(ct);
            await seedA.SaveChangesAsync();
            typeA = ct.Id;
            var c = new Contract
            {
                TenantId = TenantA,
                EmployeeId = Guid.NewGuid(),
                ContractTypeId = typeA,
                StartDate = new DateOnly(2026, 1, 1),
                Status = ContractStatus.Active,
                Terms = "A-row"
            };
            seedA.Contracts.Add(c);
            await seedA.SaveChangesAsync();
            contractA = c.Id;
        }

        await using (var seedB = new ContractsDbContext(Options<ContractsDbContext>(FixedTenant(TenantB), dbName), FixedTenant(TenantB)))
        {
            var ct = new ContractType { TenantId = TenantB, Code = "CB", NameAr = "B", NameEn = "B" };
            seedB.ContractTypes.Add(ct);
            await seedB.SaveChangesAsync();
            typeB = ct.Id;
            var c = new Contract
            {
                TenantId = TenantB,
                EmployeeId = Guid.NewGuid(),
                ContractTypeId = typeB,
                StartDate = new DateOnly(2026, 1, 1),
                Status = ContractStatus.Active,
                Terms = "B-row"
            };
            seedB.Contracts.Add(c);
            await seedB.SaveChangesAsync();
            contractB = c.Id;
        }

        await using var readB = new ContractsDbContext(Options<ContractsDbContext>(FixedTenant(TenantB), dbName), FixedTenant(TenantB));
        Assert.Null(await readB.Contracts.FirstOrDefaultAsync(c => c.Id == contractA));
        Assert.NotNull(await readB.Contracts.FirstOrDefaultAsync(c => c.Id == contractB));
        _ = typeA;
        _ = typeB;
    }

    [Fact]
    public async Task Payroll_PayrollRun_SalaryLine_EmployeePayProfile_isolated()
    {
        await AssertIsolationAsync(
            (t, n) => new PayrollDbContext(Options<PayrollDbContext>(t, n), t),
            db => db.PayrollRuns,
            tid => new PayrollRun
            {
                TenantId = tid,
                PeriodStart = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                PeriodEnd = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc),
                RunDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                Status = PayrollRunStatus.Draft,
                RunType = PayrollRunType.Regular,
                TotalNetPay = tid == TenantA ? 1 : 2
            },
            e => e.TotalNetPay == 1 ? "A-row" : "B-row");

        await AssertIsolationAsync(
            (t, n) => new PayrollDbContext(Options<PayrollDbContext>(t, n), t),
            db => db.EmployeePayProfiles,
            tid => new EmployeePayProfile
            {
                TenantId = tid,
                EmployeeId = Guid.NewGuid(),
                BaseSalary = tid == TenantA ? 100 : 200,
                EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                IsActive = true
            },
            e => e.BaseSalary == 100 ? "A-row" : "B-row");

        var dbName = $"sal-{Guid.NewGuid()}";
        Guid runA, lineA;
        await using (var seedA = new PayrollDbContext(Options<PayrollDbContext>(FixedTenant(TenantA), dbName), FixedTenant(TenantA)))
        {
            var run = new PayrollRun
            {
                TenantId = TenantA,
                PeriodStart = DateTime.UtcNow.Date,
                PeriodEnd = DateTime.UtcNow.Date.AddDays(30),
                RunDate = DateTime.UtcNow.Date,
                Status = PayrollRunStatus.Draft
            };
            seedA.PayrollRuns.Add(run);
            await seedA.SaveChangesAsync();
            runA = run.Id;
            var line = new SalaryLine
            {
                TenantId = TenantA,
                PayrollRunId = runA,
                EmployeeId = Guid.NewGuid(),
                GrossPay = 1,
                NetPay = 1,
                PayComponents = "A-row"
            };
            seedA.SalaryLines.Add(line);
            await seedA.SaveChangesAsync();
            lineA = line.Id;
        }

        await using (var seedB = new PayrollDbContext(Options<PayrollDbContext>(FixedTenant(TenantB), dbName), FixedTenant(TenantB)))
        {
            var run = new PayrollRun
            {
                TenantId = TenantB,
                PeriodStart = DateTime.UtcNow.Date,
                PeriodEnd = DateTime.UtcNow.Date.AddDays(30),
                RunDate = DateTime.UtcNow.Date,
                Status = PayrollRunStatus.Draft
            };
            seedB.PayrollRuns.Add(run);
            await seedB.SaveChangesAsync();
            seedB.SalaryLines.Add(new SalaryLine
            {
                TenantId = TenantB,
                PayrollRunId = run.Id,
                EmployeeId = Guid.NewGuid(),
                GrossPay = 2,
                NetPay = 2,
                PayComponents = "B-row"
            });
            await seedB.SaveChangesAsync();
        }

        await using var readB = new PayrollDbContext(Options<PayrollDbContext>(FixedTenant(TenantB), dbName), FixedTenant(TenantB));
        Assert.Null(await readB.SalaryLines.FirstOrDefaultAsync(l => l.Id == lineA));
        Assert.Equal(["B-row"], await readB.SalaryLines.Select(l => l.PayComponents!).ToListAsync());
    }

    [Fact]
    public async Task Movements_TransferRequest_MovementNotice_isolated()
    {
        await AssertIsolationAsync(
            (t, n) => new MovementsDbContext(Options<MovementsDbContext>(t, n), t),
            db => db.TransferRequests,
            tid => new TransferRequest
            {
                TenantId = tid,
                EmployeeId = Guid.NewGuid(),
                FromOrgUnitId = Guid.NewGuid(),
                ToOrgUnitId = Guid.NewGuid(),
                RequestDate = new DateOnly(2026, 1, 1),
                Reason = tid == TenantA ? "A-row" : "B-row",
                Status = ApprovalStatus.Pending
            },
            e => e.Reason!);

        await AssertIsolationAsync(
            (t, n) => new MovementsDbContext(Options<MovementsDbContext>(t, n), t),
            db => db.MovementNotices,
            tid => new MovementNotice
            {
                TenantId = tid,
                Title = tid == TenantA ? "A-row" : "B-row",
                IssueDate = new DateOnly(2026, 1, 1)
            },
            e => e.Title);
    }

    [Fact]
    public async Task AuthDirectory_AuthUser_Role_isolated()
    {
        await AssertIsolationAsync(
            (t, n) => new AuthDirectoryDbContext(Options<AuthDirectoryDbContext>(t, n), t),
            db => db.AuthUsers,
            tid => new AuthUser
            {
                TenantId = tid,
                Username = tid == TenantA ? "A-row" : "B-row",
                Email = $"{(tid == TenantA ? "a" : "b")}@test.local",
                PasswordHash = "x",
                IsActive = true
            },
            e => e.Username);

        await AssertIsolationAsync(
            (t, n) => new AuthDirectoryDbContext(Options<AuthDirectoryDbContext>(t, n), t),
            db => db.Roles,
            tid => new Role
            {
                TenantId = tid,
                Name = tid == TenantA ? "A-row" : "B-row"
            },
            e => e.Name);
    }

    [Fact]
    public async Task Training_Course_Enrollment_isolated()
    {
        await AssertIsolationAsync(
            (t, n) => new TrainingDbContext(Options<TrainingDbContext>(t, n), t),
            db => db.TrainingCourses,
            tid => new TrainingCourse
            {
                TenantId = tid,
                Code = tid == TenantA ? "A" : "B",
                TitleAr = tid == TenantA ? "A-row" : "B-row",
                DurationDays = 1
            },
            e => e.TitleAr);

        var dbName = $"enr-{Guid.NewGuid()}";
        Guid courseA, enrA;
        await using (var seedA = new TrainingDbContext(Options<TrainingDbContext>(FixedTenant(TenantA), dbName), FixedTenant(TenantA)))
        {
            var course = new TrainingCourse { TenantId = TenantA, Code = "CA", TitleAr = "A", DurationDays = 1 };
            seedA.TrainingCourses.Add(course);
            await seedA.SaveChangesAsync();
            courseA = course.Id;
            var enr = new TrainingEnrollment
            {
                TenantId = TenantA,
                CourseId = courseA,
                EmployeeId = Guid.NewGuid(),
                EnrolledAt = DateTime.UtcNow
            };
            seedA.TrainingEnrollments.Add(enr);
            await seedA.SaveChangesAsync();
            enrA = enr.Id;
        }

        await using (var seedB = new TrainingDbContext(Options<TrainingDbContext>(FixedTenant(TenantB), dbName), FixedTenant(TenantB)))
        {
            var course = new TrainingCourse { TenantId = TenantB, Code = "CB", TitleAr = "B", DurationDays = 1 };
            seedB.TrainingCourses.Add(course);
            await seedB.SaveChangesAsync();
            seedB.TrainingEnrollments.Add(new TrainingEnrollment
            {
                TenantId = TenantB,
                CourseId = course.Id,
                EmployeeId = Guid.NewGuid(),
                EnrolledAt = DateTime.UtcNow
            });
            await seedB.SaveChangesAsync();
        }

        await using var readB = new TrainingDbContext(Options<TrainingDbContext>(FixedTenant(TenantB), dbName), FixedTenant(TenantB));
        Assert.Null(await readB.TrainingEnrollments.FirstOrDefaultAsync(e => e.Id == enrA));
        Assert.Single(await readB.TrainingEnrollments.ToListAsync());
    }

    [Fact]
    public async Task Health_MedicalRecord_isolated()
    {
        await AssertIsolationAsync(
            (t, n) => new HealthDbContext(Options<HealthDbContext>(t, n), t),
            db => db.MedicalRecords,
            tid => new MedicalRecord
            {
                TenantId = tid,
                EmployeeId = Guid.NewGuid(),
                MedicalHistory = tid == TenantA ? "A-row" : "B-row"
            },
            e => e.MedicalHistory!);
    }

    [Fact]
    public async Task FileStorage_FileRecord_isolated()
    {
        await AssertIsolationAsync(
            (t, n) => new FileStorageDbContext(Options<FileStorageDbContext>(t, n), t),
            db => db.Files,
            tid => new FileRecord
            {
                TenantId = tid,
                OriginalName = tid == TenantA ? "A-row" : "B-row",
                Extension = ".pdf",
                Bucket = "docs",
                ObjectKey = Guid.NewGuid().ToString("N"),
                Size = 1
            },
            e => e.OriginalName);
    }

    [Fact]
    public void Assignment_and_quarantine_entities_are_global()
    {
        Assert.True(TenantEntityClassification.IsGlobalEntity(typeof(TenantAssignmentRule)));
        Assert.True(TenantEntityClassification.IsGlobalEntity(typeof(TenantQuarantineEntry)));
        Assert.False(TenantEntityClassification.IsTenantOwned(typeof(TenantAssignmentRule)));
    }
}
