# OPS-006 — Immutable artifact sources

**Card:** OPS-006  
**Purpose:** Define trusted, tamper-evident sources for clean-room rebuild outside blast radius.

## Pipeline definition

Authoritative JSON: [`ops006/immutable-artifact-pipeline.json`](ops006/immutable-artifact-pipeline.json)

## Trusted sources

| Source ID | Type | Trust anchor | Immutable |
|-----------|------|--------------|-----------|
| `git-signed-tag` | IaC | SCM-001 signed tag on `main` | Yes |
| `container-registry` | OCI | Cosign + SLSA provenance (BLD-001) | Yes |
| `sbom-artifacts` | CycloneDX | `.github/workflows/deploy.yml` CI output | Yes (90d retention) |
| `database-backup` | PostgreSQL | OPS-003 offline/WORM catalog | Yes (cross-check required) |
| `secrets-kms` | KMS | Fresh generation only — **no restore** | N/A |

## Clean-room rules

1. **Rebuild in-place forbidden** — new infrastructure segment only.
2. **Credential reuse forbidden** — see [OPS_006_CREDENTIAL_RESET.md](OPS_006_CREDENTIAL_RESET.md).
3. **Unsigned image deploy forbidden** — Kyverno admission (BLD-001).
4. **National connectors sandbox first** — INT-004 before production cutover.

## Pre-restore validation

Before any artifact is consumed in clean-room:

1. Verify Cosign signature against pinned public key (SUP-001).
2. Match image digest to SBOM component list.
3. Confirm git tag signature (`git verify-tag`).
4. Cross-check backup checksum against WORM catalog (OPS-003).
5. Reject backup generation timestamp after last known-good integrity checkpoint.

## Storage and retention

- SBOM + provenance: 90 days minimum in artifact store
- Signed digests: archived per release tag
- WORM backup catalog: per OPS-003 retention schedule
- Incident evidence manifest: SHA-256 of all consumed artifact digests

## Verification

```powershell
powershell -File scripts/ops006-validate.ps1
dotnet test --filter Ops006CyberRecoveryTests
```

## Blockers (honest)

| Blocker | Status |
|---------|--------|
| Live WORM catalog drill | Blocked — requires OPS-003 exercise |
| Live clean-room rebuild | Blocked — 2×/year national exercise |
