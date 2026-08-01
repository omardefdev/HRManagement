# SYR-013 — Branch, Governorate, Language & Accessibility UX

**Card:** SYR-013  
**Status:** Done  
**Priority:** P1-National / XL  
**Owner:** Product + Accessibility + Governorate Representatives

## Purpose

Define the Syrian branch and governorate user experience policy: Arabic RTL-first, names/numbers/dates/new-lira formatting, official language decisions (Kurdish/English), low-bandwidth resilience, modest devices, assisted service points, queue/resume, unified printing, and WCAG 2.2 AA compliance across all 14 governorates.

## Scope

- Arabic RTL-first with correct names, numbers, dates, and new Syrian lira (SYR-004)
- Language policy for Kurdish/English/other — official decision required, not assumed
- Low bandwidth, latency, and drop tolerance profiles
- Modest device support and assisted service points
- Queue/resume and encrypted draft save
- No sensitive data in browser cache
- Keyboard, screen reader, and contrast requirements
- Unified print/PDF templates
- WCAG 2.2 AA checklist for critical journeys
- Governorate UAT coverage (14 governorates or justified sample)

## Data Model

See `docs/syr/accessibility-wcag-checklist.json` for WCAG 2.2 AA criteria mapped to critical HR journeys and governorate network profiles.

## Business Rules

1. **Critical journeys work on approved connection profile** — throttled 3G lab minimum.
2. **No double submit after disconnect** — idempotent retry with duplicate-safe tokens.
3. **Draft save encrypted** — no plaintext sensitive data in localStorage/cache.
4. **Keyboard/screen reader/contrast pass** — WCAG 2.2 AA on all critical journeys.
5. **Correct Arabic documents** — RTL layout, proper numerals, new lira formatting.
6. **Governorate UAT signed** — representatives from 14 governorates or justified coverage sample.

## Relationship to PROD-004 / L10N-001 / UX-002

| Card | Focus |
|------|-------|
| PROD-004 | Remote branch production binding (lab contracts) |
| L10N-001 | Full i18n product (later) |
| UX-002 | Accessibility/responsive baseline Done |
| SYR-013 | Syria-specific branch UX, governorates, WCAG 2.2 AA |

## Acceptance Criteria Mapping

- [x] Arabic RTL-first with new lira and date formatting
- [x] Network profiles for low bandwidth/latency/drop
- [x] Encrypted draft save and no sensitive cache
- [x] WCAG 2.2 AA checklist for critical journeys
- [x] Queue/resume and duplicate-submit prevention
- [x] Governorate coverage matrix defined

## Artifacts

| File | Purpose |
|------|---------|
| `docs/SYR_013_BRANCH_UX.md` | This document |
| `docs/syr/accessibility-wcag-checklist.json` | WCAG 2.2 AA checklist for branch UX |
| `scripts/syr013-validate.ps1` | Validates SYR-013 artifacts |
| `Syr013BranchUxTests.cs` | Unit tests for accessibility checklist schema |

## Validation

- `scripts/syr013-validate.ps1`
- `Syr013BranchUxTests`
