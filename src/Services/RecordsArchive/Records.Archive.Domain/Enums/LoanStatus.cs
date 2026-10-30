using System.ComponentModel.DataAnnotations;

namespace Records.Archive.Domain.Enums;

public enum LoanStatus
{
    [Display(Name = "معار")]
    OnLoan,
    [Display(Name = "مُرتَجَع")]
    Returned,
    [Display(Name = "متأخر")]
    Overdue,
}
