using System.Diagnostics;
using HRManagement.SharedKernel.Localization;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

public class L10n002ArabicNormalizationTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "l10n002-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Theory]
    [InlineData("\u0623\u062D\u0645\u062F", "\u0627\u062D\u0645\u062F")] // أحمد → احمد
    [InlineData("\u0625\u0628\u0631\u0627\u0647\u064A\u0645", "\u0627\u0628\u0631\u0627\u0647\u064A\u0645")] // إبراهيم → ابراهيم
    [InlineData("\u0622\u0645\u0646\u0629", "\u0627\u0645\u0646\u0647")] // آمنة → امنه
    public void Hamza_alef_variants_normalize_to_bare_alef(string input, string expected)
    {
        Assert.Equal(expected, ArabicTextNormalizer.Normalize(input));
    }

    [Fact]
    public void Diacritics_are_removed()
    {
        // أحمَد (with fatha on meem) → احمد
        var withDiacritics = "\u0623\u062D\u0645\u064E\u062F";
        var normalized = ArabicTextNormalizer.Normalize(withDiacritics);
        Assert.DoesNotContain("\u064E", normalized); // no fatha
        Assert.Equal("\u0627\u062D\u0645\u062F", normalized);
    }

    [Fact]
    public void Alef_maqsura_normalizes_to_ya()
    {
        // مصطفى → مصطفي
        var input = "\u0645\u0635\u0637\u0641\u0649";
        var expected = "\u0645\u0635\u0637\u0641\u064A";
        Assert.Equal(expected, ArabicTextNormalizer.Normalize(input));
    }

    [Fact]
    public void Ta_marbuta_normalizes_to_ha()
    {
        // فاطمة → فاطمه
        var input = "\u0641\u0627\u0637\u0645\u0629";
        var expected = "\u0641\u0627\u0637\u0645\u0647";
        Assert.Equal(expected, ArabicTextNormalizer.Normalize(input));
    }

    [Fact]
    public void Tatweel_is_removed()
    {
        // عـلـي → علي
        var input = "\u0639\u0640\u0644\u0640\u064A";
        var expected = "\u0639\u0644\u064A";
        Assert.Equal(expected, ArabicTextNormalizer.Normalize(input));
    }

    [Fact]
    public void Arabic_indic_digits_normalize_to_ascii()
    {
        // ١٢٣ → 123
        var input = "\u0661\u0662\u0663";
        Assert.Equal("123", ArabicTextNormalizer.Normalize(input));
    }

    [Fact]
    public void Extended_arabic_indic_digits_normalize_to_ascii()
    {
        // ۱۲۳ → 123
        var input = "\u06F1\u06F2\u06F3";
        Assert.Equal("123", ArabicTextNormalizer.Normalize(input));
    }

    [Fact]
    public void Whitespace_is_collapsed()
    {
        var input = "\u0645\u062D\u0645\u062F  \u0639\u0644\u064A";
        var normalized = ArabicTextNormalizer.Normalize(input);
        Assert.DoesNotContain("  ", normalized);
    }

    [Fact]
    public void Null_and_empty_return_empty()
    {
        Assert.Equal(string.Empty, ArabicTextNormalizer.Normalize(null));
        Assert.Equal(string.Empty, ArabicTextNormalizer.Normalize(""));
        Assert.Equal(string.Empty, ArabicTextNormalizer.Normalize("   "));
    }

    [Fact]
    public void AreEquivalent_matches_variant_spellings()
    {
        Assert.True(ArabicTextNormalizer.AreEquivalent(
            "\u0623\u062D\u0645\u062F", // أحمد
            "\u0627\u062D\u0645\u062F"  // احمد
        ));
    }

    [Fact]
    public void AreEquivalent_rejects_different_names()
    {
        Assert.False(ArabicTextNormalizer.AreEquivalent(
            "\u0623\u062D\u0645\u062F", // أحمد
            "\u0645\u062D\u0645\u062F"  // محمد
        ));
    }

    [Fact]
    public void RuleVersion_is_positive()
    {
        Assert.True(ArabicTextNormalizer.RuleVersion >= 1);
    }

    [Fact]
    public void Latin_text_passes_through_unchanged()
    {
        Assert.Equal("Hello World", ArabicTextNormalizer.Normalize("Hello World"));
    }

    [Fact]
    public void Mixed_arabic_latin_normalized_correctly()
    {
        var input = "\u0623\u062D\u0645\u062F Ahmed";
        var normalized = ArabicTextNormalizer.Normalize(input);
        Assert.Contains("Ahmed", normalized);
        Assert.StartsWith("\u0627", normalized);
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "L10N_002_ARABIC_NORMALIZATION.md")));
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "l10n002-validate.ps1");
        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -RepoRoot \"{RepoRoot}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = RepoRoot
        };
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(300_000);
        Assert.True(proc.ExitCode == 0, $"validate failed ({proc.ExitCode}): {stdout}\n{stderr}");
        Assert.Contains("PASS", stdout, StringComparison.OrdinalIgnoreCase);
    }
}
