using Microsoft.Extensions.Configuration;
using Payroll.Application.Features.BankAccounts;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-55 (D-PAY-11): the account's identifier. An IBAN passes by ISO 13616's form and mod 97, whatever its country (no national
/// length is assumed: Syria is outside the registry); a local number passes only for a defined bank, by that bank's pattern.
/// </summary>
public sealed class Pay055BankAccountNumberValidatorTests
{
    /// <summary>A test IBAN: the check digits are computed, never copied from a real account.</summary>
    public static string TestIban(string country, string bban) =>
        country + BankAccountNumberValidator.ComputeIbanCheckDigits(country, bban) + bban;

    private const string LocalPattern = "^[0-9]{4}-?[0-9]{8}$";

    private static BankAccountNumberCheck Iban(string? raw) => BankAccountNumberValidator.Validate(BankAccountKind.Iban, raw, false, null);

    [Theory]
    [InlineData("SY", "000100000000000055000001")]
    [InlineData("SY", "0055TESTACCOUNT0000001")]
    [InlineData("XX", "12345678901")]
    [InlineData("GB", "TEST00000055000001")]
    public void An_iban_with_computed_check_digits_passes_whatever_its_country(string country, string bban)
    {
        var iban = TestIban(country, bban);

        var check = Iban(iban);

        Assert.True(check.IsValid, $"{country}: {check.Error}");
        Assert.Equal(iban, check.Normalized);
    }

    [Fact]
    public void The_standards_own_example_passes()
    {
        // ISO 13616's worked example of the mod 97 calculation, as every description of the standard prints it.
        Assert.Equal("82", BankAccountNumberValidator.ComputeIbanCheckDigits("GB", "WEST12345698765432"));
        Assert.True(Iban("GB82 WEST 1234 5698 7654 32").IsValid);
    }

    [Fact]
    public void Spaces_and_case_are_normalised_before_the_check()
    {
        var iban = TestIban("SY", "0055TESTACCOUNT0000001");
        var typed = string.Join(' ', iban.ToLowerInvariant().Chunk(4).Select(c => new string(c))) + " \t";

        var check = Iban(typed);

        Assert.True(check.IsValid);
        Assert.Equal(iban, check.Normalized);
        Assert.Equal(iban, BankAccountNumberValidator.Normalize(typed));
    }

    [Fact]
    public void A_wrong_check_digit_is_refused()
    {
        var iban = TestIban("SY", "000100000000000055000001");
        var digit = iban[3] == '9' ? '8' : (char)(iban[3] + 1);
        var wrong = iban[..3] + digit + iban[4..];

        Assert.Equal(BankAccountNumberError.IbanCheckDigits, Iban(wrong).Error);
    }

    [Fact]
    public void One_changed_character_of_the_account_is_refused()
    {
        var iban = TestIban("SY", "000100000000000055000001");
        var changed = iban[..^1] + (iban[^1] == '1' ? '2' : '1');

        Assert.Equal(BankAccountNumberError.IbanCheckDigits, Iban(changed).Error);
    }

    [Theory]
    [InlineData("SY00000100000000000055000001")] // check digits 00 are never issued
    [InlineData("SY01000100000000000055000001")]
    [InlineData("SY99000100000000000055000001")]
    public void Check_digits_outside_02_to_98_are_refused(string iban)
    {
        Assert.Equal(BankAccountNumberError.IbanCheckDigits, Iban(iban).Error);
    }

    [Theory]
    [InlineData("1Y12000100000000000055")]            // the country is two letters
    [InlineData("SYA2000100000000000055")]            // the check digits are two digits
    [InlineData("SY12-0001-0000-0000-0055")]          // only letters and digits
    [InlineData("SY1200010000")]                      // shorter than any IBAN
    [InlineData("SY12000100000000000000000000000000055")] // longer than 34
    [InlineData("SY12٠٠٠١٠٠٠٠٠٠٠٠٠٠٠٠٠٠٥٥")]          // Arabic-Indic digits are not the standard's digits
    public void A_number_that_is_not_of_the_ibans_form_is_refused(string iban)
    {
        Assert.Equal(BankAccountNumberError.IbanForm, Iban(iban).Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_number_is_refused(string? raw)
    {
        Assert.Equal(BankAccountNumberError.Missing, Iban(raw).Error);
        Assert.Equal(BankAccountNumberError.Missing, BankAccountNumberValidator.Validate(BankAccountKind.Local, raw, true, LocalPattern).Error);
    }

    [Fact]
    public void A_local_number_passes_by_its_banks_pattern()
    {
        var check = BankAccountNumberValidator.Validate(BankAccountKind.Local, " 0012-34567890 ", true, LocalPattern);

        Assert.True(check.IsValid);
        Assert.Equal("0012-34567890", check.Normalized);
    }

    [Theory]
    [InlineData("0012-3456789")]        // one digit short
    [InlineData("0012-34567890X")]      // the pattern must match the whole number
    [InlineData("X0012-34567890")]
    public void A_local_number_that_does_not_match_its_banks_pattern_is_refused(string number)
    {
        Assert.Equal(BankAccountNumberError.LocalForm, BankAccountNumberValidator.Validate(BankAccountKind.Local, number, true, LocalPattern).Error);
    }

    [Fact]
    public void A_pattern_without_anchors_still_has_to_match_the_whole_number()
    {
        Assert.Equal(
            BankAccountNumberError.LocalForm,
            BankAccountNumberValidator.Validate(BankAccountKind.Local, "ABC12345678XYZ", true, "[0-9]{8}").Error);
        Assert.True(BankAccountNumberValidator.Validate(BankAccountKind.Local, "12345678", true, "[0-9]{8}").IsValid);
    }

    [Theory]
    [InlineData("1234567890", true)]
    [InlineData("123456789012", true)]          // the second alternative: an unanchored match would stop at the first
    [InlineData("12345678901", false)]
    [InlineData("1234567890123", false)]
    public void A_pattern_with_alternatives_is_matched_whole(string number, bool accepted)
    {
        var check = BankAccountNumberValidator.Validate(BankAccountKind.Local, number, true, @"\d{10}|\d{12}");

        Assert.Equal(accepted, check.IsValid);
    }

    [Theory]
    [InlineData("^[0-9]{12}$", true)]
    [InlineData(@"\d{10}|\d{12}", true)]
    [InlineData("[0-9", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_banks_pattern_is_usable_when_it_compiles(string? pattern, bool usable)
    {
        Assert.Equal(usable, BankAccountNumberValidator.IsUsablePattern(pattern));
    }

    [Fact]
    public void The_service_refuses_to_start_on_a_bank_list_it_cannot_use()
    {
        static PayrollBankOptions Options(params PayrollBankDefinition[] banks) => new() { Banks = [.. banks] };
        static PayrollBankDefinition Bank(string code, string name, string pattern) => new() { Code = code, NameAr = name, AccountRegex = pattern };

        Assert.Empty(new PayrollBankOptions().Problems());                                         // the production list: empty
        Assert.Empty(Options(Bank("CBS", "المصرف التجاري السوري", LocalPattern), Bank("REB", "المصرف العقاري", @"\d{10}")).Problems());
        Assert.Single(Options(Bank("CBS", "المصرف التجاري السوري", "[0-9")).Problems());             // a pattern that does not compile
        Assert.Single(Options(Bank("", "المصرف التجاري السوري", LocalPattern)).Problems());          // no code
        Assert.Single(Options(Bank("CBS", " ", LocalPattern)).Problems());                          // no name
        Assert.Single(Options(Bank("CBS", "أ", LocalPattern), Bank("cbs", "ب", LocalPattern)).Problems());   // one code twice

        // The host: the options fail their validation, so the service does not start.
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<IConfiguration>(
            services,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Payroll:Bank:Banks:0:Code"] = "CBS",
                ["Payroll:Bank:Banks:0:NameAr"] = "المصرف التجاري السوري",
                ["Payroll:Bank:Banks:0:AccountRegex"] = "[0-9",
            }).Build());
        Payroll.Application.DependencyInjection.AddPayrollApplication(services);
        using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        var options = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<PayrollBankOptions>>(provider);
        Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(() => options.Value);
    }

    [Fact]
    public void A_local_number_needs_a_bank_and_a_defined_one()
    {
        Assert.Equal(BankAccountNumberError.BankRequired, BankAccountNumberValidator.Validate(BankAccountKind.Local, "001234567890", false, null).Error);
        Assert.Equal(BankAccountNumberError.UnknownBank, BankAccountNumberValidator.Validate(BankAccountKind.Local, "001234567890", true, null).Error);
    }

    [Fact]
    public void An_iban_needs_no_bank_but_a_bank_code_that_is_given_must_be_defined()
    {
        var iban = TestIban("SY", "000100000000000055000001");

        Assert.True(BankAccountNumberValidator.Validate(BankAccountKind.Iban, iban, false, null).IsValid);
        Assert.True(BankAccountNumberValidator.Validate(BankAccountKind.Iban, iban, true, LocalPattern).IsValid);
        Assert.Equal(BankAccountNumberError.UnknownBank, BankAccountNumberValidator.Validate(BankAccountKind.Iban, iban, true, null).Error);
    }

    [Fact]
    public void A_pattern_that_does_not_compile_accepts_nothing()
    {
        Assert.Equal(BankAccountNumberError.LocalForm, BankAccountNumberValidator.Validate(BankAccountKind.Local, "12345678", true, "[0-9").Error);
    }

    [Theory]
    [InlineData("SY12000100000000000055000001", "••••••••••••••••••••••••0001")]
    [InlineData("12345", "•2345")]
    [InlineData("1234", "••••")]
    [InlineData("", "")]
    public void A_number_is_masked_to_its_last_four_characters(string number, string masked)
    {
        Assert.Equal(masked, EmployeeBankAccount.Mask(number));
        Assert.Equal(number.Length, EmployeeBankAccount.Mask(number).Length);
    }

    [Fact]
    public void Every_refusal_has_an_arabic_message_that_names_no_number()
    {
        foreach (var error in Enum.GetValues<BankAccountNumberError>().Where(e => e != BankAccountNumberError.None))
        {
            var message = BankAccountMessages.Of(error);
            Assert.False(string.IsNullOrWhiteSpace(message));
            Assert.Contains(message, c => c is >= '؀' and <= 'ۿ');
        }
    }

    // --- the bank list (Payroll:Bank:Banks) ---

    [Fact]
    public void The_bank_list_binds_from_configuration_and_finds_a_bank_by_its_code_without_case()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Payroll:Bank:Banks:0:Code"] = "CBS",
            ["Payroll:Bank:Banks:0:NameAr"] = "المصرف التجاري السوري",
            ["Payroll:Bank:Banks:0:AccountRegex"] = LocalPattern,
        }).Build();

        var options = configuration.GetSection(PayrollBankOptions.SectionName).Get<PayrollBankOptions>()!;

        Assert.Single(options.Banks);
        Assert.Equal(LocalPattern, options.Find(" cbs ")!.AccountRegex);
        Assert.Null(options.Find("REB"));
        Assert.Null(options.Find(null));
    }

    [Theory]
    [InlineData("")]     // the prod-like stack's mirror: Payroll__Bank__Banks: ""
    [InlineData(null)]
    public void An_empty_list_in_the_environment_binds_to_no_bank(string? value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Payroll:Bank:Banks"] = value,
        }).Build();

        var options = configuration.GetSection(PayrollBankOptions.SectionName).Get<PayrollBankOptions>() ?? new PayrollBankOptions();

        Assert.Empty(options.Banks ?? []);
        Assert.Null(options.Find("CBS"));
    }
}
