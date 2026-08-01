using System.Text.Json;

namespace HRManagement.UnitTests;

using HRManagement.TestSupport;

/// <summary>SYR-011: tests for payment orders/banks channel profile artifacts.</summary>
public class Syr011PaymentTests
{

    [Fact]
    public void Channel_profile_exists_and_is_valid_json()
    {
        var path = RepoPaths.Resolve("docs", "syr", "payment-channel-profile.json");
        var content = System.IO.File.ReadAllText(path);
        var doc = JsonDocument.Parse(content);
        Assert.Equal("SYR-011", doc.RootElement.GetProperty("card").GetString());
    }

    [Fact]
    public void Channel_profile_has_at_least_three_channels()
    {
        var path = RepoPaths.Resolve("docs", "syr", "payment-channel-profile.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var channels = doc.RootElement.GetProperty("channels");
        Assert.True(channels.GetArrayLength() >= 3);
    }

    [Fact]
    public void Channel_profile_has_payment_order_states()
    {
        var path = RepoPaths.Resolve("docs", "syr", "payment-channel-profile.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var states = doc.RootElement.GetProperty("paymentOrderStates");
        Assert.True(states.GetArrayLength() >= 5);
    }

    [Fact]
    public void Channel_profile_has_iban_validation()
    {
        var path = RepoPaths.Resolve("docs", "syr", "payment-channel-profile.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var rules = doc.RootElement.GetProperty("validationRules");
        Assert.False(string.IsNullOrEmpty(rules.GetProperty("ibanFormat").GetString()));
    }

    [Fact]
    public void Channel_profile_has_reconciliation_invariant()
    {
        var path = RepoPaths.Resolve("docs", "syr", "payment-channel-profile.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var recon = doc.RootElement.GetProperty("reconciliation");
        Assert.False(string.IsNullOrEmpty(recon.GetProperty("invariant").GetString()));
    }

    [Fact]
    public void Channel_profile_requires_file_signing()
    {
        var path = RepoPaths.Resolve("docs", "syr", "payment-channel-profile.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var sec = doc.RootElement.GetProperty("security");
        Assert.True(sec.GetProperty("fileSigningRequired").GetBoolean());
    }

    [Fact]
    public void Doc_references_iban_and_reconciliation()
    {
        var path = RepoPaths.Resolve("docs", "SYR_011_PAYMENT_ORDERS.md");
        var content = System.IO.File.ReadAllText(path);
        Assert.Contains("SYR-011", content);
        Assert.Contains("IBAN", content);
        Assert.Matches("(?i)reconcil", content);
    }

    [Fact]
    public void Each_channel_has_required_fields()
    {
        var path = RepoPaths.Resolve("docs", "syr", "payment-channel-profile.json");
        var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        foreach (var ch in doc.RootElement.GetProperty("channels").EnumerateArray())
        {
            Assert.False(string.IsNullOrEmpty(ch.GetProperty("id").GetString()));
            Assert.False(string.IsNullOrEmpty(ch.GetProperty("name").GetString()));
            Assert.False(string.IsNullOrEmpty(ch.GetProperty("type").GetString()));
            Assert.False(string.IsNullOrEmpty(ch.GetProperty("format").GetString()));
        }
    }
}
