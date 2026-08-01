# MIG-006 — Program adoption minutes (cutover production binding)

| Field | Value |
|---|---|
| Card | MIG-006 (card 71) |
| Date | 2026-07-30 |
| Decision | Bind cutover-matrix into host startup and CI |
| Default live cutover | `LiveAgencyCutoverClaim=false` |

## Operational assets

| Asset | Path |
|---|---|
| Matrix | `docs/mig-cutover/cutover-matrix.json` (copied to `MigCutover/`) |
| Negative fixtures | delta-loss-fail, paper-rollback-fail |
| Positive fixtures | timed-rehearsal-pass, delta-capture-pass, surprise-rollback-pass, go-nogo-decision, legacy-readonly-pass, recon-t0/t1/t30, cutover-run-ledger |

## Claim adoption rules

| Claim / coverage | Requires |
|---|---|
| `LiveAgencyCutoverClaim=true` | MIG-006a–d Accepted |
| Rehearsal at agency scale | live-rehearsal-at-scale + live-cutover-perf Satisfied |
| Production DNS flip | MIG-006b (+ `liveForbiddenInThisPack` lifted) |
| Live post-cutover recon | live-agency-recon Satisfied (+ MIG-005) |

Until then: lab harness only; live cutover claim stays false; soft blockers remain honest.

## Signatories (roles)

| Role | Name / org | Signature / date |
|---|---|---|
| Cutover Command | _pending wet-ink_ | |
| Internal Audit | _pending wet-ink_ | |
| Migration Office | _pending wet-ink_ | |

## Independent review

See [MIG_006_INDEPENDENT_REVIEW_WAIVER.md](MIG_006_INDEPENDENT_REVIEW_WAIVER.md).
