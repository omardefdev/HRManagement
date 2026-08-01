# OPS-006 — Clean-room rebuild runbook

**Card:** OPS-006  
**Use when:** Confirmed compromise, ransomware, or poisoned supply chain artifact.

## Preconditions

- [ ] Incident commander declared assumed-breach (OPS-005)
- [ ] Immutable artifact pipeline validated (`scripts/ops006-validate.ps1`)
- [ ] Clean-room network isolated from compromised segment
- [ ] Forensics snapshot taken (do not rebuild in-place on suspect hosts)

## Phase 1 — Trust reset (0–30 min)

1. Revoke all active sessions (Keycloak global logout + JWT blocklist).
2. Disable compromised CI tokens and rotate GitHub/org secrets.
3. Issue new KMS master keys; **do not** import old key material.
4. Notify national connectors to sandbox mode (`GovernmentConnectors.*.Sandbox=true`).

## Phase 2 — Bootstrap clean cluster (30 min–4 h)

1. Provision new K8s control plane on clean-room network.
2. Apply base manifests from **signed git tag** only (`git verify-tag`).
3. Deploy data layer from offline backup catalog (OPS-003) — verify checksums.
4. Apply Kyverno admission policies (BLD-001) before any workload deploy.
5. Deploy application images by **digest** from immutable registry mirror.

## Phase 3 — Credential and config reset (parallel)

| System | Action |
|---|---|
| PostgreSQL | New superuser + app roles; re-encrypt where applicable |
| RabbitMQ | New users/vhosts; reset MassTransit credentials |
| Keycloak | New realm admin; re-import clients from IaC |
| MinIO | New root keys; bucket policies from git |
| App secrets | External Secrets Operator sync from fresh KMS |

## Phase 4 — Data validation (4–8 h)

1. Row counts + control totals per tenant (EVD-001).
2. Payroll immutability spot-check (PAY-002).
3. Audit hash chain verification (OBS-003).
4. Reject restore if backup predates last known-good integrity checkpoint.

## Phase 5 — Resumption (8–24 h)

1. Enable read-only smoke tests on clean-room URL.
2. Progressive tenant cutover per REL-005 cohorts.
3. Post-incident review within 72 h; track actions to closure.

## Rollback

If validation fails: **stop** — do not attach clean-room to production DNS. Retain forensics and re-run from Phase 2 with alternate backup generation.

## Evidence checklist

- [ ] Artifact digest manifest archived
- [ ] Credential rotation log (no secret values)
- [ ] Reconciliation report signed by Finance + Security
- [ ] Connector sandbox ack logs (INT-004)
