using System.ComponentModel.DataAnnotations;

namespace HRManagement.ServiceDefaults.Connectors;

/// <summary>وضع تشغيل موصل حكومي: ملف، محاكاة، sandbox، أو ربط حي (INT-004).</summary>
public enum IntegrationConnectorMode
{
    [Display(Name = "ملف")]
    File = 0,
    [Display(Name = "وهمي")]
    Stub = 1,
    [Display(Name = "بيئة تجريبية")]
    Sandbox = 2,
    [Display(Name = "مباشر")]
    Live = 3
}
