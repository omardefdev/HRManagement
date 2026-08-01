# HARD-001 — Verification

## Automated

| Check | Location |
|---|---|
| Hardening baseline docs present | `Hard001HardeningTests` |
| Exception register format | `Hard001HardeningTests` |
| PSA / security context K8s artifacts | `Hard001HardeningTests` |
| Compose security_opt / cap_drop | `scripts/hard001-compliance-scan.ps1` |
| CI gate | `.github/workflows/hardening-check.yml` |

## Compliance scan (local)

```powershell
pwsh -NoProfile -File scripts/hard001-compliance-scan.ps1
```

Exit code `0` = all checks pass.

## Acceptance criteria mapping

| Criterion | Evidence |
|---|---|
| 100% assets have baseline | Docs under `docs/hardening/HARD_001_*.md` per asset class |
| Zero Critical drift (IaC) | Compose + K8s manifests in repo; compliance scan in CI |
| Unnecessary services disabled | SEC-001 network matrix; production compose port closures |
| Exceptions temporary & reviewed | `HARD_001_EXCEPTION_REGISTER.md` + optional JSON |
| Container hardening | `docker-compose.production.yml` anchors; K8s security context defaults |
| K8s PSA restricted | `deploy/k8s/base/pod-security/restricted-psa.yaml` |

## Manual drills

1. **Unauthorized change** — attempt compose override without PR → branch protection blocks merge.
2. **Compromised container simulation** — verify `no-new-privileges` and `cap_drop ALL` on app container (inspect `docker inspect`).
3. **Exception expiry** — add expired test row to JSON (dev only) → scan fails.

## Pre-release checklist

- [ ] `hard001-compliance-scan.ps1` passes
- [ ] No Active exceptions past expiry
- [ ] Infrastructure mild-hardening comments reviewed if image upgraded
- [ ] K8s overlay diff includes no PSA label removal
