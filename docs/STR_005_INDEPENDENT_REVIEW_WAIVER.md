# STR-005 — Independent SYD-017 / PGM-001 review waiver



| Field | Value |

|---|---|

| Status | Active waiver |

| Card | STR-005 |

| Expires | When SYD-017 and PGM-001 are Accepted, or 2027-01-31 — whichever first |

| Owner | Program Board + Architecture |



## Waiver statement



Named Go-Live signatories (SYD-017) and a live program schedule / critical-path engine (PGM-001) are **deferred**. Engineering adopts role-placeholder multi-seat evidence and soft-blocker honesty in the matrix. This waiver does **not** authorize:



- Declaring national Go-Live with engineering-only seats

- Shipping Declared exceptions past `expiresOn`

- Claiming PMO interactive approvals or XL schedule baseline without PGM-001



## Conditions



1. Matrix blockers SYD-017=`Blocked` and PGM-001=`NotStarted` remain honest.

2. `ProgramGovernance:EnforceOnStartup` remains true in Staging/Production.

3. Default `GoLiveDeclared=false` until board-reviewed multi-seat evidence is ready.



## Sign-off slots



| Role | Acknowledgement |

|---|---|

| Sponsor | Role-only Go-Live seats acceptable pending SYD-017 |

| Architecture | Startup/CI binding accepted |

| Program Board | PGM-001 schedule deferred; pack binding sufficient for interim |


