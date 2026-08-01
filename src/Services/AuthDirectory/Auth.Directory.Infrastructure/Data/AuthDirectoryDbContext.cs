using Auth.Directory.Domain.Entities;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Auth.Directory.Infrastructure.Data;

public class AuthDirectoryDbContext : TenantAwareDbContext
{
    public AuthDirectoryDbContext(DbContextOptions<AuthDirectoryDbContext> options, ITenantContext? tenantContext = null)
        : base(options, tenantContext) { }

    public DbSet<AuthUser> AuthUsers => Set<AuthUser>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<AuthToken> AuthTokens => Set<AuthToken>();
    public DbSet<DirectoryEntry> DirectoryEntries => Set<DirectoryEntry>();
    public DbSet<DirectorySyncRecord> DirectorySyncRecords => Set<DirectorySyncRecord>();
    public DbSet<PermissionPolicy> PermissionPolicies => Set<PermissionPolicy>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<AuthAuditLog> AuthAuditLogs => Set<AuthAuditLog>();
    public DbSet<IntegrationMessageReceipt> IntegrationMessageReceipts => Set<IntegrationMessageReceipt>();
    public DbSet<HRManagement.SharedKernel.Entities.EntityAuditLog> AuditLogs => Set<HRManagement.SharedKernel.Entities.EntityAuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AuthUser>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.Username }).IsUnique();
            entity.HasIndex(e => new { e.TenantId, e.Email }).IsUnique();
        });

        modelBuilder.Entity<AuthToken>(entity =>
        {
            entity.HasOne(e => e.User)
                  .WithMany(u => u.Tokens)
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.HasIndex(e => new { e.AuthUserId, e.RoleId }).IsUnique();

            entity.HasOne(e => e.AuthUser)
                  .WithMany(u => u.UserRoles)
                  .HasForeignKey(e => e.AuthUserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Role)
                  .WithMany()
                  .HasForeignKey(e => e.RoleId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.ConfigureIntegrationMessageReceipts();
        modelBuilder.ConfigureUtcDateTimeProperties();
        modelBuilder.ConfigureTenantQueryFilters(this);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UtcDateTimeNormalization.Apply(this);

        foreach (var entry in ChangeTracker.Entries<HRManagement.SharedKernel.Entities.BaseEntity>())
        {
            if (entry.State == EntityState.Modified)
                entry.Entity.UpdatedAt = DateTime.UtcNow;
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}
