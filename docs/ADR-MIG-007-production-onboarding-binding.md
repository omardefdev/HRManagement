# ADR-MIG-007 — Onboarding production binding (supersedes pack-only closure)

| Field | Value |
|---|---|
| Status | Accepted |
| Card | MIG-007 (card 72) |
| Date | 2026-07-30 |
| Supersedes | Agency onboarding gate pack as insufficient for production |
| Dependencies | MIG-006 production cutover binding, STR-004 |

## Context

The prior pack published the onboarding matrix, readiness/wave/nofork contracts, and a shape validate harness, but hosts did not load the matrix at startup and CI did not gate Staging/Production `MigOnboarding` binding.

## Decision

| Topic | Choice |
|---|---|
| Source of truth | `docs/mig-onboarding/onboarding-matrix.json` |
| Runtime | `MigOnboardingOptions` + `IMigOnboardingProvider` + `IAgencyOnboardingClaimGuard` |
| Readiness | 7 mandatory gates (tenant..support) |
| Nofork | `playbook.noforkRequired` |
| Wave | sign-offs required before wave |
| Owners | data / security / ops |
| Telemetry | leadTimeTargetDays > 0; defectBudget >= 0 |
| Sandbox | labStub + livePortalForbidden |
| Live onboard | Default `LiveDualAgencyOnboardClaim=false`; forbidden until MIG-007a–d |
| Soft blockers | MIG-006/STR-004 Satisfied; EXT-006/PROD-002 NotStarted; ONB-001 / agency-extract-access / live-dual-agency-onboard Blocked |
| Closure | **Done (production onboarding binding)** |

## Consequences

- Staging/Production fail startup on incomplete onboarding policy, dishonest blockers, or live dual-agency onboard claim.
- CI runs `mig007-onboarding-runtime-validate.ps1`.
- EXT-006 portal, PROD-002 self-admin, ONB-001 provision, and live dual-agency onboard remain out of scope.
