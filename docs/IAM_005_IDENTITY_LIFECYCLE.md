# IAM-005 — Identity Lifecycle and Cross-Agency Reconciliation

## Objective

Manage the full identity lifecycle — Joiner, Mover, Leaver (JML) — ensuring that transfers do not leave residual permissions, separations revoke all access within SLA, and feed outages never grant access by default.

## JML workflow

Machine-readable lifecycle: [`docs/iam/joiner-mover-leaver.json`](./iam/joiner-mover-leaver.json).

### Joiner

| Step | Owner | SLA |
|---|---|---|
| Create person record (or link existing golden record) | HR Core | At hire |
| Provision accounts (Keycloak realm, service accounts) | Identity Platform | ≤ 1 business day |
| Assign roles per position/grade/org-unit | SoD engine (IAM-003) | At provisioning |
| Verify no privilege overlap (separation of duties) | Compliance | Before activation |

### Mover (transfer / reorganization)

| Step | Owner | SLA |
|---|---|---|
| Revoke source-org roles and access | Identity Platform | ≤ 4 hours |
| Assign destination-org roles | Identity Platform | After source revocation |
| Reconcile residual permissions | Scheduled scan | Daily |
| Update golden record linkage | MDM-002 | At transfer |

**The branch scope on a transfer is fail-closed (PLAN-52, D-52-4).** When `ITransferExecuted` arrives, Auth.Directory clears the transferred user's `scope_org_unit_id`: locally (`AuthUser.ScopeOrgUnitId`, audited as `ScopeClearedOnTransfer` with the old scope, the units from and to, and the transfer), and, with `Keycloak:DirectorySync:Enabled`, in Keycloak too (the attribute is removed whatever the local value; a failure retries the message, so nothing is half done). With the sync off the token keeps the old scope until the sync runs: a warning and `directory_scope_sync_skipped_total` say so (PLAN-62 turns the sync on). The `branch-manager` role stays, so the user's scope is `InvalidClaim` → 403 everywhere a branch scope applies, never wider access.

**Assigning the new scope** is the directory administrator's step, after the transfer: set the user's scope to the destination unit (Auth.Directory's user screen, or `scripts/vps/add-demo-user.sh --scope-org-unit-id <unit>` for a demo user), then the user signs in again so his token carries it. Until then he keeps no branch reach. HR.Health's, HR.Contracts' and Admin.Logistics' employee snapshots follow the transfer on their own, so the new unit's records reach him once his scope is set. One receipt per transfer makes a redelivery a no-op; messages are not ordered, so a late first delivery of an older transfer is applied when it arrives.

### Leaver (separation / retirement / termination)

| Step | Owner | SLA |
|---|---|---|
| Disable interactive login | Identity Platform | ≤ 1 hour |
| Revoke API tokens / service credentials | Identity Platform | ≤ 1 hour |
| Archive and anonymize per retention policy | LEG-001 / PRV-001 | Per schedule |
| Orphan account scan | Reconciliation job | Weekly |

## Orphan and dormant account policy

- Dormant: no login for 90 days → alert and require re-verification.
- Orphan: no matching HR record → disable within 24 hours.
- Service accounts: named owner, reviewed quarterly.

## Feed outage handling

If the authoritative identity feed is unavailable:
- **No new grants** until feed is restored (fail-closed).
- Existing access continues per last-known-good state.
- Alert within 15 minutes of feed interruption.
- Manual override requires dual approval and EVD-001 evidence record.

## Validation

```powershell
scripts/iam005-validate.ps1
```

## Related cards

- IAM-003 (role management / SoD)
- IAM-004 (service identity)
- MDM-002 (golden person record)
- INT-003 (saga coordination)
- EXT-001 (identity federation)
