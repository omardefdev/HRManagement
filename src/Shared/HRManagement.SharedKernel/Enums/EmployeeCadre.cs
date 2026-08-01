using System.ComponentModel.DataAnnotations;

namespace HRManagement.SharedKernel.Enums;

/// <summary>تمييز شريحة الموظف ضمن هيكل الشعبة: كادر إشرافي مقابل تنفيذي.</summary>
public enum EmployeeCadre
{
    [Display(Name = "كادر إشرافي")]
    Supervisory = 0,

    [Display(Name = "موظف تنفيذي")]
    Executive = 1
}
