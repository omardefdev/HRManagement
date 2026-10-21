using System.ComponentModel.DataAnnotations;

namespace Records.Archive.Domain.Enums;

public enum ArchiveChecklistStep
{
    [Display(Name = "مصنّف")]
    Classified = 0,
    [Display(Name = "مُدخَل")]
    Ingested = 1,
    [Display(Name = "محجوز")]
    Held = 2,
    [Display(Name = "مُرفَع")]
    Released = 3,
    [Display(Name = "طلب إتلاف")]
    DisposeRequested = 4,
    [Display(Name = "إتلاف معتمد")]
    DisposeApproved = 5,
    [Display(Name = "مُستَبعَد")]
    Disposed = 6
}
