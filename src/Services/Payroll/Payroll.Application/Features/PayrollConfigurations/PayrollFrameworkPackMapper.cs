using System.Globalization;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;

namespace Payroll.Application.Features.PayrollConfigurations;

/// <summary>
/// PLAN-54 (§1): a valid pack written onto a draft, and a version written out as a pack. The two are inverse: what an export gives, an
/// import takes, so a draft's tables are edited by exporting, editing the file and importing it into the same draft.
/// </summary>
public static class PayrollFrameworkPackMapper
{
    private static PayrollLegalValueStatus Status(PackSourcedValue value) => Enum.Parse<PayrollLegalValueStatus>(value.LegalStatus!);

    private static string? Exception(PackSourcedValue value) =>
        string.IsNullOrWhiteSpace(value.InterimExceptionId) ? null : value.InterimExceptionId.Trim();

    /// <summary>
    /// Writes the pack's content to <paramref name="draft"/>, whose tables must be empty: the fields, the field sources and the four
    /// tables. The pack has passed <see cref="PayrollFrameworkPackValidator.Validate"/>.
    /// </summary>
    public static void ApplyContent(PayrollFrameworkPack pack, PayrollConfiguration draft)
    {
        draft.RegimeCode = pack.RegimeCode!;
        draft.NameAr = pack.NameAr!.Trim();

        foreach (var name in PackContract.FieldNames)
        {
            var field = pack.Fields![name];
            var property = PackContract.Pascal(name);
            PackFieldValues.Apply(property, field.Value, draft);
            draft.FieldSources.Add(new PayrollConfigurationFieldSource
            {
                PayrollConfigurationId = draft.Id,
                FieldName = property,
                LegalReference = field.LegalReference!.Trim(),
                LegalStatus = Status(field),
                InterimExceptionId = Exception(field),
                TenantId = draft.TenantId
            });
        }

        for (var i = 0; i < pack.TaxBrackets!.Count; i++)
        {
            var row = pack.TaxBrackets[i];
            draft.TaxBrackets.Add(new TaxBracket
            {
                PayrollConfigurationId = draft.Id,
                UpperLimit = row.UpperLimit!.Value,
                RatePercent = row.RatePercent!.Value,
                SortOrder = i,
                LegalReference = row.LegalReference!.Trim(),
                LegalStatus = Status(row),
                InterimExceptionId = Exception(row),
                TenantId = draft.TenantId
            });
        }

        for (var i = 0; i < pack.StatutoryDeductions!.Count; i++)
        {
            var row = pack.StatutoryDeductions[i];
            draft.StatutoryDeductions.Add(new StatutoryDeductionRule
            {
                PayrollConfigurationId = draft.Id,
                Code = row.Code!,
                NameAr = row.NameAr!.Trim(),
                EmployeePercent = row.EmployeePercent!.Value,
                EmployerPercent = row.EmployerPercent!.Value,
                FixedAmount = row.FixedAmount!.Value,
                BaseKind = Enum.Parse<StatutoryDeductionBaseKind>(row.BaseKind!),
                Ceiling = row.Ceiling,
                InsuranceBody = row.InsuranceBody,
                LegalReference = row.LegalReference!.Trim(),
                LegalStatus = Status(row),
                InterimExceptionId = Exception(row),
                SortOrder = i,
                TenantId = draft.TenantId
            });
        }

        foreach (var row in pack.Holidays!)
        {
            PayrollFrameworkPackValidator.TryParseDate(row.Date, out var date);
            draft.Holidays.Add(new OfficialHoliday
            {
                PayrollConfigurationId = draft.Id,
                Date = date,
                NameAr = row.NameAr!.Trim(),
                IsRecurring = row.IsRecurring!.Value,
                LegalReference = row.LegalReference!.Trim(),
                LegalStatus = Status(row),
                InterimExceptionId = Exception(row),
                TenantId = draft.TenantId
            });
        }

        foreach (var row in pack.PriorityRules!)
        {
            draft.PriorityRules.Add(new DisbursementPriorityRule
            {
                // The version is the rule set of its own rules.
                RuleSetId = draft.Id,
                PayrollConfigurationId = draft.Id,
                Category = row.Category!,
                Priority = row.Priority!.Value,
                MinimumAmount = row.MinimumAmount!.Value,
                LegalSource = row.LegalReference!.Trim(),
                IsActive = true,
                LegalStatus = Status(row),
                InterimExceptionId = Exception(row),
                TenantId = draft.TenantId
            });
        }
    }

