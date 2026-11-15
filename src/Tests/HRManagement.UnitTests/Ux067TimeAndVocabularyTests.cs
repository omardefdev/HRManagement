using System.Globalization;
using HR.Core.Domain.Entities.Policy;
using HRManagement.ServiceDefaults.Localization;
using HRManagement.SharedKernel.Localization;
using HRManagement.SharedKernel.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-67 (D-67-2, G-UX-05, P-7): every displayed time is the stored UTC value in the configured zone, in one format; a time a user
/// enters is local and stored as UTC; a calendar date is never converted. And (G-UX-03, P-5) the vocabulary the pages print.
/// </summary>
public sealed class Ux067TimeAndVocabularyTests
{
    private static readonly TimeZoneInfo Damascus = HrLocalTime.ResolveZone("Asia/Damascus");

    private static DateTime Utc(int y, int mo, int d, int h, int mi) => new(y, mo, d, h, mi, 0, DateTimeKind.Utc);

    [Fact]
    public void The_default_zone_is_Damascus_three_hours_ahead_of_utc()
    {
        Assert.Equal(TimeSpan.FromHours(3), HrLocalTime.DefaultZone.GetUtcOffset(Utc(2026, 11, 13, 8, 0)));
        Assert.Equal(TimeSpan.FromHours(3), HrLocalTime.DefaultZone.GetUtcOffset(Utc(2026, 7, 1, 8, 0)));
    }

    [Theory]
    // a time during the day
    [InlineData(2026, 11, 13, 8, 6, "2026/11/13 11:06", "2026/11/13")]
    // just before midnight UTC: already the next day in Damascus
    [InlineData(2026, 11, 13, 21, 30, "2026/11/14 00:30", "2026/11/14")]
    // the end of a month
    [InlineData(2026, 11, 30, 22, 15, "2026/12/01 01:15", "2026/12/01")]
    // the end of a year
    [InlineData(2026, 12, 31, 23, 59, "2027/01/01 02:59", "2027/01/01")]
    // midnight UTC stays the same calendar day
    [InlineData(2026, 3, 1, 0, 0, "2026/03/01 03:00", "2026/03/01")]
    public void A_stored_time_reads_in_Damascus_in_one_format(int y, int mo, int d, int h, int mi, string dateTime, string date)
    {
        var stored = Utc(y, mo, d, h, mi);
        Assert.Equal(dateTime, HrLocalTime.FormatDateTime(stored, Damascus));
        Assert.Equal(date, HrLocalTime.FormatDate(stored, Damascus));
        // EF and SQLite hand back stored values without a kind: they are UTC all the same.
        Assert.Equal(dateTime, HrLocalTime.FormatDateTime(DateTime.SpecifyKind(stored, DateTimeKind.Unspecified), Damascus));
        Assert.Equal(dateTime, HrLocalTime.FormatDateTime(new DateTimeOffset(stored), Damascus));
    }

    [Fact]
    public void A_calendar_date_is_formatted_never_converted() =>
        Assert.Equal("2026/12/31", HrLocalTime.FormatDate(new DateOnly(2026, 12, 31)));

