using System.ComponentModel.DataAnnotations;

namespace HR.Core.Web.ViewModels;

/// <summary>HCM-030 (H2): <c>/careers/my/login</c> — reference number + national id (posted, never in the URL).</summary>
public sealed class CandidatePortalLoginViewModel
{
    [Display(Name = "الرقم المرجعي")]
    [StringLength(40)]
    public string? ReferenceNumber { get; set; }

    [Display(Name = "الرقم الوطني")]
    [StringLength(20)]
    public string? NationalId { get; set; }
}

/// <summary>
/// HCM-030 (H2): code entry. The reference number travels in a hidden field (the gateway does not forward the TempData
/// cookie, and the national id is not repeated).
/// </summary>
public sealed class CandidatePortalCodeViewModel
{
    [StringLength(40)]
    public string? ReferenceNumber { get; set; }

    [Display(Name = "رمز التحقق")]
    [StringLength(20)]
    public string? Code { get; set; }

    /// <summary>The neutral message shown after a code request.</summary>
    public string? Notice { get; set; }
}
