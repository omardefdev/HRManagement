# ADR-STR-001 — National scope production binding (supersedes pack-only closure)

| Field | Value |
|---|---|
| Status | Accepted |
| Card | STR-001 (card 59) |
| Date | 2026-07-30 |
| Supersedes | Envelope gate pack closure as insufficient for production |

## Context

The prior **envelope pack** published `docs/str-scope/envelope-matrix.json` and validate harnesses but did not bind values into runtime configuration, release gates, or operational guards. Claims of national scale and deployment ceilings could diverge silently from NFR docs.

## Decision

| Topic | Choice |
|---|---|
| Source of truth | `docs/str-scope/envelope-matrix.json` (copied to app output as `NationalScope/envelope-matrix.json`) |
| Runtime | `NationalScopeOptions` + `INationalScopeProvider` in ServiceDefaults |
| Default Production/Staging envelope | `syria-interim` |
| National competition claim | Forbidden in Production unless `AllowNationalCompetitionClaim=true` (post CAP-001) |
| Guards | Soft/hard tenant + payroll window ceilings via `INationalScopeGuard` |
| Metrics | `HRManagement.NationalScope` gauges |
| Closure | **Done (production scope binding)** |
| Still blocked | SYR-002 official census; CAP-001 national load proof |

## Consequences

- Gateway, Payroll.Web, and HR.Core.Web call `AddNationalScope()` and fail fast on illegal Production national claims.
- Payroll calculation respects payroll window ceiling when `HardEnforceCeilings=true`.
- Pack scripts remain for structural matrix validation; `str001-envelope-runtime-validate.ps1` gates appsettings + NFR alignment.
