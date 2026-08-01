using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.ServiceDefaults;

/// <summary>Durable register/release intent written in the same TX as the holder entity (FILE-001-C).</summary>
public class FileReferenceOutboxMessage : BaseEntity
{
    public Guid FileId { get; set; }

    [Required]
    [StringLength(256)]
    public string HolderKey { get; set; } = default!;

    /// <summary>Register or Release.</summary>
    [Required]
    [StringLength(32)]
    public string Operation { get; set; } = default!;

    [StringLength(32)]
    public string Status { get; set; } = FileReferenceOutboxStatus.Pending;

    public int Attempt { get; set; }

    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;

    [StringLength(2000)]
    public string? LastError { get; set; }

    public DateTime? ProcessedAt { get; set; }
}

public static class FileReferenceOutboxStatus
{
    public const string Pending = "Pending";
    public const string Succeeded = "Succeeded";
    public const string Dead = "Dead";
}

public static class FileReferenceOutboxOps
{
    public const string Register = "Register";
    public const string Release = "Release";
}

public static class FileReferenceOutboxModelBuilderExtensions
{
    public static void ConfigureFileReferenceOutbox(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FileReferenceOutboxMessage>(entity =>
        {
            entity.ToTable("FileReferenceOutbox");
            entity.HasIndex(x => new { x.Status, x.NextAttemptAt });
            entity.HasIndex(x => new { x.FileId, x.HolderKey, x.Operation, x.Status });
            entity.Property(x => x.Status).HasDefaultValue(FileReferenceOutboxStatus.Pending);
        });
    }
}
