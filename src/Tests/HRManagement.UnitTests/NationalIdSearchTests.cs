using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel;

namespace HRManagement.UnitTests;

public class NationalIdSearchTests
{
    [Theory]
    [InlineData("01020304050")]
    [InlineData("123456789")]
    [InlineData("12345678901")]
    public void LooksLikeNationalId_accepts_digit_patterns(string term) =>
        Assert.True(NationalIdSearch.LooksLikeNationalId(term));

    [Theory]
    [InlineData(AcceptanceSeedIds.NationalIdBr1)]
    [InlineData("ACC010000002")]
    public void LooksLikeNationalId_accepts_acceptance_alphanumeric_ids(string term) =>
        Assert.True(NationalIdSearch.LooksLikeNationalId(term));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("12345")]
    public void LooksLikeNationalId_rejects_non_identifiers(string? term) =>
        Assert.False(NationalIdSearch.LooksLikeNationalId(term));

    [Fact]
    public void ComputeHash_is_stable_sha256_hex()
    {
        var hash = NationalIdSearch.ComputeHash(AcceptanceSeedIds.NationalIdBr1);
        Assert.Equal(PiiMask.Sha256Hex(AcceptanceSeedIds.NationalIdBr1), hash);
        Assert.Equal(64, hash.Length);
        Assert.DoesNotContain(AcceptanceSeedIds.NationalIdBr1, hash, StringComparison.Ordinal);
    }

    [Fact]
    public void EmployeesController_search_does_not_use_plaintext_contains()
    {
        var path = Path.Combine(
            ResolveRepoRoot(),
            "src", "Services", "HRCore", "HR.Core.Web", "Controllers", "EmployeesController.cs");
        var source = System.IO.File.ReadAllText(path);

        Assert.Contains("NationalIdHash", source, StringComparison.Ordinal);
        Assert.DoesNotContain("NationalId.Contains", source, StringComparison.Ordinal);
    }

    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "src", "Gateway", "HRManagement.Gateway", "Program.cs")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("repo root");
    }
}
