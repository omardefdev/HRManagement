# OPS-006 — Credential and trust reset

**Card:** OPS-006  
**Use when:** Assumed breach, ransomware, or long-lived credential compromise.

## Principles

1. **Never restore** long-lived secrets from compromised backups or secret stores.
2. **Generate fresh** credentials on clean-room infrastructure only.
3. **Revoke first**, rotate second — block active sessions before issuing new material.
4. Document every rotation event (no secret values in logs).

## Reset order (strict)

| Step | System | Action | Owner |
|------|--------|--------|-------|
| 1 | Keycloak | Global logout + revoke all refresh tokens | IAM lead |
| 2 | Gateway JWT | Blocklist active JWTs; new signing keys in fresh KMS | Security |
| 3 | PostgreSQL | New superuser + app roles; rotate connection strings | DBA |
| 4 | RabbitMQ | New users/vhosts; reset MassTransit credentials | Platform |
| 5 | MinIO | New root keys + per-service access keys | Storage |
| 6 | Keycloak admin | New realm admin; re-import clients from IaC | IAM |
| 7 | CI/CD | Revoke GitHub tokens; rotate org secrets | DevOps |
| 8 | External Secrets | ESO sync from fresh KMS only | Platform |
| 9 | Service identity | Rotate `SVC_*` client secrets per SEC-004 | App teams |
| 10 | National connectors | Sandbox mode until INT-004 validation | Integration |

## Forbidden actions

- Importing old KMS master keys
- Reusing MinIO root credentials from backup
- Restoring Keycloak realm export that contains old admin passwords
- Deploying workloads before admission policy (BLD-001) is active

## Verification checklist

- [ ] All Tier-0 services authenticate with post-reset credentials
- [ ] No active sessions predating trust-reset timestamp
- [ ] Rotation log archived (redacted) under incident evidence
- [ ] `scripts/ops006-validate.ps1` passes
- [ ] Spot-check: attempt old credential — must fail closed

## Related

- [OPS_006_CYBER_RECOVERY.md](OPS_006_CYBER_RECOVERY.md)
- [OPS_006_CLEANROOM_RUNBOOK.md](OPS_006_CLEANROOM_RUNBOOK.md)
- [SEC_004_SECRET_ROTATION.md](SEC_004_SECRET_ROTATION.md)
- SOV-003 key sovereignty binding
