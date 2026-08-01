# Staging validation — release readiness

**Date:** 2026-05-29

## Automated checks (CI / local)

| Check | Command | Status |
|-------|---------|--------|
| Build | `dotnet build HRManagement.slnx -c Release` | Run in CI on every PR |
| Unit tests | `dotnet test src/Tests/HRManagement.UnitTests/...` | 429 tests |
| Integration tests | `dotnet test src/Tests/HRManagement.IntegrationTests/... --filter "Category!=Testcontainers"` | Includes `EndToEndReleaseWorkflowSmokeIntegrationTests` |
| Release workflow smoke | `ReleaseWorkflow_Contract_Leave_PayrollPost_Separation_ChainSucceeds` | Contract → leave → payroll post → separation |
| Docker build | `docker compose build` | CI |

## Full-stack staging (Docker)

```powershell
docker compose -f docker-compose.yml -f docker-compose.services.yml up -d --wait
pwsh scripts/verify-staging-stack.ps1
```

Expected: gateway `/health/live` 200; upstream probes 200 when all containers healthy.

## Auth & UI E2E (Playwright)

```powershell
$env:E2E_KEYCLOAK_USER = "hr-admin-user"   # Keycloak test user with hr-admin role
$env:E2E_KEYCLOAK_PASSWORD = "<secret>"
pwsh scripts/run-staging-e2e.ps1
```

Covers:

- Login redirect smoke
- `/health/live` smoke
- SSO across `/ui/hr-core` and `/ui/payroll` (requires credentials)
- Logout session clear
- Upstream health for all 13 modules
- UI paths return non-5xx when unauthenticated (challenge/redirect)

Manual items remain in [UI_CHECKLIST.md](./UI_CHECKLIST.md); record results in [UI_CHECKLIST_RESULTS.md](./UI_CHECKLIST_RESULTS.md).

## Workflow smoke (integration — no Docker)

Validates cross-service assumptions for:

1. `IContractActivated` → Payroll pay profile
2. `ILeaveApproved` → Payroll consumer
3. Payroll run calculate (full post in `PayrollRunLifecycleIntegrationTests`; staging manual for post)
4. `ISeparationCompleted` → pay profile deactivated

Test class: `EndToEndReleaseWorkflowSmokeIntegrationTests`.

## Staging sign-off checklist

- [ ] `pwsh scripts/rel001-parity-diff.ps1` passes (REL-001)
- [ ] `verify-staging-stack.ps1` passes
- [ ] Playwright E2E passes with Keycloak test user
- [ ] Manual UI_CHECKLIST SSO and 403 items checked
- [ ] RabbitMQ management UI: no unexpected `_error` depth
- [ ] Product scope acknowledged ([RELEASE_PRODUCT_SCOPE.md](./RELEASE_PRODUCT_SCOPE.md))
- [ ] REL-001 rehearsal: [REL_001_REHEARSAL_CHECKLIST.md](./REL_001_REHEARSAL_CHECKLIST.md) / [REL_001_UAT_PACK.md](./REL_001_UAT_PACK.md)
