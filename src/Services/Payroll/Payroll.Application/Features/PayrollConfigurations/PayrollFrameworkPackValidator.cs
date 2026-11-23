using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Payroll.Domain.Constants;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Application.Features.PayrollConfigurations;

/// <summary>The names and values <c>payroll-framework-pack.v1</c> allows: what the schema file documents and the validator enforces.</summary>
public static partial class PackContract
{
    /// <summary>The top bracket's upper limit: the largest amount the database stores, written as «no ceiling».</summary>
    public const decimal OpenEndedUpperLimit = 9_999_999_999_999_999.99m;

    public static readonly IReadOnlyList<string> RequiredTopLevel =
        ["schema", "code", "regimeCode", "nameAr", "effectiveFrom", "fields", "taxBrackets", "statutoryDeductions", "holidays", "priorityRules", "decisions"];

    public static readonly IReadOnlyList<string> LegalStatuses = [nameof(PayrollLegalValueStatus.Signed), nameof(PayrollLegalValueStatus.Interim)];
    public static readonly IReadOnlyList<string> BaseKinds = [nameof(StatutoryDeductionBaseKind.GrossPay), nameof(StatutoryDeductionBaseKind.BaseSalary)];
    public static readonly IReadOnlyList<string> InsuranceBodies = StatutoryDeductionRule.InsuranceBodies;
    public static readonly IReadOnlyList<string> RegimeCodes = LegalRegimeCodes.All;
    public static readonly IReadOnlyList<string> TaxableBaseModes = ["GrossPay", "NetOfStatutory"];
    public static readonly IReadOnlyList<string> ProrationBases = Enum.GetNames<ProrationDaysBasis>();
    public static readonly IReadOnlyList<string> RoundingModes = Enum.GetNames<PayrollRoundingMode>();
    public static readonly IReadOnlyList<string> DecisionStatuses = ["ApprovedInterim", "PendingEnactment", "Superseded", "Enacted"];

    /// <summary>The fields of a pack, in camel case: the governed fields of <see cref="PayrollConfigurationFieldSource"/>.</summary>
    public static readonly IReadOnlyList<string> FieldNames =
        PayrollConfigurationFieldSource.GovernedFields.Select(Camel).ToList();

    public static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    public static string Pascal(string name) => char.ToUpperInvariant(name[0]) + name[1..];

    /// <summary>Every property name the contract knows; a JSON path is reported only as far as it is made of these.</summary>
    private static readonly HashSet<string> KnownNames = new(
        RequiredTopLevel.Concat(FieldNames).Concat(
        [
            "value", "legalReference", "legalStatus", "interimExceptionId", "upperLimit", "ratePercent", "nameAr", "employeePercent",
            "employerPercent", "fixedAmount", "baseKind", "ceiling", "insuranceBody", "date", "isRecurring", "category", "priority",
            "minimumAmount", "decisionId", "configField", "ruleId", "officialSource", "issueRef", "owner", "approvalStatus", "notes",
        ]),
        StringComparer.Ordinal);

    [GeneratedRegex(@"\.(?<name>[^.\[\]]+)|\[(?<index>\d{1,6})\]|(?<other>.)", RegexOptions.CultureInvariant)]
    private static partial Regex PathToken();

    /// <summary>
    /// A JSON path made only of names the contract knows and of row numbers. A path that leaves the contract is cut there: a property
    /// name the uploader invented is content of the file, and an error never echoes the file.
    /// </summary>
    public static string SafePath(string? path)
    {
        if (string.IsNullOrEmpty(path) || !path.StartsWith('$'))
            return "$";

        var safe = new System.Text.StringBuilder("$");
        foreach (Match token in PathToken().Matches(path[1..]))
        {
            if (token.Groups["name"].Success && KnownNames.Contains(token.Groups["name"].Value))
                safe.Append('.').Append(token.Groups["name"].Value);
            else if (token.Groups["index"].Success)
                safe.Append('[').Append(token.Groups["index"].Value).Append(']');
            else
                break;
        }

        return safe.ToString();
    }
}

