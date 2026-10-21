using System.ComponentModel.DataAnnotations;

namespace Dev.Analytics.Domain.Enums;

public enum ReportType
{
    [Display(Name = "أسبوعي")]
    Weekly,
    [Display(Name = "شهري")]
    Monthly,
    [Display(Name = "استثنائي")]
    AdHoc,
}