    /// <summary>Writes a pack's decision onto a register row (a new one, or the one with the same decision id).</summary>
    public static void ApplyDecision(PackDecision source, LegalDecisionRecord target)
    {
        PayrollFrameworkPackValidator.TryParseDate(source.EffectiveFrom, out var from);
        target.DecisionId = source.DecisionId!;
        target.ConfigField = Blank(source.ConfigField);
        target.RuleId = Blank(source.RuleId);
        target.OfficialSource = source.OfficialSource!.Trim();
        target.IssueRef = Blank(source.IssueRef);
        target.EffectiveFrom = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        target.Owner = source.Owner!.Trim();
        target.ApprovalStatus = source.ApprovalStatus!;
        target.RegimeCode = Blank(source.RegimeCode);
        target.Notes = Blank(source.Notes);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Day(DateTime value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static void Source(ILegallySourcedValue from, PackSourcedValue to)
    {
        to.LegalReference = Blank(from.LegalReference);
        to.LegalStatus = from.LegalStatus.ToString();
        to.InterimExceptionId = Blank(from.InterimExceptionId);
    }

    /// <summary>A version as a pack, with the register rows its values cite (<paramref name="decisions"/>).</summary>
    public static PayrollFrameworkPack ToPack(PayrollConfiguration version, IEnumerable<LegalDecisionRecord> decisions)
    {
        var pack = new PayrollFrameworkPack
        {
            Schema = PayrollFrameworkPack.SchemaId,
            Code = version.Code,
            RegimeCode = version.RegimeCode,
            NameAr = version.NameAr,
            EffectiveFrom = Day(version.EffectiveFrom),
            Fields = new Dictionary<string, PackField>(StringComparer.Ordinal),
            TaxBrackets = [],
            StatutoryDeductions = [],
            Holidays = [],
            PriorityRules = [],
            Decisions = [],
        };

        var sources = version.FieldSources.ToDictionary(f => f.FieldName, StringComparer.Ordinal);
        foreach (var property in PayrollConfigurationFieldSource.GovernedFields)
        {
            var field = new PackField { Value = PackFieldValues.Read(property, version) };
            if (sources.TryGetValue(property, out var source))
                Source(source, field);
            else
                field.LegalStatus = nameof(PayrollLegalValueStatus.Interim);
            pack.Fields[PackContract.Camel(property)] = field;
        }

        foreach (var row in version.TaxBrackets.OrderBy(b => b.SortOrder).ThenBy(b => b.UpperLimit))
        {
            var item = new PackTaxBracket { UpperLimit = row.UpperLimit, RatePercent = row.RatePercent };
            Source(row, item);
            pack.TaxBrackets.Add(item);
        }

        foreach (var row in version.StatutoryDeductions.OrderBy(d => d.SortOrder).ThenBy(d => d.Code, StringComparer.Ordinal))
        {
            var item = new PackStatutoryDeduction
            {
                Code = row.Code,
                NameAr = row.NameAr,
                EmployeePercent = row.EmployeePercent,
                EmployerPercent = row.EmployerPercent,
                FixedAmount = row.FixedAmount,
                BaseKind = row.BaseKind.ToString(),
                Ceiling = row.Ceiling,
                InsuranceBody = Blank(row.InsuranceBody),
            };
            Source(row, item);
            pack.StatutoryDeductions.Add(item);
        }

        foreach (var row in version.Holidays.OrderBy(h => h.Date).ThenBy(h => h.NameAr, StringComparer.Ordinal))
        {
            var item = new PackHoliday
            {
                Date = row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                NameAr = row.NameAr,
                IsRecurring = row.IsRecurring,
            };
            Source(row, item);
            pack.Holidays.Add(item);
        }

        foreach (var row in version.PriorityRules.OrderBy(r => r.Priority).ThenBy(r => r.Category, StringComparer.Ordinal))
        {
            var item = new PackPriorityRule { Category = row.Category, Priority = row.Priority, MinimumAmount = row.MinimumAmount };
            Source(row, item);
            pack.PriorityRules.Add(item);
        }

        foreach (var row in decisions.OrderBy(d => d.DecisionId, StringComparer.Ordinal))
        {
            pack.Decisions.Add(new PackDecision
            {
                DecisionId = row.DecisionId,
                ConfigField = row.ConfigField,
                RuleId = row.RuleId,
                OfficialSource = row.OfficialSource,
                IssueRef = row.IssueRef,
                EffectiveFrom = Day(row.EffectiveFrom),
                Owner = row.Owner,
                ApprovalStatus = row.ApprovalStatus,
                RegimeCode = row.RegimeCode,
                Notes = row.Notes,
            });
        }

        return pack;
    }

    /// <summary>The decisions a version's values cite.</summary>
    public static IReadOnlyList<string> CitedDecisionIds(PayrollConfiguration version) =>
        version.LegalValues()
            .Select(v => LegalValueRules.CitedDecisionId(v.LegalReference))
            .Where(id => id is not null)
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
}
