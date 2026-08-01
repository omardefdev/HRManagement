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
