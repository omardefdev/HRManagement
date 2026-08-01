using System.ComponentModel.DataAnnotations;

namespace HRManagement.SharedKernel.Documents;

/// <summary>DOC-001: template format for official document generation.</summary>
public enum OfficialDocumentFormat
{
    PdfA = 0,
    Html = 1,
    Docx = 2
}

/// <summary>DOC-001: template status lifecycle.</summary>
public enum TemplateStatus
{
    Draft = 0,
    Active = 1,
    Deprecated = 2,
    Archived = 3
}

/// <summary>
/// DOC-001: versioned template for official documents (decisions, certificates, letters).
/// Templates are bound to data-binding fields and produce immutable official documents
/// with QR verification and digital signature/seal.
/// </summary>
public class OfficialDocumentTemplate
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    /// <summary>Template code used in numbering (e.g. "DEC", "CRT", "LTR").</summary>
    [Required]
    [MaxLength(20)]
    public string TemplateCode { get; set; } = string.Empty;

    /// <summary>Human-readable name in primary language.</summary>
    [Required]
    [MaxLength(200)]
    public string NameAr { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? NameEn { get; set; }

    /// <summary>Semantic version (e.g. "1.0.0"). Template changes create new versions.</summary>
    [Required]
    [MaxLength(20)]
    public string Version { get; set; } = "1.0.0";

    /// <summary>Effective date from which this template version is used.</summary>
    public DateTime EffectiveFromUtc { get; set; }

    /// <summary>Optional end date when this version is superseded.</summary>
    public DateTime? EffectiveToUtc { get; set; }

    public TemplateStatus Status { get; set; } = TemplateStatus.Draft;

    public OfficialDocumentFormat OutputFormat { get; set; } = OfficialDocumentFormat.PdfA;

    /// <summary>JSON schema describing the data-binding fields required by this template.</summary>
    public string DataBindingSchemaJson { get; set; } = "{}";

    /// <summary>Template body content (HTML/Razor/XSLT depending on engine).</summary>
    public string TemplateBody { get; set; } = string.Empty;

    /// <summary>When true, produced documents include a QR code for independent verification.</summary>
    public bool QrVerificationEnabled { get; set; } = true;

    /// <summary>When true, produced documents require a digital signature or seal.</summary>
    public bool SignatureRequired { get; set; } = true;

    /// <summary>When true, corrections create a new superseding document rather than editing in place.</summary>
    public bool CorrectionCreatesSuperseding { get; set; } = true;

    /// <summary>Accessibility: template must produce documents meeting WCAG 2.2 AA for tagged PDF.</summary>
    public bool AccessibilityRequired { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
