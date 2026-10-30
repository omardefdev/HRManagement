using System.ComponentModel.DataAnnotations;

namespace Records.Archive.Domain.Enums;

public enum ArchiveFormat
{
    [Display(Name = "ورقي")]
    Physical,
    [Display(Name = "رقمي")]
    Digital,
}
