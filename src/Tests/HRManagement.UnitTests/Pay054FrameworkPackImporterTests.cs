using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Features.PayrollConfigurations;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using Payroll.Infrastructure.Data;
using H = HRManagement.UnitTests.Pay054FrameworkHarness;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-54 (§1, P-5): the legal pack and its importer. A valid pack becomes a v2 draft with every value classified; every broken rule
/// is refused with the field and the row named in Arabic, without echoing the file, and leaves no row; decisions are inserted or
/// updated by their id; an existing draft's content is replaced; the documented schema and the importer say the same; and the interim
/// pack holds exactly the values the seeds hold today.
/// </summary>
public sealed class Pay054FrameworkPackImporterTests
{
    private const string Marker = "ZZ-ECHO-MARKER";

    // ------------------------------------------------------------------ a valid pack

    [Fact]
    public async Task The_interim_pack_becomes_a_v2_draft_with_every_table_every_reference_and_its_editor()
    {
        using var h = new H();

        var result = await h.ImportAsync();

        Assert.True(result.Success, result.Error + " " + string.Join(" | ", result.Errors));
        Assert.Equal(PayrollConfigPublicationStatus.Draft, result.Status);
        Assert.Equal(1, result.VersionNumber);
        var draft = await h.VersionAsync(result.FrameworkId!.Value);
        Assert.Equal("LAW_50_2004", draft.Code);
        Assert.Equal("LAW_50_2004", draft.RegimeCode);
        Assert.Equal(PayrollConfiguration.CurrentHashSchemaVersion, draft.HashSchemaVersion);
        Assert.Equal(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc), draft.EffectiveFrom);
        Assert.Null(draft.EffectiveTo);
        Assert.Null(draft.ContentHash);
        Assert.False(draft.IsActive);
        Assert.Null(draft.ApprovedBy);
        Assert.Equal(5, draft.TaxBrackets.Count);
        Assert.Equal(4, draft.StatutoryDeductions.Count);
        Assert.Equal(4, draft.Holidays.Count);
        Assert.Equal(3, draft.PriorityRules.Count);
        Assert.Equal(PayrollConfigurationFieldSource.GovernedFields.Count, draft.FieldSources.Count);
        Assert.All(draft.PriorityRules, r =>
        {
            Assert.Equal(draft.Id, r.PayrollConfigurationId);
            Assert.Equal(draft.Id, r.RuleSetId);
            Assert.True(r.IsActive);
        });
        Assert.All(draft.LegalValues(), v =>
        {
            Assert.False(string.IsNullOrWhiteSpace(v.LegalReference));
            Assert.Equal(PayrollLegalValueStatus.Interim, v.LegalStatus);
            Assert.Contains(v.InterimExceptionId, new[] { "EX-STR2-TAX-INTERIM", "EX-STR2-LAW50-UNCERTIFIED" });
        });
        var editor = Assert.Single(draft.Editors);
        Assert.Equal(H.Editor, editor.UserId);
        Assert.Equal(PayrollFrameworkEditKind.Imported, editor.Kind);
        Assert.Equal(h.Clock.Now.UtcDateTime, editor.At);

