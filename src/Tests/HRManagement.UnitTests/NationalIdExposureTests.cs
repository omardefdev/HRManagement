using System.Reflection;
using HRManagement.MessageContracts.Events;
using HRManagement.SharedKernel;

namespace HRManagement.UnitTests;

/// <summary>
/// The national identifier is the most sensitive field the system holds and belongs only to
/// HR Core. It used to ride on <see cref="IEmployeeCreated"/>, so every subscriber persisted a
/// copy: analytics payloads, IT ticket descriptions, and directory usernames. These tests keep it
/// off the wire.
/// </summary>
public class NationalIdExposureTests
{
    [Fact]
    public void EmployeeCreated_CarriesAHash_NotTheRawNumber()
    {
        var properties = typeof(IEmployeeCreated)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToList();

        Assert.Contains("NationalIdHash", properties);
        Assert.DoesNotContain("NationalId", properties);
    }

    [Fact]
    public void No_integration_event_exposes_a_raw_national_id()
    {
        var contracts = typeof(IEmployeeCreated).Assembly
            .GetTypes()
            .Where(t => t.IsInterface && t.Namespace?.Contains("Events", StringComparison.Ordinal) == true)
            .ToList();

        Assert.NotEmpty(contracts);

        var offenders = contracts
            .SelectMany(t => t.GetProperties().Select(p => (Type: t, Property: p.Name)))
            .Where(x => string.Equals(x.Property, "NationalId", StringComparison.Ordinal))
            .Select(x => $"{x.Type.Name}.{x.Property}")
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Integration events must publish NationalIdHash, not the raw identifier: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void Hash_is_stable_and_does_not_reveal_the_input()
    {
        const string nationalId = "01020304050";

        var hash = PiiMask.Sha256Hex(nationalId);

        Assert.Equal(PiiMask.Sha256Hex(nationalId), hash);
        Assert.DoesNotContain(nationalId, hash, StringComparison.Ordinal);
        Assert.Equal(64, hash.Length);
    }

    [Fact]
    public void Hash_of_empty_input_is_empty()
    {
        Assert.Equal(string.Empty, PiiMask.Sha256Hex(null));
        Assert.Equal(string.Empty, PiiMask.Sha256Hex(""));
    }
}
