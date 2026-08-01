using HRManagement.SharedKernel.Documents;

namespace HRManagement.UnitTests;

public class Doc001DocumentsTests
{
    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "docs", "DOC_001_OFFICIAL_DOCUMENTS.md")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("repo root");
    }

    [Fact]
    public void Template_defaults_to_pdfa_and_qr_and_signature()
    {
        var template = new OfficialDocumentTemplate();
        Assert.Equal(OfficialDocumentFormat.PdfA, template.OutputFormat);
        Assert.True(template.QrVerificationEnabled);
        Assert.True(template.SignatureRequired);
    }

    [Fact]
    public void Template_correction_creates_superseding_by_default()
    {
        var template = new OfficialDocumentTemplate();
        Assert.True(template.CorrectionCreatesSuperseding);
    }

    [Fact]
    public void Template_accessibility_required_by_default()
    {
        var template = new OfficialDocumentTemplate();
        Assert.True(template.AccessibilityRequired);
    }

    [Fact]
    public void Template_has_required_properties()
    {
        var template = new OfficialDocumentTemplate
        {
            TenantId = Guid.NewGuid(),
            TemplateCode = "DEC",
            NameAr = "قرار إداري",
            NameEn = "Administrative Decision",
            Version = "1.0.0",
            EffectiveFromUtc = DateTime.UtcNow,
            Status = TemplateStatus.Active,
            DataBindingSchemaJson = "{\"required\":[\"employeeName\",\"decisionNumber\"]}",
            TemplateBody = "<html>...</html>"
        };

        Assert.NotEqual(Guid.Empty, template.Id);
        Assert.NotEqual(Guid.Empty, template.TenantId);
        Assert.Equal("DEC", template.TemplateCode);
        Assert.Equal(TemplateStatus.Active, template.Status);
    }

    [Fact]
    public void Template_status_lifecycle_values_exist()
    {
        Assert.Equal(0, (int)TemplateStatus.Draft);
        Assert.Equal(1, (int)TemplateStatus.Active);
        Assert.Equal(2, (int)TemplateStatus.Deprecated);
        Assert.Equal(3, (int)TemplateStatus.Archived);
    }

    [Fact]
    public void Document_format_values_exist()
    {
        Assert.Equal(0, (int)OfficialDocumentFormat.PdfA);
        Assert.Equal(1, (int)OfficialDocumentFormat.Html);
        Assert.Equal(2, (int)OfficialDocumentFormat.Docx);
    }

    [Fact]
    public void Template_version_defaults_to_1_0_0()
    {
        var template = new OfficialDocumentTemplate();
        Assert.Equal("1.0.0", template.Version);
    }

    [Fact]
    public void Doc001_docs_and_scripts_exist()
    {
        var root = ResolveRepoRoot();
        Assert.True(System.IO.File.Exists(Path.Combine(root, "docs", "DOC_001_OFFICIAL_DOCUMENTS.md")));
        Assert.True(System.IO.File.Exists(Path.Combine(root, "scripts", "doc001-validate.ps1")));
    }

    [Fact]
    public void Doc001_validate_script_passes()
    {
        var root = ResolveRepoRoot();
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell",
            ArgumentList = { "-NoProfile", "-File", Path.Combine(root, "scripts", "doc001-validate.ps1"), "-RepoRoot", root },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.WaitForExit(30_000);
        Assert.Equal(0, proc.ExitCode);
    }
}
