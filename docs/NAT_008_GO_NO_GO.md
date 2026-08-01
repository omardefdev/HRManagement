# NAT-008 — National Go/No-Go Decision & Certification Renewal

**Card:** NAT-008  
**Status:** Done (decision harness + evidence template)  
**Priority:** National Gate / M  
**Owner:** National Authorization Board

## Purpose

Define the national Go/No-Go decision record: review of NAT-001..007, open risks, funding, sovereignty, legal, capacity, wave plan, stop/rollback, public accountability, certification expiry, and continuous monitoring.

**This harness does not grant national ATO.** Live signed board decision remains Blocked until live NAT certifications complete.

## Scope

- Candidate release identity (version, commit, digests, SBOM, schema, config)
- NAT-001..007 harness review (live certs Blocked)
- Zero open P0/P1 and Severity-1/2 criteria
- Authority signatures: business, finance, legal, privacy, security, operations, audit
- Scope, release digest, agencies, systems, certification expiry
- Wave plan with stop/rollback and public accountability
- Annual recertification and recert on material change

## Data Model

See `docs/nat/go-no-go-decision.json` for structured Go/No-Go decision template.

Cross-reference: `docs/completion-backlog/11-NATIONAL-GO-NO-GO-CHECKLIST.md` for full evidence gate matrix.

## Business Rules

1. **All NAT harness packs present** — NAT-002..007 Done; NAT-001 NotStarted.
2. **No live decision without live certs** — board signatures Blocked until assessments complete.
3. **Release digest binding** — decision tied to exact artifact digests.
4. **Certification expiry tracked** — annual renewal and material-change recert required.
5. **Continuous monitoring** — post-Go control monitoring plan required.

## Relationship to NAT-001..007 / REL-007 / FIN-001 / SYR-017

| Card | Focus |
|------|-------|
| NAT-001..007 | Certification evidence packs |
| REL-007 | National wave rollout (NotStarted) |
| FIN-001 | Five-year TCO Done |
| SYR-017 | Syrian phased payroll launch references NAT-008 fields |

## Acceptance Criteria Mapping

- [x] Go/No-Go decision JSON with candidate identity slots
- [x] NAT cert review list and authority signature matrix
- [x] Certification expiry and continuous monitoring requirements
- [x] Wave plan stop/rollback and public accountability flags
- [x] Live board signed decision = Blocked

## Artifacts

| File | Purpose |
|------|---------|
| `docs/NAT_008_GO_NO_GO.md` | This document |
| `docs/nat/go-no-go-decision.json` | Go/No-Go decision template |
| `scripts/nat008-validate.ps1` | Validates NAT-008 artifacts |
| `Nat008GoNoGoTests.cs` | Unit tests for Go/No-Go schema |

## Validation

- `scripts/nat008-validate.ps1`
- `Nat008GoNoGoTests`

## Remaining Live Blockers

- NAT-001 evidence repository NotStarted
- Live independent certifications NAT-002..007 pending
- National authorization board signed minutes pending