/// <summary>
/// PLAN-54 (§1): the rules of a legal pack, checked before anything is written. Each failure names the field and the row (the first row
/// is 1) in Arabic and says what is required; it never repeats a value from the file.
/// </summary>
public static partial class PayrollFrameworkPackValidator
{
    public const int MaxErrors = 50;

    [GeneratedRegex("^[A-Z][A-Z0-9_]{2,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();

    [GeneratedRegex("^[A-Z][A-Z0-9-]{1,49}$", RegexOptions.CultureInvariant)]
    private static partial Regex DeductionCodePattern();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]{1,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex CategoryPattern();

    [GeneratedRegex("^LD-[A-Z0-9-]{1,61}$", RegexOptions.CultureInvariant)]
    private static partial Regex DecisionIdPattern();

    [GeneratedRegex("^[A-Z0-9][A-Z0-9-]{2,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ExceptionIdPattern();

    [GeneratedRegex("^[A-Z]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyPattern();

    public static bool IsValidCode(string? code) => code is not null && CodePattern().IsMatch(code);

    public static bool TryParseDate(string? text, out DateOnly date) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    /// <summary>Money and percentages are stored with two decimals; a third would be rounded away silently.</summary>
    private static bool HasAtMostTwoDecimals(decimal value) => decimal.Round(value, 2) == value;

    public static IReadOnlyList<PackError> Validate(PayrollFrameworkPack pack)
    {
        var errors = new List<PackError>();
        void Add(string path, string message)
        {
            if (errors.Count < MaxErrors) errors.Add(new PackError(path, message));
        }

        if (pack.Schema != PayrollFrameworkPack.SchemaId)
            Add("$.schema", $"مخطط الحزمة يجب أن يكون {PayrollFrameworkPack.SchemaId}");
        if (!IsValidCode(pack.Code))
            Add("$.code", "رمز الإطار إلزامي: أحرف لاتينية كبيرة وأرقام وشرطة سفلية، من 3 إلى 100 حرف");
        if (pack.RegimeCode is null || !PackContract.RegimeCodes.Contains(pack.RegimeCode))
            Add("$.regimeCode", "النظام القانوني إلزامي ومن القائمة المعتمدة: " + string.Join("، ", PackContract.RegimeCodes));
        if (string.IsNullOrWhiteSpace(pack.NameAr) || pack.NameAr.Trim().Length > 200)
            Add("$.nameAr", "اسم الإطار بالعربية إلزامي ولا يتجاوز 200 حرف");
        if (!TryParseDate(pack.EffectiveFrom, out _))
            Add("$.effectiveFrom", "تاريخ السريان إلزامي بصيغة yyyy-MM-dd");

        ValidateFields(pack, Add);
        ValidateBrackets(pack, Add);
        ValidateStatutoryDeductions(pack, Add);
        ValidateHolidays(pack, Add);
        ValidatePriorityRules(pack, Add);
        ValidateDecisions(pack, Add);
        return errors;
    }

    private static void ValidateSource(PackSourcedValue value, string path, Action<string, string> add)
    {
        if (string.IsNullOrWhiteSpace(value.LegalReference) || value.LegalReference.Trim().Length > LegalValueRules.LegalReferenceMaxLength)
            add($"{path}.legalReference", "المرجع القانوني إلزامي لكل قيمة ولا يتجاوز 512 حرفاً");
        else if (value.LegalReference.Trim().StartsWith(LegalValueRules.DecisionPrefix, StringComparison.Ordinal)
                 && !DecisionIdPattern().IsMatch(value.LegalReference.Trim()))
            add($"{path}.legalReference", "المرجع الذي يبدأ بـ LD- هو رقم قرار في سجل القرارات: أحرف لاتينية كبيرة وأرقام وشرطات فقط");

        if (value.LegalStatus is null || !PackContract.LegalStatuses.Contains(value.LegalStatus))
        {
            add($"{path}.legalStatus", "الحالة القانونية إلزامية: Signed أو Interim");
            return;
        }

        var exception = value.InterimExceptionId?.Trim();
        if (value.LegalStatus == nameof(PayrollLegalValueStatus.Interim))
        {
            if (string.IsNullOrEmpty(exception) || !ExceptionIdPattern().IsMatch(exception))
                add($"{path}.interimExceptionId", "القيمة المؤقتة (Interim) يجب أن تستشهد برقم استثناء من كتالوج STR-002");
        }
        else if (!string.IsNullOrEmpty(exception))
        {
            add($"{path}.interimExceptionId", "القيمة الموقّعة (Signed) لا تستشهد باستثناء مؤقت");
        }
    }

    private static void ValidateFields(PayrollFrameworkPack pack, Action<string, string> add)
    {
        if (pack.Fields is null)
        {
            add("$.fields", "الحقول العددية للإطار إلزامية");
            return;
        }

        foreach (var name in PackContract.FieldNames)
        {
            var path = $"$.fields.{name}";
            if (!pack.Fields.TryGetValue(name, out var field) || field is null)
            {
                add(path, "الحقل إلزامي في الحزمة، بقيمته ومرجعه القانوني وحالته");
                continue;
            }

            ValidateSource(field, path, add);
            if (PackFieldValues.Check(PackContract.Pascal(name), field.Value) is { } valueError)
                add($"{path}.value", valueError);
        }

        if (pack.Fields.Keys.Any(k => !PackContract.FieldNames.Contains(k)))
            add("$.fields", "في الحقول اسم لا يعرفه مخطط الحزمة");
    }

    private static void ValidateBrackets(PayrollFrameworkPack pack, Action<string, string> add)
    {
        var rows = pack.TaxBrackets;
        if (rows is null || rows.Count == 0)
        {
            add("$.taxBrackets", "شرائح الضريبة إلزامية: إطار بلا شرائح لا يُنشر");
            return;
        }

        if (rows.Count > PayrollFrameworkPackReader.MaxRows)
        {
            add("$.taxBrackets", "عدد الشرائح يتجاوز الحد المسموح");
            return;
        }

        decimal previous = 0m;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var path = $"$.taxBrackets[{i}]";
            var label = FormattableString.Invariant($"الشريحة {i + 1}");
            if (row is null)
            {
                add(path, $"{label}: الصف فارغ");
                continue;
            }

            ValidateSource(row, path, add);
            if (row.RatePercent is not { } rate || rate < 0m || rate > 100m || !HasAtMostTwoDecimals(rate))
                add($"{path}.ratePercent", $"{label}: النسبة إلزامية بين 0 و100 وبمنزلتين عشريتين على الأكثر");
            if (row.UpperLimit is not { } limit || limit <= 0m || limit > PackContract.OpenEndedUpperLimit || !HasAtMostTwoDecimals(limit))
            {
                add($"{path}.upperLimit", $"{label}: الحد الأعلى إلزامي وموجب وبمنزلتين عشريتين على الأكثر");
                continue;
            }

            // Ascending with no gap: a bracket starts where the previous one ends, so its limit must be above it.
            if (limit <= previous)
                add($"{path}.upperLimit", $"{label}: الشرائح تصاعدية بلا فجوات؛ الحد الأعلى يجب أن يزيد على حد الشريحة السابقة");
            previous = limit;
        }

        // No gap above the last bracket either: income over the last limit would otherwise be taxed by no bracket.
        if (rows[^1]?.UpperLimit is { } last && last != PackContract.OpenEndedUpperLimit)
            add($"$.taxBrackets[{rows.Count - 1}].upperLimit", "الشريحة الأخيرة بلا سقف: حدّها الأعلى هو 9999999999999999.99");
    }

    private static void ValidateStatutoryDeductions(PayrollFrameworkPack pack, Action<string, string> add)
    {
        var rows = pack.StatutoryDeductions;
        if (rows is null)
        {
            add("$.statutoryDeductions", "جدول الخصومات النظامية إلزامي (ويجوز أن يكون فارغاً)");
            return;
        }

        if (rows.Count > PayrollFrameworkPackReader.MaxRows)
        {
            add("$.statutoryDeductions", "عدد الخصومات النظامية يتجاوز الحد المسموح");
            return;
        }

        var codes = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var path = $"$.statutoryDeductions[{i}]";
            var label = FormattableString.Invariant($"الخصم النظامي {i + 1}");
            if (row is null)
            {
                add(path, $"{label}: الصف فارغ");
                continue;
            }

            ValidateSource(row, path, add);
            if (row.Code is null || !DeductionCodePattern().IsMatch(row.Code))
                add($"{path}.code", $"{label}: الرمز إلزامي: أحرف لاتينية كبيرة وأرقام وشرطات، حتى 50 حرفاً");
            else if (!codes.Add(row.Code))
                add($"{path}.code", $"{label}: الرمز مكرر في الحزمة");
            if (string.IsNullOrWhiteSpace(row.NameAr) || row.NameAr.Trim().Length > StatutoryDeductionRule.NameMaxLength)
                add($"{path}.nameAr", $"{label}: الاسم بالعربية إلزامي ولا يتجاوز 200 حرف");
            if (row.EmployeePercent is not { } employee || employee < 0m || employee > 100m || !HasAtMostTwoDecimals(employee))
                add($"{path}.employeePercent", $"{label}: حصة الموظف إلزامية بين 0 و100 وبمنزلتين عشريتين على الأكثر");
            if (row.EmployerPercent is not { } employer || employer < 0m || employer > 100m || !HasAtMostTwoDecimals(employer))
                add($"{path}.employerPercent", $"{label}: حصة صاحب العمل إلزامية بين 0 و100 وبمنزلتين عشريتين على الأكثر");
            if (row.FixedAmount is not { } fixedAmount || fixedAmount < 0m || !HasAtMostTwoDecimals(fixedAmount))
                add($"{path}.fixedAmount", $"{label}: المبلغ الثابت إلزامي وغير سالب وبمنزلتين عشريتين على الأكثر");
            if (row.BaseKind is null || !PackContract.BaseKinds.Contains(row.BaseKind))
                add($"{path}.baseKind", $"{label}: وعاء الاحتساب إلزامي: GrossPay أو BaseSalary");
            if (row.Ceiling is { } ceiling && (ceiling <= 0m || !HasAtMostTwoDecimals(ceiling)))
                add($"{path}.ceiling", $"{label}: سقف الوعاء، إن وُجد، موجب وبمنزلتين عشريتين على الأكثر");
            if (row.InsuranceBody is not null && !PackContract.InsuranceBodies.Contains(row.InsuranceBody))
                add($"{path}.insuranceBody", $"{label}: جهة التأمين، إن وُجدت: GOSI أو GOIP");
        }
    }

    private static void ValidateHolidays(PayrollFrameworkPack pack, Action<string, string> add)
    {
        var rows = pack.Holidays;
        if (rows is null)
        {
            add("$.holidays", "جدول العطل الرسمية إلزامي (ويجوز أن يكون فارغاً)");
            return;
        }

        if (rows.Count > PayrollFrameworkPackReader.MaxRows)
        {
            add("$.holidays", "عدد العطل يتجاوز الحد المسموح");
            return;
        }

        var seen = new HashSet<(DateOnly, bool)>();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var path = $"$.holidays[{i}]";
            var label = FormattableString.Invariant($"العطلة {i + 1}");
            if (row is null)
            {
                add(path, $"{label}: الصف فارغ");
                continue;
            }

            ValidateSource(row, path, add);
            if (string.IsNullOrWhiteSpace(row.NameAr) || row.NameAr.Trim().Length > 200)
                add($"{path}.nameAr", $"{label}: الاسم بالعربية إلزامي ولا يتجاوز 200 حرف");
            if (row.IsRecurring is null)
                add($"{path}.isRecurring", $"{label}: يجب بيان هل العطلة متكررة سنوياً (true أو false)");
            if (!TryParseDate(row.Date, out var date))
                add($"{path}.date", $"{label}: التاريخ إلزامي بصيغة yyyy-MM-dd");
            else if (!seen.Add((date, row.IsRecurring ?? false)))
                add($"{path}.date", $"{label}: التاريخ مكرر في الحزمة");
        }
    }

    private static void ValidatePriorityRules(PayrollFrameworkPack pack, Action<string, string> add)
    {
        var rows = pack.PriorityRules;
        if (rows is null)
        {
            add("$.priorityRules", "جدول قواعد أولوية الصرف إلزامي (ويجوز أن يكون فارغاً)");
            return;
        }

        if (rows.Count > PayrollFrameworkPackReader.MaxRows)
        {
            add("$.priorityRules", "عدد قواعد الأولوية يتجاوز الحد المسموح");
            return;
        }

        var categories = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var path = $"$.priorityRules[{i}]";
            var label = FormattableString.Invariant($"قاعدة الأولوية {i + 1}");
            if (row is null)
            {
                add(path, $"{label}: الصف فارغ");
                continue;
            }

            ValidateSource(row, path, add);
            if (row.Category is null || !CategoryPattern().IsMatch(row.Category))
                add($"{path}.category", $"{label}: الفئة إلزامية: أحرف لاتينية وأرقام، حتى 100 حرف");
            else if (!categories.Add(row.Category))
                add($"{path}.category", $"{label}: الفئة مكررة في الحزمة");
            if (row.Priority is not { } priority || priority < 1 || priority > 1000)
                add($"{path}.priority", $"{label}: الأولوية إلزامية بين 1 و1000 (الأصغر يُصرف أولاً)");
            if (row.MinimumAmount is not { } minimum || minimum < 0m || !HasAtMostTwoDecimals(minimum))
                add($"{path}.minimumAmount", $"{label}: الحد الأدنى إلزامي وغير سالب وبمنزلتين عشريتين على الأكثر");
        }
    }

    private static void ValidateDecisions(PayrollFrameworkPack pack, Action<string, string> add)
    {
        var rows = pack.Decisions;
        if (rows is null)
        {
            add("$.decisions", "جدول القرارات القانونية إلزامي (ويجوز أن يكون فارغاً إن كانت القرارات مسجلة من قبل)");
            return;
        }

        if (rows.Count > PayrollFrameworkPackReader.MaxRows)
        {
            add("$.decisions", "عدد القرارات يتجاوز الحد المسموح");
            return;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var path = $"$.decisions[{i}]";
            var label = FormattableString.Invariant($"القرار {i + 1}");
            if (row is null)
            {
                add(path, $"{label}: الصف فارغ");
                continue;
            }

            if (row.DecisionId is null || !DecisionIdPattern().IsMatch(row.DecisionId))
                add($"{path}.decisionId", $"{label}: رقم القرار إلزامي ويبدأ بـ LD-، بأحرف لاتينية كبيرة وأرقام وشرطات، حتى 64 حرفاً");
            else if (!ids.Add(row.DecisionId))
                add($"{path}.decisionId", $"{label}: رقم القرار مكرر في الحزمة");
            if (string.IsNullOrWhiteSpace(row.OfficialSource) || row.OfficialSource.Trim().Length > 512)
                add($"{path}.officialSource", $"{label}: المصدر الرسمي إلزامي ولا يتجاوز 512 حرفاً");
            if (string.IsNullOrWhiteSpace(row.Owner) || row.Owner.Trim().Length > 128)
                add($"{path}.owner", $"{label}: الجهة المالكة للقرار إلزامية ولا تتجاوز 128 حرفاً");
            if (row.ApprovalStatus is null || !PackContract.DecisionStatuses.Contains(row.ApprovalStatus))
                add($"{path}.approvalStatus", $"{label}: حالة القرار إلزامية ومن القائمة: " + string.Join("، ", PackContract.DecisionStatuses));
            if (!TryParseDate(row.EffectiveFrom, out _))
                add($"{path}.effectiveFrom", $"{label}: تاريخ سريان القرار إلزامي بصيغة yyyy-MM-dd");
            if (row.ConfigField is { Length: > 256 })
                add($"{path}.configField", $"{label}: حقل الإعداد لا يتجاوز 256 حرفاً");
            if (row.RuleId is { Length: > 128 })
                add($"{path}.ruleId", $"{label}: رقم القاعدة لا يتجاوز 128 حرفاً");
            if (row.IssueRef is { Length: > 128 })
                add($"{path}.issueRef", $"{label}: مرجع الإصدار لا يتجاوز 128 حرفاً");
            if (row.RegimeCode is { Length: > 50 })
                add($"{path}.regimeCode", $"{label}: رمز النظام لا يتجاوز 50 حرفاً");
            if (row.Notes is { Length: > 2000 })
                add($"{path}.notes", $"{label}: الملاحظات لا تتجاوز 2000 حرف");
        }
    }

    /// <summary>The decisions the pack's values cite (a legal reference that starts with <c>LD-</c>).</summary>
    public static IReadOnlyList<(string Path, string DecisionId)> CitedDecisions(PayrollFrameworkPack pack)
    {
        var cited = new List<(string, string)>();
        void Take(PackSourcedValue? value, string path)
        {
            if (LegalValueRules.CitedDecisionId(value?.LegalReference) is { } id)
                cited.Add(($"{path}.legalReference", id));
        }

        foreach (var (name, field) in pack.Fields ?? [])
            if (PackContract.FieldNames.Contains(name)) Take(field, $"$.fields.{name}");
        for (var i = 0; i < (pack.TaxBrackets?.Count ?? 0); i++) Take(pack.TaxBrackets![i], $"$.taxBrackets[{i}]");
        for (var i = 0; i < (pack.StatutoryDeductions?.Count ?? 0); i++) Take(pack.StatutoryDeductions![i], $"$.statutoryDeductions[{i}]");
        for (var i = 0; i < (pack.Holidays?.Count ?? 0); i++) Take(pack.Holidays![i], $"$.holidays[{i}]");
        for (var i = 0; i < (pack.PriorityRules?.Count ?? 0); i++) Take(pack.PriorityRules![i], $"$.priorityRules[{i}]");
        return cited;
    }
}

/// <summary>The value of each numeric field of a pack: its type, its range, and how it is written to and read from the version.</summary>
public static class PackFieldValues
{
    private static bool Int(JsonElement value, int min, int max, out int result)
    {
        result = 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out result) && result >= min && result <= max;
    }

    private static bool Money(JsonElement value, decimal min, decimal max, out decimal result)
    {
        result = 0m;
        return value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out result)
               && result >= min && result <= max && decimal.Round(result, 2) == result;
    }

