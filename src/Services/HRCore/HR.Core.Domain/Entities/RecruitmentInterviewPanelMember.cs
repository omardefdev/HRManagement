using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

/// <summary>HCM-026: interview panel member for one interview slot.</summary>
public class RecruitmentInterviewPanelMember : AuditableEntity
{
    public const string ChairRole = "Chair";
    public const string MemberRole = "Member";

    [Display(Name = "المقابلة")]
    public Guid SlotId { get; set; }

    public virtual RecruitmentInterviewSlot? Slot { get; set; }

    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    public virtual Employee? Employee { get; set; }

    [Required, StringLength(20)]
    [Display(Name = "الدور")]
    public string Role { get; set; } = MemberRole;

    [Display(Name = "إقرار تعارض المصالح")]
    public bool CoiDeclared { get; set; }
}
