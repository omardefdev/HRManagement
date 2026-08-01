# ADR-STR-005 — Program governance production binding (supersedes pack-only closure)



| Field | Value |

|---|---|

| Status | Accepted |

| Card | STR-005 (card 63) |

| Date | 2026-07-30 |

| Supersedes | Governance gate pack closure as insufficient for production |

| Dependencies | STR-004 production nofork binding |



## Context



The prior pack published boards, decision rights, decision/risk logs, Go-Live fixtures, and a shape validate harness, but hosts did not load those assets at startup and CI did not gate Staging/Production `ProgramGovernance` binding.



## Decision



| Topic | Choice |

|---|---|

| Source of truth | `governance-matrix.json` + `decision-log.json` + `risk-register.json` |

| Runtime | `ProgramGovernanceOptions` + `IProgramGovernanceProvider` + `IGoLiveSeatGuard` |

| Go-Live | Default `GoLiveDeclared=false`; if true, multi-seat evidence required (`sponsor` + `security` + `architecture`) |

| Eng-only | Forbidden for Go-Live declaration |

| Soft blockers | SYD-017 remains **Blocked**; PGM-001 remains **NotStarted** |

| Closure | **Done (production governance binding)** |



Raising `GoLiveDeclared=true` requires board review plus multi-seat evidence. Human signatory names remain SYD-017.



## Consequences



- Staging/Production fail startup on expired Declared exceptions, broken soft-blocker honesty, critical owner==reviewer, or eng-only Go-Live evidence when declared.

- CI runs `str005-governance-runtime-validate.ps1`.

- Live PMO approval engine and program schedule remain out of scope (PGM-001).