        await using var db = h.Db();
        Assert.Equal(17, await db.LegalDecisionRecords.CountAsync());
        Assert.Equal(0, await db.OfficialHolidays.CountAsync(x => x.PayrollConfigurationId == null));
        Assert.Equal(0, await db.DisbursementPriorityRules.CountAsync(x => x.PayrollConfigurationId == null));
    }

    [Fact]
    public async Task The_code_and_the_date_given_with_the_import_take_the_place_of_the_packs_own()
    {
        using var h = new H();

        var result = await h.ImportAsync(code: "LAW_50_2004_AC54", effectiveFrom: new DateOnly(2031, 1, 1));

        Assert.True(result.Success, result.Error);
        var draft = await h.VersionAsync(result.FrameworkId!.Value);
        Assert.Equal("LAW_50_2004_AC54", draft.Code);
        Assert.Equal("LAW_50_2004", draft.RegimeCode);
        Assert.Equal(new DateTime(2031, 1, 1, 0, 0, 0, DateTimeKind.Utc), draft.EffectiveFrom);
    }

    [Fact]
    public async Task A_second_import_of_a_code_is_the_next_version_and_decisions_are_updated_by_their_id_not_duplicated()
    {
        using var h = new H();
        var first = await h.ImportAsync();
        var changed = H.PackWith(p => p["decisions"]![0]!["officialSource"] = "المرسوم التشريعي 24 لعام 2003");

        var second = await h.ImportAsync(changed, effectiveFrom: new DateOnly(2027, 1, 1), actor: H.OtherEditor);

        Assert.True(second.Success, second.Error);
        Assert.Equal(2, second.VersionNumber);
        Assert.NotEqual(first.FrameworkId, second.FrameworkId);
        await using var db = h.Db();
        Assert.Equal(17, await db.LegalDecisionRecords.CountAsync());
        Assert.Equal("المرسوم التشريعي 24 لعام 2003", (await db.LegalDecisionRecords.SingleAsync(d => d.DecisionId == "LD-TAX-BRACKETS")).OfficialSource);
        // The first draft is untouched by the second import.
        Assert.Equal(5, (await h.VersionAsync(first.FrameworkId!.Value)).TaxBrackets.Count);
    }

    [Fact]
    public async Task An_import_into_a_draft_replaces_its_content_keeps_its_number_and_leaves_no_general_row_behind()
    {
        using var h = new H();
        var first = await h.ImportAsync();
        var edited = H.PackWith(p =>
        {
            p["statutoryDeductions"]!.AsArray().RemoveAt(3);
            p["statutoryDeductions"]![1]!["employeePercent"] = 8;
            p["holidays"]!.AsArray().RemoveAt(0);
            p["priorityRules"]!.AsArray().RemoveAt(2);
            p["fields"]!["taxExemptionAmount"]!["value"] = 2500;
        });

        var second = await h.ImportAsync(edited, targetDraftId: first.FrameworkId, actor: H.OtherEditor);

        Assert.True(second.Success, second.Error);
        Assert.Equal(first.FrameworkId, second.FrameworkId);
        Assert.Equal(1, second.VersionNumber);
        var draft = await h.VersionAsync(first.FrameworkId!.Value);
        Assert.Equal(3, draft.StatutoryDeductions.Count);
        Assert.Equal(8m, draft.StatutoryDeductions.Single(d => d.Code == "DED-SOC").EmployeePercent);
        Assert.Equal(3, draft.Holidays.Count);
        Assert.Equal(2, draft.PriorityRules.Count);
        Assert.Equal(2500m, draft.TaxExemptionAmount);
        Assert.Equal(5, draft.TaxBrackets.Count);
        Assert.Equal(PayrollConfigurationFieldSource.GovernedFields.Count, draft.FieldSources.Count);
        Assert.Equal([H.Editor, H.OtherEditor], draft.Editors.OrderBy(e => e.At).ThenBy(e => e.UserId).Select(e => e.UserId));

        var counts = await h.RowCountsAsync();
        Assert.Equal(1, counts["PayrollConfigurations"]);
        Assert.Equal(5, counts["TaxBrackets"]);
        Assert.Equal(3, counts["StatutoryDeductionRules"]);
        Assert.Equal(3, counts["OfficialHolidays"]);
        Assert.Equal(2, counts["DisbursementPriorityRules"]);
        Assert.Equal(PayrollConfigurationFieldSource.GovernedFields.Count, counts["PayrollConfigurationFieldSources"]);
    }

    [Fact]
    public async Task An_import_into_a_version_that_is_not_a_draft_into_another_code_or_into_nothing_is_refused_and_writes_nothing()
    {
        using var h = new H();
        var first = await h.ImportAsync();
        await using (var db = h.Db())
        {
            var version = await db.PayrollConfigurations.SingleAsync();
            version.PublicationStatus = PayrollConfigPublicationStatus.Approved;
            await db.SaveChangesAsync();
        }

        var before = await h.RowCountsAsync();

        var notDraft = await h.ImportAsync(targetDraftId: first.FrameworkId);
        Assert.Equal(PayrollFrameworkFailure.Conflict, notDraft.Failure);
        Assert.Equal(409, notDraft.StatusCode);
        Assert.Equal(PayrollConfigPublicationStatus.Approved, notDraft.Status);

        var missing = await h.ImportAsync(targetDraftId: Guid.NewGuid());
        Assert.Equal(PayrollFrameworkFailure.NotFound, missing.Failure);

        await using (var db = h.Db())
        {
            var version = await db.PayrollConfigurations.SingleAsync();
            version.PublicationStatus = PayrollConfigPublicationStatus.Draft;
            await db.SaveChangesAsync();
        }

        var otherCode = await h.ImportAsync(code: "LAW_50_2004_B", targetDraftId: first.FrameworkId);
        Assert.Equal(PayrollFrameworkFailure.Invalid, otherCode.Failure);
        Assert.Equal(PayrollFrameworkImportMessages.TargetOfAnotherCode, otherCode.Error);

        await h.AssertNothingWrittenAsync(before);
    }

    [Fact]
    public async Task A_code_keeps_one_regime()
    {
        using var h = new H();
        await h.ImportAsync();
        var before = await h.RowCountsAsync();

        var result = await h.ImportAsync(H.PackWith(p => p["regimeCode"] = "LAW_53_2021"));

        Assert.False(result.Success);
        Assert.Equal(PayrollFrameworkImportMessages.CodeOfAnotherRegime, result.Error);
        await h.AssertNothingWrittenAsync(before);
    }

    // ------------------------------------------------------------------ every refusal

    public static TheoryData<string, string, Action<JsonObject>> Defects() => new()
    {
        { "schema", "$.schema", p => p["schema"] = Marker },
        { "code in lower case", "$.code", p => p["code"] = "law_" + Marker },
        { "code missing", "$.code", p => p.Remove("code") },
        { "unknown regime", "$.regimeCode", p => p["regimeCode"] = Marker },
        { "empty name", "$.nameAr", p => p["nameAr"] = " " },
        { "no effective date", "$.effectiveFrom", p => p.Remove("effectiveFrom") },
        { "a date that is not a date", "$.effectiveFrom", p => p["effectiveFrom"] = "2031-13-45" },
        { "fields missing", "$.fields", p => p.Remove("fields") },
        { "a governed field missing", "$.fields.taxExemptionAmount", p => p["fields"]!.AsObject().Remove("taxExemptionAmount") },
        { "a field of the wrong type", "$.fields.dailyRateDivisor.value", p => p["fields"]!["dailyRateDivisor"]!["value"] = Marker },
        { "a field out of range", "$.fields.decimalPlaces.value", p => p["fields"]!["decimalPlaces"]!["value"] = 9 },
        { "an enum field with an unknown value", "$.fields.roundingMode.value", p => p["fields"]!["roundingMode"]!["value"] = Marker },
        { "non-working days that are not days", "$.fields.nonWorkingDays.value", p => p["fields"]!["nonWorkingDays"]!["value"] = "Friday," + Marker },
        { "a currency that is not three letters", "$.fields.currencyCode.value", p => p["fields"]!["currencyCode"]!["value"] = "syp" },
        { "an amount with three decimals", "$.fields.taxExemptionAmount.value", p => p["fields"]!["taxExemptionAmount"]!["value"] = 10.125 },
        { "a field the contract does not know", "$.fields", p => p["fields"]![Marker] = p["fields"]!["decimalPlaces"]!.DeepClone() },
        { "a field without a reference", "$.fields.currencyCode.legalReference", p => p["fields"]!["currencyCode"]!.AsObject().Remove("legalReference") },
        { "no brackets", "$.taxBrackets", p => p["taxBrackets"] = new JsonArray() },
        { "a bracket of 120%", "$.taxBrackets[2].ratePercent", p => p["taxBrackets"]![2]!["ratePercent"] = 120 },
        { "a negative rate", "$.taxBrackets[1].ratePercent", p => p["taxBrackets"]![1]!["ratePercent"] = -1 },
        { "brackets that do not ascend", "$.taxBrackets[2].upperLimit", p => p["taxBrackets"]![2]!["upperLimit"] = 15000 },
        { "a last bracket with a ceiling", "$.taxBrackets[4].upperLimit", p => p["taxBrackets"]![4]!["upperLimit"] = 90000 },
        { "a bracket without a limit", "$.taxBrackets[0].upperLimit", p => p["taxBrackets"]![0]!.AsObject().Remove("upperLimit") },
        { "a bracket with an unknown status", "$.taxBrackets[0].legalStatus", p => p["taxBrackets"]![0]!["legalStatus"] = Marker },
        { "an interim value without an exception", "$.taxBrackets[0].interimExceptionId", p => p["taxBrackets"]![0]!.AsObject().Remove("interimExceptionId") },
        { "a signed value with an exception", "$.taxBrackets[0].interimExceptionId", p => p["taxBrackets"]![0]!["legalStatus"] = "Signed" },
        { "a decision reference of a wrong form", "$.taxBrackets[0].legalReference", p => p["taxBrackets"]![0]!["legalReference"] = "LD-" + Marker.ToLowerInvariant() },
        { "statutory deductions missing", "$.statutoryDeductions", p => p.Remove("statutoryDeductions") },
        { "a duplicate deduction code", "$.statutoryDeductions[1].code", p => p["statutoryDeductions"]![1]!["code"] = "DED-TAX" },
        { "an employee share over 100", "$.statutoryDeductions[0].employeePercent", p => p["statutoryDeductions"]![0]!["employeePercent"] = 101 },
        { "an employer share below 0", "$.statutoryDeductions[1].employerPercent", p => p["statutoryDeductions"]![1]!["employerPercent"] = -3 },
        { "a negative fixed amount", "$.statutoryDeductions[0].fixedAmount", p => p["statutoryDeductions"]![0]!["fixedAmount"] = -1 },
        { "an unknown base", "$.statutoryDeductions[0].baseKind", p => p["statutoryDeductions"]![0]!["baseKind"] = Marker },
        { "a ceiling of zero", "$.statutoryDeductions[0].ceiling", p => p["statutoryDeductions"]![0]!["ceiling"] = 0 },
        { "an unknown insurance body", "$.statutoryDeductions[1].insuranceBody", p => p["statutoryDeductions"]![1]!["insuranceBody"] = Marker },
        { "a deduction without a name", "$.statutoryDeductions[2].nameAr", p => p["statutoryDeductions"]![2]!.AsObject().Remove("nameAr") },
        { "a holiday on no date", "$.holidays[1].date", p => p["holidays"]![1]!["date"] = Marker },
        { "a holiday twice", "$.holidays[1].date", p => p["holidays"]![1]!["date"] = "2000-01-01" },
        { "a holiday that does not say whether it recurs", "$.holidays[0].isRecurring", p => p["holidays"]![0]!.AsObject().Remove("isRecurring") },
        { "a priority of zero", "$.priorityRules[0].priority", p => p["priorityRules"]![0]!["priority"] = 0 },
        { "a category twice", "$.priorityRules[1].category", p => p["priorityRules"]![1]!["category"] = "LowIncome" },
        { "a negative minimum", "$.priorityRules[2].minimumAmount", p => p["priorityRules"]![2]!["minimumAmount"] = -5 },
        { "a rule without a reference", "$.priorityRules[0].legalReference", p => p["priorityRules"]![0]!["legalReference"] = "" },
        { "a decision id of a wrong form", "$.decisions[0].decisionId", p => p["decisions"]![0]!["decisionId"] = Marker },
        { "a decision twice", "$.decisions[1].decisionId", p => p["decisions"]![1]!["decisionId"] = "LD-TAX-BRACKETS" },
        { "a decision with an unknown status", "$.decisions[2].approvalStatus", p => p["decisions"]![2]!["approvalStatus"] = Marker },
        { "a decision without a source", "$.decisions[3].officialSource", p => p["decisions"]![3]!.AsObject().Remove("officialSource") },
        { "a decision without a date", "$.decisions[4].effectiveFrom", p => p["decisions"]![4]!["effectiveFrom"] = Marker },
        { "a cited decision that is nowhere", "$.fields.dailyRateDivisor.legalReference", p => p["fields"]!["dailyRateDivisor"]!["legalReference"] = "LD-NOWHERE-54" },
    };

    [Theory]
    [MemberData(nameof(Defects))]
    public async Task A_pack_that_breaks_a_rule_is_refused_with_the_field_named_without_echoing_the_file_and_leaves_no_row(
        string what, string path, Action<JsonObject> plant)
    {
        using var h = new H();
        var before = await h.RowCountsAsync();

        var result = await h.ImportAsync(H.PackWith(plant));

        Assert.False(result.Success, $"accepted: {what}");
        Assert.Equal(PayrollFrameworkFailure.Invalid, result.Failure);
        Assert.Equal(400, result.StatusCode);
        Assert.Null(result.FrameworkId);
        Assert.True(result.Errors.Any(e => e.Path == path), $"{what}: no error names {path}; got {string.Join(" | ", result.Errors)}");
        Assert.All(result.Errors, e =>
        {
            Assert.DoesNotContain(Marker, e.Path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Marker, e.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Matches(@"\p{IsArabic}", e.Message);
        });
        Assert.DoesNotContain(Marker, result.Error!, StringComparison.OrdinalIgnoreCase);
        await h.AssertNothingWrittenAsync(before);
    }

    [Fact]
    public async Task A_cited_decision_that_the_register_already_holds_need_not_be_in_the_pack()
    {
        using var h = new H();
        await h.ImportAsync();

        var result = await h.ImportAsync(H.PackWith(p => p["decisions"] = new JsonArray()), effectiveFrom: new DateOnly(2027, 1, 1));

        Assert.True(result.Success, result.Error + string.Join(" | ", result.Errors));
    }

    public static TheoryData<string, string> Unreadable() => new()
    {
        { "empty", "" },
        { "not JSON", "<pack>" + Marker + "</pack>" },
        { "an array", "[1,2,3]" },
        { "a property the contract does not know", H.PackWith(p => p[Marker] = "x") },
        { "a row property the contract does not know", H.PackWith(p => p["taxBrackets"]![0]![Marker] = 1) },
        { "a number written as text", H.PackWith(p => p["taxBrackets"]![0]!["ratePercent"] = "5") },
        { "deeper than the limit", H.PackWith(p => p["fields"]!["currencyCode"]!["value"] = JsonNode.Parse(new string('[', 40) + new string(']', 40))) },
        { "larger than the limit", H.PackWith(p => p["nameAr"] = new string('ا', PayrollFrameworkPackReader.MaxBytes)) },
    };

    [Theory]
    [MemberData(nameof(Unreadable))]
    public async Task A_file_that_is_not_a_pack_is_refused_without_echoing_it_and_leaves_no_row(string what, string content)
    {
        using var h = new H();
        var before = await h.RowCountsAsync();

        var result = await h.ImportAsync(content);

        Assert.False(result.Success, $"accepted: {what}");
        Assert.Equal(400, result.StatusCode);
        var error = Assert.Single(result.Errors);
        Assert.StartsWith("$", error.Path, StringComparison.Ordinal);
        Assert.DoesNotContain(Marker, error.Path, StringComparison.Ordinal);
        Assert.DoesNotContain(Marker, error.Message, StringComparison.Ordinal);
        await h.AssertNothingWrittenAsync(before);
    }

    [Fact]
    public void A_json_path_is_reported_only_as_far_as_the_contract_knows_its_names()
    {
        Assert.Equal("$.taxBrackets[2].ratePercent", PackContract.SafePath("$.taxBrackets[2].ratePercent"));
        Assert.Equal("$.fields.currencyCode.value", PackContract.SafePath("$.fields.currencyCode.value"));
        Assert.Equal("$", PackContract.SafePath("$." + Marker));
        Assert.Equal("$.taxBrackets[0]", PackContract.SafePath("$.taxBrackets[0]." + Marker + ".ratePercent"));
        Assert.Equal("$.fields", PackContract.SafePath("$.fields['" + Marker + "']"));
        Assert.Equal("$", PackContract.SafePath(null));
        Assert.Equal("$", PackContract.SafePath(Marker));
    }

    // ------------------------------------------------------------------ the export

    [Fact]
    public async Task An_export_is_a_pack_that_imports_into_the_same_draft_with_the_same_fingerprint()
    {
        using var h = new H();
        var imported = await h.ImportAsync(code: "LAW_50_2004_X", effectiveFrom: new DateOnly(2031, 1, 1));
        var before = PayrollConfigurationHashService.ComputeContentHash(await h.VersionAsync(imported.FrameworkId!.Value));

        var export = await h.ExportAsync(imported.FrameworkId.Value);

        Assert.NotNull(export);
        Assert.Equal("LAW_50_2004_X-v1.json", export!.FileName);
        var (pack, error) = PayrollFrameworkPackReader.Read(export.Json);
        Assert.Null(error);
        Assert.Empty(PayrollFrameworkPackValidator.Validate(pack!));
        // The decisions an export carries are the ones the version's values cite.
        Assert.Equal(15, pack!.Decisions!.Count);
        Assert.DoesNotContain(pack.Decisions, d => d.DecisionId == "LD-CIVIL-SERVICE-PENDING");

        var again = await h.ImportAsync(export.Json, targetDraftId: imported.FrameworkId);
        Assert.True(again.Success, again.Error + string.Join(" | ", again.Errors));
        Assert.Equal(before, PayrollConfigurationHashService.ComputeContentHash(await h.VersionAsync(imported.FrameworkId.Value)));
        Assert.Null(await h.ExportAsync(Guid.NewGuid()));
    }

    // ------------------------------------------------------------------ create

    [Fact]
    public async Task A_draft_from_a_regimes_template_is_v2_unclassified_with_the_date_given_and_its_creator()
    {
        using var h = new H();

        var result = await h.CreateAsync(regimeCode: "LAW_50_2004", code: "LAW_50_2004_NEW", effectiveFrom: new DateOnly(2027, 3, 1));

        Assert.True(result.Success, result.Error);
        var draft = await h.VersionAsync(result.FrameworkId!.Value);
        Assert.Equal(PayrollConfigPublicationStatus.Draft, draft.PublicationStatus);
        Assert.Equal(PayrollConfiguration.CurrentHashSchemaVersion, draft.HashSchemaVersion);
        Assert.Equal("LAW_50_2004_NEW", draft.Code);
        Assert.Equal("LAW_50_2004", draft.RegimeCode);
        Assert.Equal(1, draft.VersionNumber);
        Assert.Equal(new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc), draft.EffectiveFrom);
        Assert.Null(draft.SupersedesConfigurationId);
        Assert.Equal(5, draft.TaxBrackets.Count);
        Assert.Equal(4, draft.Holidays.Count);
        Assert.Empty(draft.StatutoryDeductions);
        Assert.Empty(draft.PriorityRules);
        Assert.Equal(PayrollConfigurationFieldSource.GovernedFields.Count, draft.FieldSources.Count);
        Assert.All(draft.LegalValues(), v =>
        {
            Assert.Equal(PayrollLegalValueStatus.Interim, v.LegalStatus);
            Assert.Null(v.InterimExceptionId);
            Assert.Null(v.LegalReference);
        });
        var editor = Assert.Single(draft.Editors);
        Assert.Equal((H.Editor, PayrollFrameworkEditKind.Created), (editor.UserId, editor.Kind));
    }

    [Fact]
    public async Task A_clone_of_a_legacy_version_starts_from_the_general_tables_and_a_clone_of_a_v2_version_from_its_own()
    {
        using var h = new H();
        Guid legacyId;
        await using (var db = h.Db())
        {
            var v1 = PayrollRegimeSeed.CreateLaw50Configuration();
            foreach (var own in PayrollRegimeSeed.CreateDefaultHolidays(v1.Id)) v1.Holidays.Add(own);
            db.PayrollConfigurations.Add(v1);
            db.DeductionTypes.AddRange(
                new DeductionType { Code = "DED-SOC", NameAr = "تأمينات اجتماعية", IsStatutory = true, Percentage = 7m },
                new DeductionType { Code = "DED-LOAN", NameAr = "قسط سلفة", IsStatutory = false, FixedAmount = 25_000m });
            db.OfficialHolidays.Add(new OfficialHoliday { Date = new DateOnly(2000, 3, 21), NameAr = "عيد الأم", IsRecurring = true });
            db.DisbursementPriorityRules.Add(new DisbursementPriorityRule { RuleSetId = Guid.NewGuid(), Category = "General", Priority = 1, LegalSource = "تعميم", IsActive = true });
            await db.SaveChangesAsync();
            legacyId = v1.Id;
        }

        var fromLegacy = await h.CreateAsync(cloneFromId: legacyId, effectiveFrom: new DateOnly(2027, 1, 1));

        Assert.True(fromLegacy.Success, fromLegacy.Error);
        Assert.Equal(2, fromLegacy.VersionNumber);
        var draft = await h.VersionAsync(fromLegacy.FrameworkId!.Value);
        Assert.Equal(legacyId, draft.SupersedesConfigurationId);
        Assert.Equal(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), draft.EffectiveFrom);
        Assert.Equal("DED-SOC", Assert.Single(draft.StatutoryDeductions).Code);
        Assert.Equal(5, draft.Holidays.Count);
        Assert.Equal("General", Assert.Single(draft.PriorityRules).Category);
        Assert.All(draft.LegalValues(), v => Assert.Null(v.InterimExceptionId));
        // The general rows and the legacy version are as they were.
        await using (var db = h.Db())
        {
            Assert.Equal(1, await db.OfficialHolidays.CountAsync(x => x.PayrollConfigurationId == null));
            Assert.Equal(1, await db.DisbursementPriorityRules.CountAsync(x => x.PayrollConfigurationId == null));
            Assert.Equal(4, await db.OfficialHolidays.CountAsync(x => x.PayrollConfigurationId == legacyId));
            Assert.Equal(PayrollConfiguration.LegacyHashSchemaVersion, (await db.PayrollConfigurations.SingleAsync(c => c.Id == legacyId)).HashSchemaVersion);
        }

        var imported = await h.ImportAsync(code: "LAW_50_2004_V2", effectiveFrom: new DateOnly(2031, 1, 1));
        var fromV2 = await h.CreateAsync(cloneFromId: imported.FrameworkId, effectiveFrom: new DateOnly(2031, 2, 1), actor: H.OtherEditor);

        Assert.True(fromV2.Success, fromV2.Error);
        Assert.Equal(2, fromV2.VersionNumber);
        var clone = await h.VersionAsync(fromV2.FrameworkId!.Value);
        Assert.Equal(4, clone.StatutoryDeductions.Count);
        Assert.Equal(4, clone.Holidays.Count);
        Assert.Equal(3, clone.PriorityRules.Count);
        Assert.All(clone.LegalValues(), v => Assert.False(string.IsNullOrEmpty(v.InterimExceptionId)));
        Assert.Equal(H.OtherEditor, Assert.Single(clone.Editors).UserId);
    }

    [Fact]
    public async Task Create_needs_exactly_one_source_a_known_regime_a_valid_code_and_a_date()
    {
        using var h = new H();
        var before = await h.RowCountsAsync();

        Assert.Equal(PayrollFrameworkImportMessages.OneSource, (await h.CreateAsync(effectiveFrom: new DateOnly(2027, 1, 1))).Error);
        Assert.Equal(PayrollFrameworkImportMessages.OneSource,
            (await h.CreateAsync(regimeCode: "LAW_50_2004", cloneFromId: Guid.NewGuid(), effectiveFrom: new DateOnly(2027, 1, 1))).Error);
        Assert.Equal(PayrollFrameworkMessages.EffectiveFromRequired, (await h.CreateAsync(regimeCode: "LAW_50_2004")).Error);
        Assert.Equal(PayrollFrameworkImportMessages.UnknownRegime, (await h.CreateAsync(regimeCode: "LAW_99", effectiveFrom: new DateOnly(2027, 1, 1))).Error);
        Assert.Equal(PayrollFrameworkImportMessages.CodeInvalid,
            (await h.CreateAsync(regimeCode: "LAW_50_2004", code: "bad code", effectiveFrom: new DateOnly(2027, 1, 1))).Error);
        Assert.Equal(PayrollFrameworkImportMessages.SourceNotFound, (await h.CreateAsync(cloneFromId: Guid.NewGuid(), effectiveFrom: new DateOnly(2027, 1, 1))).Error);

        await h.AssertNothingWrittenAsync(before);
    }

    // ------------------------------------------------------------------ the effective date

    [Fact]
    public async Task The_effective_date_of_a_draft_changes_and_its_editor_is_recorded_and_no_other_status_takes_it()
    {
        using var h = new H();
        var imported = await h.ImportAsync();

        var set = await h.SetEffectiveFromAsync(imported.FrameworkId!.Value, new DateOnly(2031, 2, 1), H.OtherEditor);

        Assert.True(set.Success, set.Error);
        var draft = await h.VersionAsync(imported.FrameworkId.Value);
        Assert.Equal(new DateTime(2031, 2, 1, 0, 0, 0, DateTimeKind.Utc), draft.EffectiveFrom);
        Assert.Contains(draft.Editors, e => e.UserId == H.OtherEditor && e.Kind == PayrollFrameworkEditKind.Edited);
        Assert.Equal(2, draft.Editors.Count);

        Assert.Equal(PayrollFrameworkMessages.EffectiveFromRequired, (await h.SetEffectiveFromAsync(imported.FrameworkId.Value, null)).Error);
        Assert.Equal(PayrollFrameworkFailure.NotFound, (await h.SetEffectiveFromAsync(Guid.NewGuid(), new DateOnly(2031, 2, 1))).Failure);

        await using (var db = h.Db())
        {
            (await db.PayrollConfigurations.SingleAsync()).PublicationStatus = PayrollConfigPublicationStatus.Approved;
            await db.SaveChangesAsync();
        }

        var refused = await h.SetEffectiveFromAsync(imported.FrameworkId.Value, new DateOnly(2032, 1, 1));
        Assert.Equal(409, refused.StatusCode);
        Assert.Equal(PayrollFrameworkMessages.DraftOnly, refused.Error);
        Assert.Equal(new DateTime(2031, 2, 1, 0, 0, 0, DateTimeKind.Utc), (await h.VersionAsync(imported.FrameworkId.Value)).EffectiveFrom);
    }

    // ------------------------------------------------------------------ the schema and the seed

    private static List<string> Strings(JsonElement array) => array.EnumerateArray().Select(e => e.GetString()!).ToList();

    [Fact]
    public void The_documented_schema_and_the_importer_say_the_same()
    {
        using var schema = JsonDocument.Parse(System.IO.File.ReadAllText(H.SchemaPath));
        var root = schema.RootElement;
        var properties = root.GetProperty("properties");

        Assert.Equal(PayrollFrameworkPack.SchemaId, root.GetProperty("$id").GetString());
        Assert.Equal(PayrollFrameworkPack.SchemaId, properties.GetProperty("schema").GetProperty("const").GetString());
        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(PackContract.RequiredTopLevel, Strings(root.GetProperty("required")));
        Assert.Equal(PackContract.RequiredTopLevel.OrderBy(x => x, StringComparer.Ordinal), properties.EnumerateObject().Select(p => p.Name).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(PackContract.RegimeCodes, Strings(properties.GetProperty("regimeCode").GetProperty("enum")));

        var fields = properties.GetProperty("fields");
        Assert.Equal(PackContract.FieldNames, Strings(fields.GetProperty("required")));
        Assert.Equal(PackContract.FieldNames, fields.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToList());
        JsonElement FieldValue(string name) => fields.GetProperty("properties").GetProperty(name).GetProperty("properties").GetProperty("value");
        Assert.Equal(PackContract.ProrationBases, Strings(FieldValue("prorationDaysBasis").GetProperty("enum")));
        Assert.Equal(PackContract.TaxableBaseModes, Strings(FieldValue("taxableBaseMode").GetProperty("enum")));
        Assert.Equal(PackContract.RoundingModes, Strings(FieldValue("roundingMode").GetProperty("enum")));

        // Every sourced value: the three source fields, the reference and the status required, the status from the same list.
        var sourced = PackContract.FieldNames.Select(n => fields.GetProperty("properties").GetProperty(n))
            .Concat(new[] { "taxBrackets", "statutoryDeductions", "holidays", "priorityRules" }.Select(t => properties.GetProperty(t).GetProperty("items")));
        foreach (var value in sourced)
        {
            Assert.False(value.GetProperty("additionalProperties").GetBoolean());
            Assert.Contains("legalReference", Strings(value.GetProperty("required")));
            Assert.Contains("legalStatus", Strings(value.GetProperty("required")));
            Assert.Equal(PackContract.LegalStatuses, Strings(value.GetProperty("properties").GetProperty("legalStatus").GetProperty("enum")));
            Assert.True(value.GetProperty("properties").TryGetProperty("interimExceptionId", out _));
        }

        var deduction = properties.GetProperty("statutoryDeductions").GetProperty("items").GetProperty("properties");
        Assert.Equal(PackContract.BaseKinds, Strings(deduction.GetProperty("baseKind").GetProperty("enum")));
        Assert.Equal(PackContract.InsuranceBodies, Strings(deduction.GetProperty("insuranceBody").GetProperty("enum")));
        Assert.Equal(PackContract.DecisionStatuses,
            Strings(properties.GetProperty("decisions").GetProperty("items").GetProperty("properties").GetProperty("approvalStatus").GetProperty("enum")));
        Assert.Equal(PackContract.OpenEndedUpperLimit,
            properties.GetProperty("taxBrackets").GetProperty("items").GetProperty("properties").GetProperty("upperLimit").GetProperty("maximum").GetDecimal());

        // Every row property the schema names is a property the reader maps, and the other way round.
        void SameProperties(string table, Type type)
        {
            var inSchema = properties.GetProperty(table).GetProperty("items").GetProperty("properties").EnumerateObject().Select(p => p.Name).OrderBy(x => x, StringComparer.Ordinal);
            var inReader = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => PackContract.Camel(p.Name)).OrderBy(x => x, StringComparer.Ordinal);
            Assert.Equal(inReader, inSchema);
        }

        SameProperties("taxBrackets", typeof(PackTaxBracket));
        SameProperties("statutoryDeductions", typeof(PackStatutoryDeduction));
        SameProperties("holidays", typeof(PackHoliday));
        SameProperties("priorityRules", typeof(PackPriorityRule));
        SameProperties("decisions", typeof(PackDecision));
    }

    [Fact]
    public void The_interim_pack_is_valid_and_holds_exactly_the_values_the_seeds_hold_today()
    {
        var (pack, error) = PayrollFrameworkPackReader.Read(H.InterimPackJson());
        Assert.Null(error);
        Assert.Empty(PayrollFrameworkPackValidator.Validate(pack!));

        // The fields and the brackets of the seeded LAW_50_2004.
        var seed = PayrollRegimeSeed.CreateLaw50Configuration();
        var fromPack = new PayrollConfiguration { Code = pack!.Code!, HashSchemaVersion = PayrollConfiguration.CurrentHashSchemaVersion };
        PayrollFrameworkPackMapper.ApplyContent(pack, fromPack);
        Assert.Equal(seed.Code, pack.Code);
        Assert.Equal(seed.RegimeCode, fromPack.RegimeCode);
        Assert.Equal(seed.NameAr, fromPack.NameAr);
        Assert.Equal("2000-01-01", pack.EffectiveFrom);
        foreach (var field in PayrollConfigurationFieldSource.GovernedFields)
        {
            var property = typeof(PayrollConfiguration).GetProperty(field)!;
            Assert.True(Equals(property.GetValue(seed), property.GetValue(fromPack)), $"{field}: the pack differs from the seed");
        }

        Assert.Equal(
            PayrollRegimeSeed.Law50TaxBrackets.Select(b => (b.UpperLimit, b.Rate * 100m)),
            fromPack.TaxBrackets.OrderBy(b => b.SortOrder).Select(b => (b.UpperLimit, b.RatePercent)));
        Assert.Equal(seed.TaxBrackets.OrderBy(b => b.SortOrder).Select(b => (b.SortOrder, b.UpperLimit, b.RatePercent)),
            fromPack.TaxBrackets.OrderBy(b => b.SortOrder).Select(b => (b.SortOrder, b.UpperLimit, b.RatePercent)));

        // The four statutory deduction types the Demo seed writes (read from the seeder itself).
        var definitions = (Array)typeof(DemoPayrollSeeder).GetField("DeductionDefs", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var statutory = definitions.Cast<(string Code, string NameAr, bool Statutory, decimal? Pct, decimal? Fixed)>().Where(d => d.Statutory).ToList();
        Assert.Equal(4, statutory.Count);
        Assert.Equal(
            statutory.Select(d => (d.Code, d.NameAr, d.Pct ?? 0m, d.Fixed ?? 0m)),
            fromPack.StatutoryDeductions.OrderBy(d => d.SortOrder).Select(d => (d.Code, d.NameAr, d.EmployeePercent, d.FixedAmount)));
        Assert.All(fromPack.StatutoryDeductions, d =>
        {
            Assert.Equal(StatutoryDeductionBaseKind.GrossPay, d.BaseKind);
            Assert.Null(d.Ceiling);
        });

        // The four default holidays.
        Assert.Equal(
            PayrollRegimeSeed.CreateDefaultHolidays().OrderBy(x => x.Date).Select(x => (x.Date, x.NameAr, x.IsRecurring)),
            fromPack.Holidays.OrderBy(x => x.Date).Select(x => (x.Date, x.NameAr, x.IsRecurring)));

        // The three priority rules of the acceptance seed (AcceptancePayrollSeeder.SeedPartialDisbursementAsync).
        Assert.Equal(
            new[] { ("LowIncome", 1, 250_000m), ("FamilySupport", 2, 150_000m), ("General", 3, 0m) },
            fromPack.PriorityRules.OrderBy(r => r.Priority).Select(r => (r.Category, r.Priority, r.MinimumAmount)));
        Assert.All(fromPack.PriorityRules, r => Assert.Equal("Acceptance v2 synthetic treasury directive", r.LegalSource));

        // The register's decisions, verbatim.
        var register = LegalDecisionRegisterSeed.CreateDefaults();
        Assert.Equal(register.Count, pack.Decisions!.Count);
        foreach (var expected in register)
        {
            var actual = new LegalDecisionRecord();
            PayrollFrameworkPackMapper.ApplyDecision(pack.Decisions.Single(d => d.DecisionId == expected.DecisionId), actual);
            Assert.Equal(
                (expected.ConfigField, expected.RuleId, expected.OfficialSource, expected.IssueRef, expected.EffectiveFrom, expected.Owner, expected.ApprovalStatus, expected.RegimeCode, expected.Notes),
                (actual.ConfigField, actual.RuleId, actual.OfficialSource, actual.IssueRef, actual.EffectiveFrom, actual.Owner, actual.ApprovalStatus, actual.RegimeCode, actual.Notes));
        }
    }

    /// <summary>D-PAY-1: every value of the interim pack is interim; tax cites the tax exception, the rest the Law 50 exception.</summary>
    [Fact]
    public void Every_value_of_the_interim_pack_is_interim_and_cites_its_exception()
    {
        var (pack, _) = PayrollFrameworkPackReader.Read(H.InterimPackJson());
        const string tax = "EX-STR2-TAX-INTERIM";
        const string law50 = "EX-STR2-LAW50-UNCERTIFIED";
        string[] taxFields = ["taxableBaseMode", "taxExemptionAmount"];

        foreach (var (name, field) in pack!.Fields!)
        {
            Assert.Equal("Interim", field.LegalStatus);
            Assert.Equal(taxFields.Contains(name) ? tax : law50, field.InterimExceptionId);
        }

        Assert.All(pack.TaxBrackets!, b => Assert.Equal(("Interim", tax), (b.LegalStatus, b.InterimExceptionId)));
        Assert.All(pack.StatutoryDeductions!, d => Assert.Equal(("Interim", d.Code == "DED-TAX" ? tax : law50), (d.LegalStatus, d.InterimExceptionId)));
        Assert.All(pack.Holidays!, x => Assert.Equal(("Interim", law50), (x.LegalStatus, x.InterimExceptionId)));
        Assert.All(pack.PriorityRules!, r => Assert.Equal(("Interim", law50), (r.LegalStatus, r.InterimExceptionId)));

        // Both are exceptions of the STR-002 control catalog.
        using var catalog = JsonDocument.Parse(System.IO.File.ReadAllText(Path.Combine(H.RepoRoot, "docs", "str-legal", "control-catalog.json")));
        var exceptions = catalog.RootElement.GetProperty("exceptions").EnumerateArray().Select(e => e.GetProperty("exceptionId").GetString()).ToList();
        Assert.Contains(tax, exceptions);
        Assert.Contains(law50, exceptions);
    }
}
