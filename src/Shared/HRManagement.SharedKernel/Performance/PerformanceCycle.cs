using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HRManagement.SharedKernel.Performance;

/// <summary>HCM-003: defines a performance evaluation cycle with phases and rating policy.</summary>
public class PerformanceCycle : BaseEntity
{
    [Required, MaxLength(50)]
    public string CycleCode { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string CycleName { get; set; } = string.Empty;

    public int Year { get; set; }

    public CycleStatus Status { get; set; } = CycleStatus.Draft;

    public DateTime PlanningStart { get; set; }

    public DateTime PlanningEnd { get; set; }

    public DateTime ReviewStart { get; set; }

    public DateTime ReviewEnd { get; set; }

    public DateTime? CalibrationStart { get; set; }

    public DateTime? CalibrationEnd { get; set; }

    /// <summary>Rating scale upper bound (e.g. 5 for 1-5 scale).</summary>
    public int RatingScaleMax { get; set; } = 5;

    /// <summary>Whether objectives are mandatory for this cycle.</summary>
    public bool ObjectivesRequired { get; set; } = true;

    /// <summary>Whether competency assessment is included.</summary>
    public bool CompetenciesIncluded { get; set; } = true;

    /// <summary>Template/form version used for this cycle.</summary>
    [MaxLength(50)]
    public string? TemplateVersion { get; set; }

    public bool IsInPhase(CyclePhase phase, DateTime asOf)
    {
        return phase switch
        {
            CyclePhase.Planning => asOf >= PlanningStart && asOf <= PlanningEnd,
            CyclePhase.Review => asOf >= ReviewStart && asOf <= ReviewEnd,
            CyclePhase.Calibration => CalibrationStart.HasValue && CalibrationEnd.HasValue
                                      && asOf >= CalibrationStart.Value && asOf <= CalibrationEnd.Value,
            _ => false
        };
    }
}

public enum CycleStatus
{
    [Display(Name = "مسودة")]
    Draft = 0,
    [Display(Name = "نشط")]
    Active = 1,
    [Display(Name = "قيد المراجعة")]
    InReview = 2,
    [Display(Name = "معايرة")]
    Calibrating = 3,
    [Display(Name = "مغلق")]
    Closed = 4,
    [Display(Name = "ملغى")]
    Cancelled = 5
}

public enum CyclePhase
{
    [Display(Name = "تخطيط")]
    Planning = 0,
    [Display(Name = "مراجعة")]
    Review = 1,
    [Display(Name = "معايرة")]
    Calibration = 2
}