    private static bool OneOf(JsonElement value, IReadOnlyList<string> allowed, out string result)
    {
        result = string.Empty;
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } text || !allowed.Contains(text))
            return false;
        result = text;
        return true;
    }

    private static bool Bool(JsonElement value, out bool result)
    {
        result = value.ValueKind == JsonValueKind.True;
        return value.ValueKind is JsonValueKind.True or JsonValueKind.False;
    }

    private static bool WeekDays(JsonElement value, out string result)
    {
        result = string.Empty;
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { Length: <= 100 } text)
            return false;
        var days = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (days.Length is 0 or > 6 || days.Distinct(StringComparer.Ordinal).Count() != days.Length
            || days.Any(d => !Enum.GetNames<DayOfWeek>().Contains(d, StringComparer.Ordinal)))
            return false;
        result = string.Join(",", days);
        return true;
    }

    private const decimal MaxMoney = 9_999_999_999_999_999.99m;

    /// <summary>Null when <paramref name="value"/> is a valid value of <paramref name="field"/>; else what the field requires (Arabic).</summary>
    public static string? Check(string field, JsonElement value) => Apply(field, value, null);

    /// <summary>Writes the pack's value of <paramref name="field"/> to <paramref name="target"/>; returns the rule broken, if any.</summary>
    public static string? Apply(string field, JsonElement value, PayrollConfiguration? target)
    {
        switch (field)
        {
            case nameof(PayrollConfiguration.ProrationDaysBasis):
                if (!OneOf(value, PackContract.ProrationBases, out var basis)) return "القيمة من القائمة: " + string.Join("، ", PackContract.ProrationBases);
                if (target is not null) target.ProrationDaysBasis = Enum.Parse<ProrationDaysBasis>(basis);
                return null;
            case nameof(PayrollConfiguration.DailyRateDivisor):
                if (!Int(value, 1, 31, out var divisor)) return "عدد صحيح بين 1 و31";
                if (target is not null) target.DailyRateDivisor = divisor;
                return null;
            case nameof(PayrollConfiguration.MissionAllowanceAmount):
                if (!Money(value, 0m, MaxMoney, out var mission)) return "مبلغ غير سالب بمنزلتين عشريتين على الأكثر";
                if (target is not null) target.MissionAllowanceAmount = mission;
                return null;
            case nameof(PayrollConfiguration.SecondmentAllowancePercent):
                if (!Money(value, 0m, 100m, out var secondment)) return "نسبة بين 0 و100 بمنزلتين عشريتين على الأكثر";
                if (target is not null) target.SecondmentAllowancePercent = secondment;
                return null;
            case nameof(PayrollConfiguration.EnableTrainingPremiums):
                if (!Bool(value, out var training)) return "true أو false";
                if (target is not null) target.EnableTrainingPremiums = training;
                return null;
            case nameof(PayrollConfiguration.TrainingPremiumAmount):
                if (!Money(value, 0m, MaxMoney, out var premium)) return "مبلغ غير سالب بمنزلتين عشريتين على الأكثر";
                if (target is not null) target.TrainingPremiumAmount = premium;
                return null;
            case nameof(PayrollConfiguration.MedicalCompensationDefaultAmount):
                if (!Money(value, 0m, MaxMoney, out var medical)) return "مبلغ غير سالب بمنزلتين عشريتين على الأكثر";
                if (target is not null) target.MedicalCompensationDefaultAmount = medical;
                return null;
            case nameof(PayrollConfiguration.MaxRetroCorrectionMonths):
                if (!Int(value, 0, 120, out var months)) return "عدد صحيح بين 0 و120";
                if (target is not null) target.MaxRetroCorrectionMonths = months;
                return null;
            case nameof(PayrollConfiguration.EosGratuityRateFirstPeriod):
                if (!Money(value, 0m, 12m, out var first)) return "معدّل بين 0 و12 بمنزلتين عشريتين على الأكثر";
                if (target is not null) target.EosGratuityRateFirstPeriod = first;
                return null;
            case nameof(PayrollConfiguration.EosGratuityFirstYearsThreshold):
                if (!Int(value, 0, 60, out var threshold)) return "عدد صحيح بين 0 و60";
                if (target is not null) target.EosGratuityFirstYearsThreshold = threshold;
                return null;
            case nameof(PayrollConfiguration.EosGratuityRateAfterThreshold):
                if (!Money(value, 0m, 12m, out var after)) return "معدّل بين 0 و12 بمنزلتين عشريتين على الأكثر";
                if (target is not null) target.EosGratuityRateAfterThreshold = after;
                return null;
            case nameof(PayrollConfiguration.NonWorkingDays):
                if (!WeekDays(value, out var days)) return "أسماء أيام الأسبوع بالإنجليزية مفصولة بفواصل، مثل Friday,Saturday";
                if (target is not null) target.NonWorkingDays = days;
                return null;
            case nameof(PayrollConfiguration.UseDbHolidaysOnly):
                if (!Bool(value, out var dbOnly)) return "true أو false";
                if (target is not null) target.UseDbHolidaysOnly = dbOnly;
                return null;
            case nameof(PayrollConfiguration.TaxableBaseMode):
                if (!OneOf(value, PackContract.TaxableBaseModes, out var mode)) return "القيمة من القائمة: " + string.Join("، ", PackContract.TaxableBaseModes);
                if (target is not null) target.TaxableBaseMode = mode;
                return null;
            case nameof(PayrollConfiguration.TaxExemptionAmount):
                if (!Money(value, 0m, MaxMoney, out var exemption)) return "مبلغ غير سالب بمنزلتين عشريتين على الأكثر";
                if (target is not null) target.TaxExemptionAmount = exemption;
                return null;
            case nameof(PayrollConfiguration.LeavePayoutDailyDivisor):
                if (!Int(value, 1, 31, out var leaveDivisor)) return "عدد صحيح بين 1 و31";
                if (target is not null) target.LeavePayoutDailyDivisor = leaveDivisor;
                return null;
            case nameof(PayrollConfiguration.RoundingMode):
                if (!OneOf(value, PackContract.RoundingModes, out var rounding)) return "القيمة من القائمة: " + string.Join("، ", PackContract.RoundingModes);
                if (target is not null) target.RoundingMode = Enum.Parse<PayrollRoundingMode>(rounding);
                return null;
            case nameof(PayrollConfiguration.DecimalPlaces):
                if (!Int(value, 0, 6, out var places)) return "عدد صحيح بين 0 و6";
                if (target is not null) target.DecimalPlaces = places;
                return null;
            case nameof(PayrollConfiguration.CurrencyCode):
                if (value.ValueKind != JsonValueKind.String || value.GetString() is not { Length: 3 } currency
                    || currency.Any(c => c is < 'A' or > 'Z'))
                    return "رمز العملة بثلاثة أحرف لاتينية كبيرة";
                if (target is not null) target.CurrencyCode = currency;
                return null;
            default:
                return "حقل لا يعرفه مخطط الحزمة";
        }
    }

    /// <summary>The value of <paramref name="field"/> on <paramref name="source"/>, as a pack writes it.</summary>
    public static JsonElement Read(string field, PayrollConfiguration source)
    {
        object value = field switch
        {
            nameof(PayrollConfiguration.ProrationDaysBasis) => source.ProrationDaysBasis.ToString(),
            nameof(PayrollConfiguration.DailyRateDivisor) => source.DailyRateDivisor,
            nameof(PayrollConfiguration.MissionAllowanceAmount) => source.MissionAllowanceAmount,
            nameof(PayrollConfiguration.SecondmentAllowancePercent) => source.SecondmentAllowancePercent,
            nameof(PayrollConfiguration.EnableTrainingPremiums) => source.EnableTrainingPremiums,
            nameof(PayrollConfiguration.TrainingPremiumAmount) => source.TrainingPremiumAmount,
            nameof(PayrollConfiguration.MedicalCompensationDefaultAmount) => source.MedicalCompensationDefaultAmount,
            nameof(PayrollConfiguration.MaxRetroCorrectionMonths) => source.MaxRetroCorrectionMonths,
            nameof(PayrollConfiguration.EosGratuityRateFirstPeriod) => source.EosGratuityRateFirstPeriod,
            nameof(PayrollConfiguration.EosGratuityFirstYearsThreshold) => source.EosGratuityFirstYearsThreshold,
            nameof(PayrollConfiguration.EosGratuityRateAfterThreshold) => source.EosGratuityRateAfterThreshold,
            nameof(PayrollConfiguration.NonWorkingDays) => source.NonWorkingDays,
            nameof(PayrollConfiguration.UseDbHolidaysOnly) => source.UseDbHolidaysOnly,
            nameof(PayrollConfiguration.TaxableBaseMode) => source.TaxableBaseMode,
            nameof(PayrollConfiguration.TaxExemptionAmount) => source.TaxExemptionAmount,
            nameof(PayrollConfiguration.LeavePayoutDailyDivisor) => source.LeavePayoutDailyDivisor,
            nameof(PayrollConfiguration.RoundingMode) => source.RoundingMode.ToString(),
            nameof(PayrollConfiguration.DecimalPlaces) => source.DecimalPlaces,
            nameof(PayrollConfiguration.CurrencyCode) => source.CurrencyCode,
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, "not a governed field"),
        };
        return JsonSerializer.SerializeToElement(value);
    }
}
