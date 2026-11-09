using HRManagement.SharedKernel.CodeGeneration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace HR.Core.Infrastructure.Services.CodeGeneration;

/// <summary>PLAN-48 (D-TEN-9): what the generator does when no code policy is published for a kind.</summary>
public enum CodeMissingPolicyMode
{
    /// <summary>An explicit error (422) that burns no number. Every environment except Development and Testing.</summary>
    Refuse,

    /// <summary>Development and Testing only: <c>{FallbackPrefix}-YYYY-#####</c>, never an empty prefix.</summary>
    Fallback
}

/// <summary>PLAN-48 (§2): the <c>CodeGeneration</c> configuration section.</summary>
public sealed class CodeGenerationOptions
{
    public const string SectionName = "CodeGeneration";

    public CodeMissingPolicyMode MissingPolicy { get; set; } = CodeMissingPolicyMode.Refuse;

    /// <summary>Fallback mode only: the prefix of every fallback number; empty means the kind's own prefix.</summary>
    public string? FallbackPrefix { get; set; }

    /// <summary>
    /// D-TEN-9: Fallback is honoured in Development and Testing only (its default there); anywhere else the generator refuses,
    /// whatever the configuration says.
    /// </summary>
    public static CodeGenerationOptions Resolve(IConfiguration? configuration, IHostEnvironment? environment, Action<string>? warn = null)
    {
        var mayFallBack = environment is not null && (environment.IsDevelopment() || environment.IsEnvironment("Testing"));
        var options = new CodeGenerationOptions
        {
            MissingPolicy = mayFallBack ? CodeMissingPolicyMode.Fallback : CodeMissingPolicyMode.Refuse,
            FallbackPrefix = configuration?[$"{SectionName}:FallbackPrefix"]
        };

        var configured = configuration?[$"{SectionName}:MissingPolicy"];
        if (!string.IsNullOrWhiteSpace(configured) && Enum.TryParse<CodeMissingPolicyMode>(configured, ignoreCase: true, out var mode))
        {
            if (mode == CodeMissingPolicyMode.Fallback && !mayFallBack)
                warn?.Invoke($"{SectionName}:MissingPolicy=Fallback is ignored in environment {environment?.EnvironmentName ?? "(none)"}: the generator refuses (D-TEN-9).");
            else
                options.MissingPolicy = mode;
        }

        return options;
    }

    /// <summary>The prefix of a fallback number for <paramref name="kind"/>: never empty.</summary>
    public string FallbackPrefixFor(CodeEntityKind kind)
    {
        if (!string.IsNullOrWhiteSpace(FallbackPrefix))
            return FallbackPrefix.Trim();
        return KindPrefixes.TryGetValue(kind, out var prefix)
            ? prefix
            : kind.ToString().ToUpperInvariant()[..Math.Min(3, kind.ToString().Length)];
    }

    /// <summary>The prefixes of the national policies, so a fallback number still looks like its kind.</summary>
    public static readonly IReadOnlyDictionary<CodeEntityKind, string> KindPrefixes = new Dictionary<CodeEntityKind, string>
    {
        [CodeEntityKind.OrgUnit] = "OU",
        [CodeEntityKind.FundedPosition] = "POS",
        [CodeEntityKind.AssetTag] = "AH",
        [CodeEntityKind.ItAssetTag] = "IT",
        [CodeEntityKind.ArchiveReference] = "ARC",
        [CodeEntityKind.LegalCase] = "LC",
        [CodeEntityKind.ServiceTransaction] = "SRV",
        [CodeEntityKind.IdCardSerial] = "ID",
        [CodeEntityKind.CitizenReference] = "CIT",
        [CodeEntityKind.ForensicCase] = "FOR",
        [CodeEntityKind.CostCenter] = "CC",
        [CodeEntityKind.InventorySku] = "SKU",
        [CodeEntityKind.RecruitmentRequisition] = "REQ",
        [CodeEntityKind.OfferLetter] = "OFF",
        [CodeEntityKind.EstablishmentChangeRequest] = "EST",
        [CodeEntityKind.JobCatalogEntry] = "JC",
        [CodeEntityKind.AdministrativeDecision] = "DEC",
        [CodeEntityKind.RecruitmentCompetition] = "CMP"
    };
}

/// <summary>PLAN-48 (§2): no published code policy for the kind (and key) in the tenant: a 422 that burns no number.</summary>
public sealed class CodePolicyMissingException(CodeEntityKind kind, string? policyKey, Guid tenantId)
    : InvalidOperationException($"No published code policy for {kind}{(policyKey is null ? "" : $" with key {policyKey}")} in tenant {tenantId}.")
{
    public CodeEntityKind Kind { get; } = kind;

    public string? PolicyKey { get; } = policyKey;

    public Guid TenantId { get; } = tenantId;

    public string MessageAr => PolicyKey is null
        ? $"لا توجد سياسة ترميز منشورة لـ {Kind}"
        : $"لا توجد سياسة ترميز منشورة لـ {Kind} بالمفتاح {PolicyKey}";
}

/// <summary>PLAN-48 (§3): the number was issued before in this scope (the issuance's unique index): a 409, logged as an error.</summary>
public sealed class CodeCollisionException(CodeEntityKind kind, string code, string scopeKey, Exception? inner = null)
    : InvalidOperationException($"Code {code} of {kind} was already issued (scope {scopeKey}).", inner)
{
    public CodeEntityKind Kind { get; } = kind;

    public string Code { get; } = code;

    public string ScopeKey { get; } = scopeKey;

    public string MessageAr => $"الرقم {Code} صدر من قبل لـ {Kind}؛ أعد المحاولة، وإن تكرر فراجع عدّاد سياسة الترميز";
}
