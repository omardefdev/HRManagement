# MOB-001 — Mobile, Low-Bandwidth, and Interruption Resilience

## Purpose

Document mobile-first PWA strategy, performance budgets, resumable uploads, encrypted drafts, safe retry/idempotency, secure cache policy, offline field cases (after threat model), conflict resolution, and accessible touch UX.

## Scope

- Mobile-first PWA with installable manifest
- Network profiles for 3G-slow and intermittent connectivity
- Performance budgets (FCP, LCP, TTI, TBT, CLS, JS/CSS size)
- Resumable upload with chunking and idempotency keys
- Encrypted draft storage with max age
- Safe retry with exponential backoff; forbid double submit
- PII/token forbidden in cache; remote logout clears cache
- Offline field cases enabled only after SEC-003 threat model
- Device loss remote wipe; server-wins conflict resolution with review queue
- Touch UX: 44px tap targets, WCAG 2.2 AA

## Artifacts

| File | Purpose |
|------|---------|
| `docs/MOB_001_MOBILE.md` | This document |
| `docs/mob/performance-budgets.json` | PWA and performance budget structure |
| `scripts/mob001-validate.ps1` | Validates MOB-001 artifacts and structure |
| `Mob001MobileTests.cs` | Unit tests for mobile policy schema and validation |

## Critical Journeys

- Leave request
- Payslip view
- Attendance check-in
- Document upload

## Acceptance Criteria Mapping

- [x] Core journeys work on certified device/network profiles
- [x] Interruption does not duplicate submissions
- [x] PII/token cache policy defined
- [x] Remote logout/revocation clears client state
- [x] Accessible touch UX targets defined

## Validation

- `scripts/mob001-validate.ps1`
- `Mob001MobileTests`
