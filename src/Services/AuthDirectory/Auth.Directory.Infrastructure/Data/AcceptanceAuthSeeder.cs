using Auth.Directory.Domain.Entities;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Seeding;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Auth.Directory.Infrastructure.Data;

public static class AcceptanceAuthSeeder
{
    private const string ExternalPasswordMarker = "KEYCLOAK-MANAGED-ACCEPTANCE-V2";

    public static async Task SeedAsync(AuthDirectoryDbContext db, CancellationToken ct = default)
    {
        await AcceptanceSeedReceiptStore.EnsureTableAsync(db, ct);
        var agenciesToApply = new List<AcceptanceAgency>();
        foreach (var agency in AcceptanceSeedCatalog.Agencies)
        {
            if (await AcceptanceSeedReceiptStore.ShouldApplyAsync(db, agency.Id, nameof(AuthDirectoryDbContext), ct))
                agenciesToApply.Add(agency);
        }
        if (agenciesToApply.Count == 0)
            return;

        var usernames = AcceptancePersonaCatalog.Personas.Select(p => p.Username).ToArray();
        var existing = await db.AuthUsers
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.SeedInitialization)
            .Where(u => usernames.Contains(u.Username))
            .Select(u => u.Username)
            .ToListAsync(ct);
        if (existing.Count > 0 && existing.Count != usernames.Length)
        {
            throw new InvalidOperationException(
                "Auth.Directory contains an incomplete Acceptance v2 identity pack. " +
                "Run scripts/acceptance-reset.ps1 before reseeding.");
        }

        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        try
        {
            if (existing.Count == 0)
            {
                await SeedRolesAsync(db, ct);
                await SeedUsersAsync(db, ct);
            }
            foreach (var agency in agenciesToApply)
                await AcceptanceSeedReceiptStore.WriteAppliedAsync(db, agency, nameof(AuthDirectoryDbContext), ct);
            if (transaction is not null)
                await transaction.CommitAsync(ct);
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(ct);
            throw;
        }
    }

    private static async Task SeedRolesAsync(AuthDirectoryDbContext db, CancellationToken ct)
    {
        var tenantIds = AcceptancePersonaCatalog.Personas
            .Select(p => p.StorageTenantId)
            .Distinct()
            .ToArray();
        var existing = await db.Roles
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.SeedInitialization)
            .Where(r => tenantIds.Contains(r.TenantId))
            .Select(r => new { r.TenantId, r.Name })
            .ToListAsync(ct);
        var keys = existing.Select(r => $"{r.TenantId:D}|{r.Name}").ToHashSet(StringComparer.Ordinal);

        var previousTenantId = TenantIdFlow.Get();
        try
        {
            foreach (var tenantId in tenantIds)
            {
                TenantIdFlow.Set(tenantId);
                var tenantCode = AcceptanceSeedCatalog.Agencies.FirstOrDefault(a => a.Id == tenantId)?.Code ?? "PLATFORM";
                foreach (var role in AcceptanceRoleCatalog.All.OrderBy(r => r, StringComparer.Ordinal))
                {
                    if (!keys.Add($"{tenantId:D}|{role}"))
                        continue;
                    db.Roles.Add(new Role
                    {
                        Id = AcceptanceSeedCatalog.DeterministicGuid(tenantCode, $"auth-role:{role}"),
                        TenantId = tenantId,
                        Name = role,
                        Description = $"Acceptance v2 role: {role}",
                        Permissions = role,
                        CreatedAt = AcceptanceSeedCatalog.ReferenceDate,
                        UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
                    });
                }
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
            }
        }
        finally
        {
            TenantIdFlow.Set(previousTenantId);
        }
    }

    private static async Task SeedUsersAsync(AuthDirectoryDbContext db, CancellationToken ct)
    {
        var roles = await db.Roles
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.SeedInitialization)
            .ToListAsync(ct);
        var previousTenantId = TenantIdFlow.Get();
        try
        {
            foreach (var tenantGroup in AcceptancePersonaCatalog.Personas.GroupBy(p => p.StorageTenantId))
            {
                TenantIdFlow.Set(tenantGroup.Key);
                foreach (var persona in tenantGroup)
                {
                    var tenantCode = persona.Definition.TenantCode ?? "NO-TENANT";
                    var userId = AcceptanceSeedCatalog.DeterministicGuid(tenantCode, $"auth-user:{persona.Id}");
                    var email = $"{persona.Username}@acceptance.test";
                    db.AuthUsers.Add(new AuthUser
                    {
                        Id = userId,
                        TenantId = persona.StorageTenantId,
                        Username = persona.Username,
                        Email = email,
                        PasswordHash = ExternalPasswordMarker,
                        EmployeeId = persona.Employee?.Id,
                        ScopeOrgUnitId = persona.ScopeOrgUnitId,
                        IsActive = persona.Enabled,
                        KeycloakUserId = AcceptanceSeedCatalog.DeterministicGuid("KEYCLOAK", persona.Username).ToString("D"),
                        CreatedAt = AcceptanceSeedCatalog.ReferenceDate,
                        UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
                    });

                    foreach (var roleName in persona.Roles)
                    {
                        var role = roles.Single(r => r.TenantId == persona.StorageTenantId && r.Name == roleName);
                        db.UserRoles.Add(new UserRole
                        {
                            Id = AcceptanceSeedCatalog.DeterministicGuid(tenantCode, $"user-role:{persona.Id}:{roleName}"),
                            TenantId = persona.StorageTenantId,
                            AuthUserId = userId,
                            RoleId = role.Id,
                            AssignedAt = AcceptanceSeedCatalog.ReferenceDate,
                            AssignedBy = "acceptance-v2-seeder",
                            CreatedAt = AcceptanceSeedCatalog.ReferenceDate,
                            UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
                        });
                    }

                    if (persona.Employee is not null)
                    {
                        db.DirectoryEntries.Add(new DirectoryEntry
                        {
                            Id = AcceptanceSeedCatalog.DeterministicGuid(tenantCode, $"directory-entry:{persona.Id}"),
                            TenantId = persona.StorageTenantId,
                            EmployeeId = persona.Employee.Id,
                            DisplayName = persona.Employee.FullName,
                            PrimaryEmail = persona.Employee.Email,
                            PrimaryPhone = persona.Employee.Phone,
                            PrimaryOrgUnitId = persona.Employee.OrgUnitId,
                            Status = persona.Employee.State.ToString(),
                            LastSyncedAt = AcceptanceSeedCatalog.ReferenceDate,
                            CreatedAt = AcceptanceSeedCatalog.ReferenceDate,
                            UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
                        });
                    }

                    db.AuthAuditLogs.Add(new AuthAuditLog
                    {
                        Id = AcceptanceSeedCatalog.DeterministicGuid(tenantCode, $"auth-audit:{persona.Id}"),
                        TenantId = persona.StorageTenantId,
                        Action = "AcceptancePersonaProvisioned",
                        EntityType = nameof(AuthUser),
                        EntityId = userId,
                        PerformedBy = "acceptance-v2-seeder",
                        PerformedAt = AcceptanceSeedCatalog.ReferenceDate,
                        Details = $"Persona={persona.Id}; Fixture={persona.Fixture ?? "none"}",
                        CreatedAt = AcceptanceSeedCatalog.ReferenceDate,
                        UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
                    });
                }

                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
            }

            TenantIdFlow.Set(TenantDefaults.DefaultTenantId);
            db.DirectorySyncRecords.Add(new DirectorySyncRecord
            {
                Id = AcceptanceSeedCatalog.DeterministicGuid("PLATFORM", "auth-directory-sync-receipt"),
                TenantId = TenantDefaults.DefaultTenantId,
                SourceService = AcceptanceSeedCatalog.PackId,
                RecordsProcessed = AcceptancePersonaCatalog.Personas.Count,
                SuccessCount = AcceptancePersonaCatalog.Personas.Count,
                FailureCount = 0,
                StartedAt = AcceptanceSeedCatalog.ReferenceDate,
                CompletedAt = AcceptanceSeedCatalog.ReferenceDate,
                Log = $"digest={AcceptanceSeedPackFingerprint.Value}",
                CreatedAt = AcceptanceSeedCatalog.ReferenceDate,
                UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
            });
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
        finally
        {
            TenantIdFlow.Set(previousTenantId);
        }
    }
}
