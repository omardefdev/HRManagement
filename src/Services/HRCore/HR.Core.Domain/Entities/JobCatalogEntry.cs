using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

/// <summary>
/// EST-001: A job title/description in the organization's job catalog.
/// Maps to the official classification of positions by grade and function.
/// </summary>
public class JobCatalogEntry : BaseEntity
{
    [Required]
    [StringLength(20)]
    public string JobCode { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string TitleAr { get; set; } = string.Empty;

    [StringLength(200)]
    public string? TitleEn { get; set; }

    [StringLength(2000)]
    public string? DescriptionAr { get; set; }

    [Required]
    [StringLength(20)]
    public string GradeCode { get; set; } = string.Empty;

    [StringLength(100)]
    public string? OccupationalGroup { get; set; }

    public bool IsActive { get; set; } = true;
}
