using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HRManagement.SharedKernel.Entities;

/// <summary>INT-003: human intervention queue for stuck process managers.</summary>
public class ProcessIntervention : BaseEntity
{
    [Required, MaxLength(100)]
    public string SagaType { get; set; } = "";

    /// <summary>Business correlation (SeparationRecordId, ServiceTransactionId, …).</summary>
    public Guid CorrelationId { get; set; }

    [Required, MaxLength(200)]
    public string Reason { get; set; } = "";

    [MaxLength(100)]
    public string? FailedParty { get; set; }

    [Required, MaxLength(50)]
    public string Status { get; set; } = ProcessInterventionStatuses.Open;

    public DateTime? ResolvedAt { get; set; }

    public Guid? ResolvedBy { get; set; }

    [MaxLength(500)]
    public string? ResolutionNotes { get; set; }
}

public static class ProcessInterventionStatuses
{
    public const string Open = "Open";
    public const string Resolved = "Resolved";
    public const string Cancelled = "Cancelled";
}

public static class SagaTypeNames
{
    public const string Separation = "Separation";
    public const string Hire = "Hire";
    public const string Transfer = "Transfer";
    public const string Secondment = "Secondment";
}
