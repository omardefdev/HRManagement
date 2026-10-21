using System.ComponentModel.DataAnnotations;

namespace HR.Core.Domain.Entities;

/// <summary>EST-003: type of establishment change captured in a version row.</summary>
public enum EstablishmentOperationType
{
    [Display(Name = "إنشاء")]
    Create = 0,
    [Display(Name = "تعديل")]
    Update = 1,
    [Display(Name = "تعطيل")]
    Deactivate = 2,
    [Display(Name = "إنشاء منصب")]
    PositionCreated = 3,
    [Display(Name = "تجميد")]
    Freeze = 4,
    [Display(Name = "إلغاء تجميد")]
    Unfreeze = 5,
    [Display(Name = "إشغال")]
    Occupied = 6,
    [Display(Name = "إخلاء")]
    Vacated = 7
}
