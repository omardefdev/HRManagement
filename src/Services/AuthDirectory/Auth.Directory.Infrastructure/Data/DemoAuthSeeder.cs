using Auth.Directory.Domain.Entities;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Auth.Directory.Infrastructure.Data;

/// <summary>
/// Populates Auth.Directory with demo roles, users, user-roles, and directory entries.
/// Idempotent: skips when auth users already exist.
/// </summary>
public static class DemoAuthSeeder
{
    private static readonly Guid TenantId = TenantDefaults.DefaultTenantId;

    private const string DemoPasswordHash = "DEMO-HASH-NOT-FOR-PROD";

    private static readonly (string Name, string Description)[] RoleDefs =
    [
        ("مدير_عام", "مدير عام — صلاحيات إدارية عليا"),
        ("مدير_شعبة", "مدير شعبة — إشراف على وحدة تنظيمية"),
        ("موظف_موارد_بشرية", "موظف موارد بشرية"),
        ("محاسب", "محاسب / مالية"),
        ("مدقق", "مدقق داخلي"),
        ("قانوني", "مستشار / موظف قانوني"),
        ("تقني", "تقني نظم ومعلومات"),
        ("أرشيف", "أمين أرشيف"),
        ("لوجستيات", "موظف لوجستيات ومشتريات"),
        ("موظف", "موظف عام"),
    ];

    public static async Task SeedAsync(AuthDirectoryDbContext db, CancellationToken ct = default)
    {
        if (await db.AuthUsers.AnyAsync(ct))
            return;

        var rng = new Random(42);
        var now = DateTime.UtcNow;

        await SeedRolesAsync(db, now, ct);
        await SeedUsersAndDirectoryAsync(db, rng, now, ct);
    }

    private static async Task SeedRolesAsync(AuthDirectoryDbContext db, DateTime now, CancellationToken ct)
    {
        if (await db.Roles.AnyAsync(ct))
            return;

        for (var i = 0; i < RoleDefs.Length; i++)
        {
            var (name, description) = RoleDefs[i];
            db.Roles.Add(new Role
            {
                Id = DemoSeedIds.RoleId(i),
                TenantId = TenantId,
                Name = name,
                Description = description,
                Permissions = name,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedUsersAndDirectoryAsync(
        AuthDirectoryDbContext db,
        Random rng,
        DateTime now,
        CancellationToken ct)
    {
        for (var i = 0; i < DemoSeedIds.EmployeeCount; i++)
        {
            var person = DemoNameBank.BuildPerson(i);
            var empId = DemoSeedIds.EmployeeId(i);
            var userId = DemoSeedIds.AuthUserId(i);
            var orgUnitId = DemoSeedIds.OrgUnitId(1 + (i % Math.Max(1, DemoSeedIds.OrgUnitCount - 1)));
            var isActive = DemoSeedIds.IsActiveEmployee(i);
            var created = now.AddDays(-(i % 400));

            db.AuthUsers.Add(new AuthUser
            {
                Id = userId,
                TenantId = TenantId,
                Username = $"emp{i:D3}",
                Email = DemoNameBank.Email(person.First, person.Last, i),
                PasswordHash = DemoPasswordHash,
                EmployeeId = empId,
                ScopeOrgUnitId = orgUnitId,
                IsActive = isActive,
                LastLoginAt = isActive && i % 3 != 0 ? now.AddDays(-rng.Next(0, 45)) : null,
                CreatedAt = created,
                UpdatedAt = now,
            });

            var roleId = ResolveRoleId(i);
            db.UserRoles.Add(new UserRole
            {
                Id = DemoSeedIds.Named($"user-role-{i}"),
                TenantId = TenantId,
                AuthUserId = userId,
                RoleId = roleId,
                AssignedAt = created,
                AssignedBy = "demo-seeder",
                CreatedAt = created,
                UpdatedAt = now,
            });

            // Supervisors also receive the general employee role for baseline access.
            if (DemoSeedIds.IsSupervisor(i) && roleId != DemoSeedIds.RoleId(9))
            {
                db.UserRoles.Add(new UserRole
                {
                    Id = DemoSeedIds.Named($"user-role-base-{i}"),
                    TenantId = TenantId,
                    AuthUserId = userId,
                    RoleId = DemoSeedIds.RoleId(9),
                    AssignedAt = created,
                    AssignedBy = "demo-seeder",
                    CreatedAt = created,
                    UpdatedAt = now,
                });
            }

            db.DirectoryEntries.Add(new DirectoryEntry
            {
                Id = DemoSeedIds.Named($"directory-entry-{i}"),
                TenantId = TenantId,
                EmployeeId = empId,
                DisplayName = DemoNameBank.FullName(person.First, person.Middle, person.Last),
                PrimaryEmail = DemoNameBank.Email(person.First, person.Last, i),
                PrimaryPhone = DemoNameBank.Phone(i),
                PrimaryOrgUnitId = orgUnitId,
                Status = StatusLabel(i),
                LastSyncedAt = now.AddHours(-rng.Next(1, 72)),
                CreatedAt = created,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static Guid ResolveRoleId(int employeeIndex)
    {
        if (DemoSeedIds.IsSupervisor(employeeIndex))
        {
            // Top band → مدير_عام; remaining supervisors → مدير_شعبة
            return employeeIndex < 10
                ? DemoSeedIds.RoleId(0)
                : DemoSeedIds.RoleId(1);
        }

        // Non-supervisors cycle through specialist + general roles (indexes 2..9)
        return DemoSeedIds.RoleId(2 + (employeeIndex % 8));
    }

    private static string StatusLabel(int index) => DemoSeedIds.StatusBucket(index) switch
    {
        < 85 => "Active",
        < 90 => "OnLeave",
        < 95 => "Seconded",
        < 98 => "Terminated",
        _ => "Retired",
    };
}
