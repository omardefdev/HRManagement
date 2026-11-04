using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;

namespace HRManagement.ServiceDefaults.Messaging;

/// <summary>
/// PLAN-40 (§6): the token every replay POST must send. Replay runs with a gateway session as well as a bearer token, so
/// a forged cross-site POST must not reach it. The token endpoint sits behind the same policy as the replay itself.
/// </summary>
public sealed record MessagingRepairAntiforgery(string HeaderName, string RequestToken)
{
    public static MessagingRepairAntiforgery Issue(IAntiforgery antiforgery, HttpContext context)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        return new MessagingRepairAntiforgery(tokens.HeaderName!, tokens.RequestToken!);
    }
}
