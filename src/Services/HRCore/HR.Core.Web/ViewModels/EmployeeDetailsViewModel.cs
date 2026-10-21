using HR.Core.Domain.Entities;

namespace HR.Core.Web.ViewModels;

public class EmployeeDetailsViewModel
{
    public Employee Employee { get; set; } = null!;
    public IReadOnlyList<Qualification> Qualifications { get; set; } = [];
    public IReadOnlyList<ContactInfo> Contacts { get; set; } = [];
    public IReadOnlyList<EmployeeDocument> Documents { get; set; } = [];
    public IReadOnlyList<EmploymentRecord> EmploymentRecords { get; set; } = [];
    public IReadOnlyList<PerformanceEvaluation> Evaluations { get; set; } = [];
    public IReadOnlyList<EmploymentStatusHistory> StatusHistory { get; set; } = [];
    public EmployeeProfile? Profile { get; set; }

    public QualificationCreateViewModel QualificationCreate { get; set; } = new();
    public ContactCreateViewModel ContactCreate { get; set; } = new();
    public DocumentCreateViewModel DocumentCreate { get; set; } = new();
    public EvaluationCreateViewModel EvaluationCreate { get; set; } = new();
    public bool ShowQualificationCreateModal { get; set; }
    public bool ShowContactCreateModal { get; set; }
    public bool ShowDocumentCreateModal { get; set; }
    public bool ShowEvaluationCreateModal { get; set; }

    /// <summary>AC-26: masked or full national ID for display.</summary>
    public string DisplayNationalId { get; set; } = string.Empty;

    public bool CanViewFullNationalId { get; set; }

    /// <summary>SYR-003: current employment record service regime (read-only).</summary>
    public string? ServiceRegimeLabel { get; set; }

    /// <summary>SYR-001: resolved legislation label for the current service regime.</summary>
    public string? ServiceRegimeLegalSourceLabel { get; set; }

    /// <summary>SYR-007: regime transition history (read-only).</summary>
    public IReadOnlyList<EmploymentRegimeTransition> RegimeTransitions { get; set; } = [];
}

public class EmployeeDetailsModalState
{
    public QualificationCreateViewModel? QualificationCreate { get; set; }
    public ContactCreateViewModel? ContactCreate { get; set; }
    public DocumentCreateViewModel? DocumentCreate { get; set; }
    public EvaluationCreateViewModel? EvaluationCreate { get; set; }
    public bool ShowQualificationCreateModal { get; set; }
    public bool ShowContactCreateModal { get; set; }
    public bool ShowDocumentCreateModal { get; set; }
    public bool ShowEvaluationCreateModal { get; set; }
}
