using Payroll.Domain.Enums;

namespace Payroll.Domain.Entities;

/// <summary>
/// PLAN-54: a value of a payroll framework that governs pay carries its legal reference, its status, and, when interim, the STR-002
/// exception that allows it. A reference that starts with <c>LD-</c> cites a row of the legal decision register.
/// </summary>
public interface ILegallySourcedValue
{
    string? LegalReference { get; }

    PayrollLegalValueStatus LegalStatus { get; }

    string? InterimExceptionId { get; }

    /// <summary>What the value is, for a message to a person (Arabic).</summary>
    string DescribeAr();
}

/// <summary>PLAN-54: the lengths and the decision prefix every legally sourced value shares.</summary>
public static class LegalValueRules
{
    public const int LegalReferenceMaxLength = 512;
    public const int InterimExceptionIdMaxLength = 64;
    public const string DecisionPrefix = "LD-";

    /// <summary>The decision a reference cites, or null when the reference is a free citation.</summary>
    public static string? CitedDecisionId(string? legalReference)
    {
        var value = legalReference?.Trim();
        return !string.IsNullOrEmpty(value) && value.StartsWith(DecisionPrefix, StringComparison.Ordinal) ? value : null;
    }
}
