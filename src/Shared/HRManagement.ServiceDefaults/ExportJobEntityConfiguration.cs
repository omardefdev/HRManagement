using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Exports;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.ServiceDefaults;

public static class ExportJobEntityConfiguration
{
    public static void ConfigureExportJobs(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ExportJob>(entity =>
        {
            entity.ToTable("ExportJobs");
            entity.HasIndex(e => new { e.TenantId, e.Status, e.CreatedAt });
            entity.HasIndex(e => e.RequestedByUserId);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(e => e.FilterJson).HasColumnType("text");
            entity.Property(e => e.AuthSnapshotJson).HasColumnType("text");
        });
    }
}

/// <summary>Arabic UX helpers for PERF-004 enqueue responses.</summary>
public static class ExportJobMessages
{
    public const string EnqueuedArabic =
        "تم جدولة التصدير في الخلفية. يمكنك متابعة الحالة عبر معرّف المهمة.";

    public static object AcceptedBody(Guid jobId) => new
    {
        jobId,
        message = EnqueuedArabic,
        statusUrlHint = $"export-jobs/{jobId}"
    };
}
