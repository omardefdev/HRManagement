# BLD-001 — Build provenance and admission policy

**Card:** BLD-001  
**Status:** Done (isolated build policy + Cosign admission + NuGet mirror placeholder)

## Scope

| Control | Implementation |
|---------|----------------|
| Ephemeral builders | GitHub Actions runners (no long-lived build secrets in images) |
| Dependency mirror | [`NuGet.config`](../NuGet.config) sovereign placeholder |
| SBOM + signing | `.github/workflows/deploy.yml` — CycloneDX + Cosign + provenance attestation |
| Admission policy | [`deploy/k8s/base/admission/require-signed-images.yaml`](../deploy/k8s/base/admission/require-signed-images.yaml) |
| Registry immutability | GHCR digest-pinned deploys per SUP-001 |

## Build → deploy traceability

```
git signed tag → CI build → SBOM + Cosign sign + SLSA attest → GHCR digest
     → cosign verify (deploy runner) → K8s deploy by digest → Kyverno admission
```

Every production image MUST map to:

- Source commit (signed tag)
- CI workflow run ID
- CycloneDX SBOM artifact
- Cosign signature + build provenance attestation

## NuGet sovereign mirror

Replace `SOVEREIGN_NUGET_MIRROR` in `NuGet.config` with the national artifact registry before air-gapped production builds. Until then, the config documents intent and package source mapping without blocking developer builds.

## Kyverno admission

1. Install Kyverno on the cluster.
2. Replace `PLACEHOLDER_REPLACE_BEFORE_PRODUCTION` in `require-signed-images.yaml` with the Cosign public key from SUP-001 key ceremony.
3. Apply via kustomize: `deploy/k8s/base/admission/` (included in base kustomization).

Unsigned or digest-mismatch images are **rejected** at Pod admission.

## Verification

```powershell
powershell -File scripts/bld001-validate.ps1
powershell -File scripts/bld001-verify-provenance.ps1   # requires cosign CLI + IMAGE env
dotnet test --filter Bld001BuildProvenanceTests
```

## Blockers (honest)

| Blocker | Status |
|---------|--------|
| Sovereign NuGet mirror live (SAN-001) | Blocked — placeholder URL |
| Cosign public key in admission policy | Blocked — replace placeholder before prod |
| Live forged-provenance exercise | Blocked — scheduled with PSIRT |

## Related

- SUP-001 supply chain (Cosign in deploy.yml)
- OPS-006 clean-room rebuild (consumes signed artifacts)
- SCM-001 signed tags and commits
