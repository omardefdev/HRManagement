using System.Text.Json;
using Auth.Directory.Domain.Entities;
using Auth.Directory.Infrastructure.Data;
using Auth.Directory.Infrastructure.Keycloak;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Auth.Directory.Infrastructure.Consumers;

/// <summary>
/// The directory follows a transfer. PLAN-52 (D-52-4, P-10): a transferred user who carries a scope loses it — fail-closed: his scope
/// becomes <c>InvalidClaim</c>, so 403, until the directory admin assigns a new one. The local <see cref="AuthUser.ScopeOrgUnitId"/> is
/// cleared and audited (old unit, new unit, transfer); with <c>Keycloak:DirectorySync:Enabled</c> the token's attribute is removed too,
/// whatever the local value (a failure there retries the message, so a token never keeps the old scope while the directory says otherwise);
/// with the sync off the token keeps it until the sync runs, and a warning and <c>directory_scope_sync_skipped_total</c> say so (D-52-4;
/// PLAN-62 turns the sync on). Every employee's user gets a scope when the directory creates it, so the counter counts every transfer then.
/// <c>branch-manager</c> stays: removing it alone would make an <c>hr-core</c> holder unrestricted. The claim matters only with that role,
/// so clearing it never widens anyone's access.
/// <para>
/// Idempotent per transfer: one receipt per transfer (<c>TransferRequestId</c>, or the message id when it is null) in the same save as the
/// change, so a redelivery changes nothing and writes no second audit row. Messages are not ordered: a late first delivery of an older
/// transfer is applied when it arrives (it clears a scope, which is fail-closed).
/// </para>
/// </summary>
public class TransferExecutedConsumer(
    AuthDirectoryDbContext db,
    ILogger<TransferExecutedConsumer> logger,
    IKeycloakUserProvisioningService? keycloak = null,
    IConfiguration? configuration = null) : IConsumer<ITransferExecuted>
{
    public const string ReceiptConsumer = "auth-directory.transfer-executed.scope";
    public const string ScopeClearedAction = "ScopeClearedOnTransfer";

    public static string? IdempotencyKey(ITransferExecuted message, Guid? messageId) =>
        message.TransferRequestId is Guid transfer ? $"transfer:{transfer:D}" : messageId?.ToString("D");

    public async Task Consume(ConsumeContext<ITransferExecuted> context)
    {
        var m = context.Message;
        var ct = context.CancellationToken;
        logger.LogInformation(
            "Auth.Directory: TransferExecuted EmployeeId={EmployeeId} To={ToOrgUnitId}",
            m.EmployeeId, m.ToOrgUnitId);

        var key = IdempotencyKey(m, context.MessageId);
        var tenant = m.TenantId ?? TenantIdFlow.Get() ?? Guid.Empty;
        if (key is not null && await db.HasReceiptAsync(key, ReceiptConsumer, tenant, ct))
        {
            logger.LogInformation("Auth.Directory: transfer already applied, skipped (redelivery)");
            return;
        }

        var entry = await db.DirectoryEntries.FirstOrDefaultAsync(e => e.EmployeeId == m.EmployeeId, ct);
        if (entry is null)
        {
            db.DirectoryEntries.Add(new DirectoryEntry
            {
                EmployeeId = m.EmployeeId,
                DisplayName = m.EmployeeId.ToString(),
                PrimaryOrgUnitId = m.ToOrgUnitId,
                Status = "Active",
                LastSyncedAt = DateTime.UtcNow
            });
        }
        else
        {
            entry.PrimaryOrgUnitId = m.ToOrgUnitId;
            entry.LastSyncedAt = DateTime.UtcNow;
        }

        var user = await db.AuthUsers.FirstOrDefaultAsync(u => u.EmployeeId == m.EmployeeId, ct);
        if (user is not null)
        {
            var oldScope = user.ScopeOrgUnitId;
            var syncEnabled = configuration?.GetValue("Keycloak:DirectorySync:Enabled", false) == true;
            var canSync = syncEnabled && keycloak is not null && !string.IsNullOrWhiteSpace(user.KeycloakUserId);

            // Review 5: with the sync on, Keycloak is asked whatever the local value says, so a scope that exists only in the token (a
            // user written straight into the realm) goes too. A failure throws: the message retries and nothing is saved.
            var removedInKeycloak = false;
            if (canSync)
            {
                var result = await keycloak!.TryClearScopeAttributeAsync(user.KeycloakUserId!, ct);
                if (result is { Success: false })
                    throw new InvalidOperationException($"Keycloak did not remove the scope of a transferred user ({result.Detail}); the message retries.");
                removedInKeycloak = result.RemovedScope;
            }

            if (oldScope is not null || removedInKeycloak)
            {
                user.ScopeOrgUnitId = null;
                db.AuthAuditLogs.Add(new AuthAuditLog
                {
                    Action = ScopeClearedAction,
                    EntityType = nameof(AuthUser),
                    EntityId = user.Id,
                    PerformedBy = "TransferExecuted",
                    PerformedAt = DateTime.UtcNow,
                    Details = JsonSerializer.Serialize(new
                    {
                        employeeId = m.EmployeeId,
                        fromScopeOrgUnitId = oldScope,
                        fromOrgUnitId = m.FromOrgUnitId,
                        toOrgUnitId = m.ToOrgUnitId,
                        transferRequestId = m.TransferRequestId,
                        keycloak = canSync ? (removedInKeycloak ? "removed" : "absent") : "not synced"
                    })
                });

                if (!canSync)
                {
                    logger.LogWarning(
                        "Auth.Directory: scope cleared locally for a transferred user; the Keycloak attribute was not touched (directory sync {State})",
                        syncEnabled ? "on, but the user has no Keycloak id" : "off");
                    JobActivitySource.DirectoryScopeSyncSkipped.Add(1);
                }
            }
        }

        if (key is not null)
            db.StageReceipt(key, ReceiptConsumer, tenant);
        await db.SaveChangesAsync(ct);
    }
}
