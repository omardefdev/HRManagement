namespace HRManagement.Gateway.Models;

/// <summary>
/// PLAN-67 (D-67-3, G-UX-04): the name a signed-in user carries, so a page can show a person instead of an id (a maker, a checker,
/// an issuer). The gateway writes it from the token's claims when a user signs in, and at most once an hour after that; nothing else
/// writes it. It holds the name only: no e-mail, no role.
/// </summary>
public sealed class GatewayUserName
{
    /// <summary>The user's Keycloak <c>sub</c>, as the services store it in <c>CreatedBy</c>, <c>MakerUserId</c>…</summary>
    public string Sub { get; set; } = "";

    /// <summary>The token's <c>name</c>, else its <c>preferred_username</c>.</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// The user's agency (<c>tenant_id</c>); a lookup answers only the caller's agency (D-67-3). Not named <c>TenantId</c>: the bootstrap's
    /// TEN-002C hardening forces tenant row security on every such column, and the gateway sets no tenant for its writer or lookup, so the
    /// production role would read and write nothing. The lookup filters by agency in its query.
    /// </summary>
    public Guid? AgencyId { get; set; }

    public DateTime FirstSeenAtUtc { get; set; }

    public DateTime LastSeenAtUtc { get; set; }
}
