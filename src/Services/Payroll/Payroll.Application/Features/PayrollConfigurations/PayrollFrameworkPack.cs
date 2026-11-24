using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Payroll.Application.Features.PayrollConfigurations;

/// <summary>
/// PLAN-54 (§1): the legal pack of a payroll framework, <c>payroll-framework-pack.v1</c>
/// (<c>docs/syr/payroll-framework-packs/schema.v1.json</c> is the documented contract; <see cref="PayrollFrameworkPackValidator"/>
/// is what enforces it, and a unit test keeps the two in line). Every value that governs pay carries its legal reference, its status
/// and, when interim, the STR-002 exception that allows it. A pack is how a framework version enters a production agency, and how a
/// draft's tables are edited (export, edit, import).
/// </summary>
public sealed class PayrollFrameworkPack
{
    public const string SchemaId = "payroll-framework-pack.v1";

    public string? Schema { get; set; }
    public string? Code { get; set; }
    public string? RegimeCode { get; set; }
    public string? NameAr { get; set; }

    /// <summary>ISO date, <c>yyyy-MM-dd</c>.</summary>
    public string? EffectiveFrom { get; set; }

    /// <summary>The numeric fields, keyed by the property's name in camel case.</summary>
    public Dictionary<string, PackField>? Fields { get; set; }

    public List<PackTaxBracket>? TaxBrackets { get; set; }
    public List<PackStatutoryDeduction>? StatutoryDeductions { get; set; }
    public List<PackHoliday>? Holidays { get; set; }
    public List<PackPriorityRule>? PriorityRules { get; set; }
    public List<PackDecision>? Decisions { get; set; }
}

/// <summary>The three things every value of a pack says about its legal source.</summary>
public abstract class PackSourcedValue
{
    public string? LegalReference { get; set; }

    /// <summary><c>Signed</c> or <c>Interim</c>.</summary>
    public string? LegalStatus { get; set; }

    public string? InterimExceptionId { get; set; }
}

public sealed class PackField : PackSourcedValue
{
    /// <summary>A number, a string or a boolean, by the field.</summary>
    public JsonElement Value { get; set; }
}

public sealed class PackTaxBracket : PackSourcedValue
{
    public decimal? UpperLimit { get; set; }
    public decimal? RatePercent { get; set; }
}

public sealed class PackStatutoryDeduction : PackSourcedValue
{
    public string? Code { get; set; }
    public string? NameAr { get; set; }
    public decimal? EmployeePercent { get; set; }
    public decimal? EmployerPercent { get; set; }
    public decimal? FixedAmount { get; set; }

    /// <summary><c>GrossPay</c> or <c>BaseSalary</c>.</summary>
    public string? BaseKind { get; set; }

    public decimal? Ceiling { get; set; }

    /// <summary><c>GOSI</c>, <c>GOIP</c> or absent.</summary>
    public string? InsuranceBody { get; set; }
}

public sealed class PackHoliday : PackSourcedValue
{
    /// <summary>ISO date, <c>yyyy-MM-dd</c>. For a recurring holiday only the month and the day count.</summary>
    public string? Date { get; set; }

    public string? NameAr { get; set; }
    public bool? IsRecurring { get; set; }
}

public sealed class PackPriorityRule : PackSourcedValue
{
    public string? Category { get; set; }
    public int? Priority { get; set; }
    public decimal? MinimumAmount { get; set; }
}

public sealed class PackDecision
{
    public string? DecisionId { get; set; }
    public string? ConfigField { get; set; }
    public string? RuleId { get; set; }
    public string? OfficialSource { get; set; }
    public string? IssueRef { get; set; }

    /// <summary>ISO date, <c>yyyy-MM-dd</c>.</summary>
    public string? EffectiveFrom { get; set; }

    public string? Owner { get; set; }
    public string? ApprovalStatus { get; set; }
    public string? RegimeCode { get; set; }
    public string? Notes { get; set; }
}

/// <summary>One thing wrong with a pack: where (the field and the row) and what, in Arabic. Never the file's content.</summary>
public sealed record PackError(string Path, string Message)
{
    public override string ToString() => $"{Path}: {Message}";
}

/// <summary>
/// Reads an uploaded pack: JSON only, of a bounded size and depth, with no property the contract does not name. A pack that cannot be
/// read answers with where it broke (the JSON path), never with what it contained.
/// </summary>
public static class PayrollFrameworkPackReader
{
    /// <summary>The largest pack accepted, in bytes (a full framework with its decisions is about 20 KB).</summary>
    public const int MaxBytes = 512 * 1024;

    public const int MaxDepth = 16;

    /// <summary>The most rows any one table of a pack may hold.</summary>
    public const int MaxRows = 500;

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = MaxDepth,
        NumberHandling = JsonNumberHandling.Strict,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };

    public static (PayrollFrameworkPack? Pack, PackError? Error) Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return (null, new PackError("$", "ملف الحزمة فارغ"));
        if (Encoding.UTF8.GetByteCount(json) > MaxBytes)
            return (null, new PackError("$", FormattableString.Invariant($"ملف الحزمة أكبر من الحد المسموح ({MaxBytes / 1024} كيلوبايت)")));

        try
        {
            var pack = JsonSerializer.Deserialize<PayrollFrameworkPack>(json, ReadOptions);
            return pack is null
                ? (null, new PackError("$", "ملف الحزمة يجب أن يكون كائن JSON"))
                : (pack, null);
        }
        catch (JsonException ex)
        {
            // The path names the field and the row; the exception's message may quote the file, so it is not passed on.
            return (null, new PackError(string.IsNullOrEmpty(ex.Path) ? "$" : ex.Path, "ليس JSON صالحاً، أو فيه حقل لا يعرفه مخطط الحزمة، أو قيمة من نوع خاطئ"));
        }
    }

    public static string Write(PayrollFrameworkPack pack) => JsonSerializer.Serialize(pack, WriteOptions);
}
