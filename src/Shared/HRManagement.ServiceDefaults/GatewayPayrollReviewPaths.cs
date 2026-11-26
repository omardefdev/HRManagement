using Microsoft.AspNetCore.Http;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// PLAN-58 (§4, D-PAY-3): the payroll paths the independent reviewer (<see cref="HrManagementPolicies.PayrollAuditReview"/>) reaches through
/// the gateway, and nothing else of payroll: the review screens (<c>/ui/payroll/PayrollAuditRecords[/{Action}[/{guid}]]</c>), the two
/// review API writes with their antiforgery token, a run's read (<c>GET /api/payroll/runs/{guid}</c>, which the module's users keep:
/// <see cref="HrManagementPolicies.PayrollRunRead"/>) and the module's static files. Every other payroll path keeps
/// <c>Module.Payroll</c>, which the reviewer does not hold. As <see cref="GatewayApprovalPaths"/> does for an approver's screens.
/// </summary>
public static class GatewayPayrollReviewPaths
{
    public const string Module = "payroll";
    public const string ReviewController = "PayrollAuditRecords";

    /// <summary>The review API, after <c>/api/payroll</c>: the opening (POST), the closing (POST) and the token (GET).</summary>
    public static readonly IReadOnlyList<(string Template, string Method)> ReviewApi =
    [
        ("runs/{guid}/audit-records", HttpMethods.Post),
        ("audit-records/{guid}/close", HttpMethods.Post),
        ("audit-records/antiforgery", HttpMethods.Get),
    ];

    /// <summary>The policy a payroll path takes for the reviewer's reach, or false when the path is none of his.</summary>
    public static bool TryGetPolicy(string httpMethod, string pathValue, out string policy)
    {
        policy = string.Empty;
        if (IsRunRead(httpMethod, pathValue) || GatewayApprovalPaths.IsModuleStaticAsset(Module, httpMethod, pathValue))
        {
            policy = HrManagementPolicies.PayrollRunRead;
            return true;
        }

        if (IsReviewScreen(pathValue) || IsReviewApi(httpMethod, pathValue))
        {
            policy = HrManagementPolicies.PayrollAuditReview;
            return true;
        }

        return false;
    }

    /// <summary><c>GET /api/payroll/runs/{guid}</c> exactly.</summary>
    public static bool IsRunRead(string httpMethod, string pathValue) =>
        HttpMethods.IsGet(httpMethod) && TrySplitApi(pathValue, out var rest) && GatewayApprovalPaths.MatchesTemplate("runs/{guid}", rest);

    /// <summary><c>/ui/payroll/PayrollAuditRecords[/{Action}[/{guid}]]</c>, any verb (the screens' own policies decide the writes).</summary>
    public static bool IsReviewScreen(string pathValue)
    {
        var segments = Segments(pathValue);
        if (segments.Count < 3 || !string.Equals(segments[0], "ui", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(segments[1], Module, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(segments[2], ReviewController, StringComparison.OrdinalIgnoreCase))
            return false;
        return segments.Count switch
        {
            3 or 4 => true,
            5 => Guid.TryParse(segments[4], out _),
            _ => false,
        };
    }

    public static bool IsReviewApi(string httpMethod, string pathValue) =>
        TrySplitApi(pathValue, out var rest)
        && ReviewApi.Any(a => string.Equals(a.Method, httpMethod, StringComparison.OrdinalIgnoreCase) && GatewayApprovalPaths.MatchesTemplate(a.Template, rest));

    private static bool TrySplitApi(string pathValue, out IReadOnlyList<string> rest)
    {
        var segments = Segments(pathValue);
        if (segments.Count >= 2 && string.Equals(segments[0], "api", StringComparison.OrdinalIgnoreCase)
            && string.Equals(segments[1], Module, StringComparison.OrdinalIgnoreCase))
        {
            rest = segments.Skip(2).ToList();
            return true;
        }

        rest = [];
        return false;
    }

    private static List<string> Segments(string pathValue) =>
        pathValue.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
}
