# ADR-STR-002 — Legal control production binding (supersedes pack-only closure)

| Field | Value |
|---|---|
| Status | Accepted |
| Card | STR-002 (card 60) |
| Date | 2026-07-30 |
| Supersedes | Legal control catalog pack closure as insufficient for production |
| Dependencies | STR-001 production scope binding; GOV-001; GOV-002 |

## Context

The prior **catalog pack** published `docs/str-legal/control-catalog.json` and structural validate harnesses but did not bind controls into runtime configuration, release gates, or payroll start guards. `LEGAL_TBD` was gated for payroll sources only; Partial rows without Active exceptions could ship silently; legislative watch was documentation-only.

## Decision

| Topic | Choice |
|---|---|
| Source of truth | `docs/str-legal/control-catalog.json` (copied to app output as `LegalControl/control-catalog.json`) |
| Runtime | `LegalControlOptions` + `ILegalControlCatalogProvider` + `ILegalControlGuard` in ServiceDefaults |
| Default Production/Staging profile | `Interim` with `EnforceOnStartup=true` |
| National claim | Forbidden while critical-domain `Blocked-Legal` remain unless `AllowBlockedLegalNationalClaim=true` |
| Partial | Requires Active `exceptionId` |
| Implemented | Must not reference `LEGAL_TBD` |
| Legislative watch | `legislative-watch-stamp.json`; overdue fails Production startup |
| Payroll | `IPayrollLegalControlGuard` on `StartPayrollRunJobHandler` |
| Closure | **Done (production control binding)** |
| Still blocked | SYR-001 corpus; live SOV-001 ATO (SOV-001 baseline binding Satisfied); SYD-001/004/008/009/011 OPEN-NO-GO |

## Consequences

- Gateway, Payroll.Web, and HR.Core.Web call `AddLegalControl()` and fail fast on illegal Production catalog states.
- CI runs `str002-control-runtime-validate.ps1` and extended `check-no-legal-tbd.py`.
- Pack scripts remain for structural catalog validation.
- This binding does **not** certify legislation, MoF tables, privacy licenses, or national ATO.