    [Fact]
    public void An_effective_time_entered_at_half_past_midnight_in_Damascus_is_stored_the_evening_before_in_utc_and_reads_back_as_typed()
    {
        Assert.True(HrLocalTime.TryParseUserTime("2026-12-01T00:30", Damascus, out var stored));
        Assert.Equal(Utc(2026, 11, 30, 21, 30), stored);
        Assert.Equal(DateTimeKind.Utc, stored.Kind);
        Assert.Equal("2026/12/01 00:30", HrLocalTime.FormatDateTime(stored, Damascus));
        Assert.Equal("2026-12-01T00:30", HrLocalTime.ToLocal(stored, Damascus).ToString(HrLocalTime.DateTimeLocalInputFormat, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("2026-12-01T00:00:00Z", 2026, 12, 1, 0, 0)]
    [InlineData("2026-12-01T03:00:00+03:00", 2026, 12, 1, 0, 0)]
    public void A_time_with_an_offset_means_what_it_says(string text, int y, int mo, int d, int h, int mi)
    {
        Assert.True(HrLocalTime.TryParseUserTime(text, Damascus, out var stored));
        Assert.Equal(Utc(y, mo, d, h, mi), stored);
    }

    [Theory]
    [InlineData("2026-12-01")]
    [InlineData("2026-12-01T00:00:00Z")]
    [InlineData("not a time")]
    [InlineData("")]
    public void Only_a_datetime_local_value_is_a_local_input(string text) =>
        Assert.False(HrLocalTime.TryParseLocalInput(text, out _));

    [Fact]
    public void The_configured_zone_wins_and_an_unknown_one_stops_the_service()
    {
        var utc = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [HrLocalTime.TimeZoneKey] = "UTC" }).Build();
        try
        {
            Assert.Equal(TimeSpan.Zero, new DamascusCalendarService(utc).TimeZone.BaseUtcOffset);
            var wrong = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [HrLocalTime.TimeZoneKey] = "Mars/Olympus" }).Build();
            Assert.Throws<InvalidOperationException>(() => new DamascusCalendarService(wrong));
        }
        finally
        {
            HrLocalTime.UseZone(new DamascusCalendarService().TimeZone);
        }
    }

    // ------------------------------------------------------------------ the model binder

    private static async Task<ModelBindingResult> BindAsync(string value, bool apiController)
    {
        var services = new ServiceCollection().AddSingleton<ICalendarService>(new DamascusCalendarService()).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        var descriptor = new ActionDescriptor { EndpointMetadata = apiController ? [new ApiControllerAttribute()] : [] };
        var metadata = new EmptyModelMetadataProvider().GetMetadataForType(typeof(DateTime?));
        var context = new DefaultModelBindingContext
        {
            ActionContext = new ActionContext(http, new RouteData(), descriptor),
            ModelName = "at",
            ModelMetadata = metadata,
            ModelState = new ModelStateDictionary(),
            ValueProvider = new QueryStringValueProvider(BindingSource.Query, new QueryCollection(new Dictionary<string, StringValues> { ["at"] = value }), CultureInfo.InvariantCulture),
        };
        var binder = new HrLocalDateTimeModelBinder(new Microsoft.AspNetCore.Mvc.ModelBinding.Binders.SimpleTypeModelBinder(typeof(DateTime?), NullLoggerFactory.Instance));
        await binder.BindModelAsync(context);
        return context.Result;
    }

    [Fact]
    public async Task A_page_reads_a_datetime_local_value_as_damascus_time_and_binds_utc()
    {
        var result = await BindAsync("2026-11-13T11:06", apiController: false);
        Assert.True(result.IsModelSet);
        Assert.Equal(Utc(2026, 11, 13, 8, 6), (DateTime)result.Model!);
        Assert.Equal(DateTimeKind.Utc, ((DateTime)result.Model!).Kind);
    }

    [Fact]
    public async Task An_api_controller_and_a_date_alone_bind_as_before()
    {
        var api = await BindAsync("2026-11-13T11:06", apiController: true);
        Assert.Equal(new DateTime(2026, 11, 13, 11, 6, 0), (DateTime)api.Model!);
        var date = await BindAsync("2026-11-13", apiController: false);
        Assert.Equal(new DateTime(2026, 11, 13), (DateTime)date.Model!);
    }

    // Review finding 1: a calendar date entered through a datetime-local field (a payroll period) must not move back a day.
    public sealed class DatedForm
    {
        [System.ComponentModel.DataAnnotations.DataType(System.ComponentModel.DataAnnotations.DataType.Date)]
        public DateTime PeriodStart { get; set; }

        public DateTime ScheduledAt { get; set; }
    }

    private sealed class ProviderContext(ModelMetadata metadata, IServiceProvider services) : ModelBinderProviderContext
    {
        public override BindingInfo BindingInfo { get; } = new();

        public override ModelMetadata Metadata => metadata;

        public override IModelMetadataProvider MetadataProvider => throw new NotSupportedException();

        public override IServiceProvider Services => services;

        public override IModelBinder CreateBinder(ModelMetadata metadata) => throw new NotSupportedException();
    }

    [Fact]
    public void A_property_marked_as_a_date_is_bound_as_posted_and_a_time_is_read_on_the_damascus_clock()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddMvcCore().AddDataAnnotations();
        using var provider = services.BuildServiceProvider();
        var metadata = provider.GetRequiredService<IModelMetadataProvider>();
        var binderProvider = new HrLocalDateTimeModelBinderProvider();

        Assert.Null(binderProvider.GetBinder(new ProviderContext(metadata.GetMetadataForProperty(typeof(DatedForm), nameof(DatedForm.PeriodStart)), provider)));
        Assert.IsType<HrLocalDateTimeModelBinder>(binderProvider.GetBinder(new ProviderContext(metadata.GetMetadataForProperty(typeof(DatedForm), nameof(DatedForm.ScheduledAt)), provider)));
    }

    [Fact]
    public void Every_payroll_period_and_effective_date_is_a_date()
    {
        foreach (var (type, name) in new[]
                 {
                     (typeof(Payroll.Domain.Entities.AllowanceRecord), "EffectiveFrom"),
                     (typeof(Payroll.Domain.Entities.AllowanceRecord), "EffectiveTo"),
                     (typeof(Payroll.Domain.Entities.EmployeePayProfile), "EffectiveFrom"),
                     (typeof(HR.Health.Domain.Entities.HealthStatusEntry), "EntryDate"),
                 })
        {
            var attribute = type.GetProperty(name)!.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.DataTypeAttribute), true)
                .Cast<System.ComponentModel.DataAnnotations.DataTypeAttribute>().SingleOrDefault();
            Assert.True(attribute?.DataType == System.ComponentModel.DataAnnotations.DataType.Date, $"{type.Name}.{name} is not marked as a date");
        }
    }

    [Fact]
    public void A_time_in_an_hour_the_clocks_skipped_is_read_after_the_change_and_never_fails_the_request()
    {
        var zone = HrLocalTime.DefaultZone;
        var gap = Enumerable.Range(0, 366 * 24).Select(h => new DateTime(2021, 1, 1, 0, 30, 0).AddHours(h)).FirstOrDefault(zone.IsInvalidTime);
        Assert.NotEqual(default, gap);
        Assert.Equal(HrLocalTime.ToUtc(gap.AddHours(1), zone), HrLocalTime.ToUtc(gap, zone));
        Assert.True(HrLocalTime.TryParseUserTime(gap.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture), zone, out _));
    }

    // ------------------------------------------------------------------ vocabulary

    [Fact]
    public void Every_policy_domain_has_an_arabic_name_and_so_has_its_owner()
    {
        foreach (var d in PolicyDomainCatalog.All)
        {
            Assert.NotEqual(d.Domain, d.NameAr);
            Assert.Matches(@"\p{IsArabic}", d.NameAr);
            Assert.Matches(@"\p{IsArabic}", d.OwnerServiceAr);
        }
    }

    [Fact]
    public void Every_operator_and_effect_has_an_arabic_word()
    {
        Assert.All(PolicyRuleJson.Operators, op => Assert.True(PolicyVocabularyAr.Operators.ContainsKey(op), op));
        Assert.All(Enum.GetValues<PolicyEffect>(), e => Assert.True(PolicyVocabularyAr.Effects.ContainsKey(e), e.ToString()));
    }

    [Fact]
    public void The_policy_enums_print_in_arabic()
    {
        foreach (var value in Enum.GetValues<PolicyTemplateScope>().Cast<Enum>()
                     .Concat(Enum.GetValues<PolicyVersionStatus>().Cast<Enum>())
                     .Concat(Enum.GetValues<PolicyPublicationKind>().Cast<Enum>())
                     .Concat(Enum.GetValues<PolicyVersionEditAction>().Cast<Enum>())
                     .Concat(Enum.GetValues<PolicyEnforcementMode>().Cast<Enum>()))
            Assert.Matches(@"\p{IsArabic}", EnumDisplayExtensions.GetDisplayName(value));
        Assert.Equal("منشور", EnumDisplayExtensions.GetDisplayName(PolicyVersionStatus.Published));
        Assert.Equal("بذرة النظام", EnumDisplayExtensions.GetDisplayName(PolicyPublicationKind.SystemSeed));
    }

    [Theory]
    [InlineData("RecruitmentRequisition", "طلب شاغر")]
    [InlineData("EstablishmentChangeRequest", "طلب تغيير ملاك")]
    [InlineData("SomethingNew", "SomethingNew")]
    public void A_stored_record_type_prints_by_its_arabic_name(string stored, string shown) =>
        Assert.Equal(shown, EntityTypeNamesAr.Get(stored));

    [Theory]
    [InlineData("planned", "مخطط لها")]
    [InlineData("draft", "مسودة")]
    [InlineData("Permanent", "دائم")]
    public void A_stored_lowercase_status_prints_in_arabic(string stored, string shown) =>
        Assert.Equal(shown, EnumDisplayExtensions.GetStoredEnumDisplayName("NoSuchEnum", stored));

    [Fact]
    public async Task An_unknown_zone_stops_the_service_at_start_up_and_leaves_the_zone_as_it_was()
    {
        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder();
        builder.Configuration[HrLocalTime.TimeZoneKey] = "Mars/Olympus_Mons";
        builder.Services.AddHrLocalization();
        await using var app = builder.Build();
        var before = HrLocalTime.Zone;

        var error = Assert.Throws<InvalidOperationException>(() => app.UseHrLocalization());
        Assert.Contains(HrLocalTime.TimeZoneKey, error.Message, StringComparison.Ordinal);
        Assert.Same(before, HrLocalTime.Zone);
    }

    [Theory]
    [InlineData("تقديم تغيير ملاك → workflow", "R-EST-SUBMIT", "تقديم تغيير ملاك — يُطلق مسار الاعتماد")]
    [InlineData("رفض تغيير من نطاق فرع", "R-EST-DENY-BRANCH", "رفض تغيير من نطاق فرع")]
    [InlineData(null, "R-X", "R-X")]
    public void A_seeded_rule_name_reads_in_arabic_without_rewriting_the_version(string? stored, string key, string shown) =>
        Assert.Equal(shown, PolicyVocabularyAr.RuleName(stored, key));
}
