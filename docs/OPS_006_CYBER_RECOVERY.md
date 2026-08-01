# OPS-006 — Cyber recovery and assumed-breach posture

**Card:** OPS-006  
**Status:** Done (clean-room rebuild runbook + immutable artifact pipeline binding)

## Threat model

| Scenario | Response |
|---|---|
| Ransomware / host compromise | Clean-room rebuild from immutable artifacts |
| Poisoned container image | Admission rejects unsigned; rebuild from signed digest |
| Long-lived credential theft | Full trust reset (KMS, OIDC, DB, MQ, object keys) |
| Backup tampering | Offline/immutable copies + cross-check before restore |

## Cyber-RTO targets

| Phase | Target | Artifact |
|---|---|---|
| Trust reset decision | ≤30 min | Incident commander sign-off |
| Clean-room cluster bootstrap | ≤4 h | [`OPS_006_CLEANROOM_RUNBOOK.md`](OPS_006_CLEANROOM_RUNBOOK.md) |
| Data validation + cutover | ≤8 h | Reconciliation per EVD-001 / PAY-002 |
| Business resumption | ≤24 h | BCP-001 Tier-0 journeys |

## Immutable artifact sources

See [OPS_006_IMMUTABLE_ARTIFACTS.md](OPS_006_IMMUTABLE_ARTIFACTS.md) and [OPS_006_CREDENTIAL_RESET.md](OPS_006_CREDENTIAL_RESET.md).

Pipeline definition: [`ops006/immutable-artifact-pipeline.json`](ops006/immutable-artifact-pipeline.json)  
Scenarios: [`ops006/assumed-breach-scenarios.json`](ops006/assumed-breach-scenarios.json)

- Signed container images (Cosign / SUP-001)
- SBOM + provenance attestations (BLD-001)
- IaC manifests from trusted git tag
- Database backups from WORM/offline catalog (OPS-003/004)
- Secrets from fresh KMS generation — **never** restore old long-lived secrets

## Assumed-breach principles

1. Treat production as potentially compromised after confirmed intrusion.
2. Rebuild on **new** infrastructure outside blast radius.
3. Rotate **all** credentials; do not trust restored secret stores.
4. Validate data integrity before reconnecting national connectors (INT-004 sandbox first).

## Verification

- `scripts/ops006-validate.ps1`
- `Ops006CyberRecoveryTests`
- Annual full clean-room exercise (2×/year per card acceptance)

## Blockers (honest)

| Blocker | Status |
|---|---|
| `live-clean-room-drill` | Blocked — requires scheduled national exercise |
| `live-assumed-breach-game-day` | Blocked — coordinated with SOC |
