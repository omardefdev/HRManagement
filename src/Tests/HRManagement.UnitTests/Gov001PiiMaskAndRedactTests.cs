using HRManagement.SharedKernel;
using HR.Core.Domain.Entities;

namespace HRManagement.UnitTests;

public class Gov001PiiMaskAndRedactTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("1234", "****")]
    [InlineData("1234567890", "******7890")]
    [InlineData("AB12", "****")]
    public void NationalId_masks_preserving_last_four_when_long(string? input, string expected)
    {
        Assert.Equal(expected, PiiMask.NationalId(input));
    }

    [Fact]
    public void Sha256Hex_is_stable_and_64_chars()
    {
        var a = PiiMask.Sha256Hex("09010000001");
        var b = PiiMask.Sha256Hex("09010000001");
        Assert.Equal(a, b);
        Assert.Equal(64, a.Length);
        Assert.DoesNotContain("0901", a, StringComparison.Ordinal);
    }

    [Fact]
    public void Employee_NationalId_has_AuditRedact()
    {
        var prop = typeof(Employee).GetProperty(nameof(Employee.NationalId));
        Assert.NotNull(prop);
        Assert.NotNull(Attribute.GetCustomAttribute(prop!, typeof(AuditRedactAttribute)));
    }
}
