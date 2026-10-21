using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.Localization;
using HRManagement.SharedKernel.MasterData;
using Payroll.Domain.Enums;

namespace HRManagement.UnitTests;

public class L10n003ArabicUiTests
{
    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "scripts", "l10n003-arabic-ui-scan.ps1")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("repo root");
    }

    [Fact]
    public void EnumDisplayExtensions_returns_arabic_for_LeaveType()
    {
        var label = EnumDisplayExtensions.GetDisplayName(HR.Contracts.Domain.Enums.LeaveType.Annual);
        Assert.Equal("سنوية", label);
    }

    [Fact]
    public void EnumDisplayExtensions_returns_arabic_for_EmploymentStatus()
    {
        var label = EnumDisplayExtensions.GetDisplayName(EmploymentStatus.Active);
        Assert.Equal("نشط", label);
    }

    [Fact]
    public void EnumDisplayExtensions_returns_arabic_for_PayrollRunStatus()
    {
        var label = EnumDisplayExtensions.GetDisplayName(PayrollRunStatus.Draft);
        Assert.Equal("مسودة", label);
    }

    [Fact]
    public void Stored_enum_display_resolves_EmploymentStatus_string()
    {
        var label = EnumDisplayExtensions.GetStoredEnumDisplayName(nameof(EmploymentStatus), "OnLeave");
        Assert.Equal("في إجازة", label);
    }

    [Fact]
    public void Stored_enum_display_falls_back_for_lowercase_active()
    {
        var label = EnumDisplayExtensions.GetStoredEnumDisplayName(nameof(EmploymentStatus), "active");
        Assert.Equal("نشط", label);
    }

    [Fact]
    public void LocalizedNameExtensions_prefers_arabic_for_ar_SY()
    {
        var entry = new ReferenceCatalogEntry
        {
            Code = "G1",
            NameAr = "الدرجة الأولى",
            NameEn = "Grade 1"
        };

        var label = entry.GetLocalizedName(new System.Globalization.CultureInfo("ar-SY"));
        Assert.Equal("الدرجة الأولى", label);
    }

    [Fact]
    public void LocalizedNameExtensions_falls_back_to_english_when_arabic_missing()
    {
        var entry = new ReferenceCatalogEntry
        {
            Code = "G2",
            NameAr = "",
            NameEn = "Grade 2"
        };

        var label = entry.GetLocalizedName(new System.Globalization.CultureInfo("ar-SY"));
        Assert.Equal("Grade 2", label);
    }

    [Fact]
    public void Validate_script_exists()
    {
        var root = ResolveRepoRoot();
        Assert.True(System.IO.File.Exists(Path.Combine(root, "scripts", "l10n003-arabic-ui-scan.ps1")));
    }

    [Fact]
    public void Jquery_validate_ar_exists()
    {
        var root = ResolveRepoRoot();
        var path = Path.Combine(root, "src", "Shared", "HRManagement.Web.Shared", "wwwroot", "js", "jquery-validate-ar.js");
        Assert.True(System.IO.File.Exists(path));
        var content = System.IO.File.ReadAllText(path);
        Assert.Contains("هذا الحقل مطلوب", content);
    }

    [Fact]
    public void Tag_helpers_exist()
    {
        var root = ResolveRepoRoot();
        Assert.True(System.IO.File.Exists(Path.Combine(root, "src", "Shared", "HRManagement.Web.Shared", "TagHelpers", "HrEnumDisplayTagHelper.cs")));
        Assert.True(System.IO.File.Exists(Path.Combine(root, "src", "Shared", "HRManagement.Web.Shared", "TagHelpers", "HrStoredEnumTagHelper.cs")));
    }
}
