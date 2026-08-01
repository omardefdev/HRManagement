# MIG-004 — Independent agency-extract / live-labelled-metrics / L10N-002 / matcher review waiver

| Field | Value |
|---|---|
| Status | Active waiver |
| Card | MIG-004 |
| Expires | When agency-extract-access, live-labelled-metrics, L10N-002, and MIG-004a–d are Accepted, or 2027-01-31 — whichever first |
| Owner | Data Quality + Privacy |

## Waiver statement

Live agency labelled evaluation (agency-extract-access / live-labelled-metrics), full Arabic normalization (L10N-002), and production matcher workers (MIG-004a–d) are **deferred**. Engineering adopts the match matrix as a startup/CI binding with lab labelled pairs and signed thresholds. This waiver does **not** authorize:

- Claiming a live identity matcher while `LiveIdentityMatchClaim` must stay false
- High-risk AutoMerge or AutoMerge without evidence
- Non-reversible merges or incomplete merge audit fields
- Cross-agency PII exposure in review queues
- `liveAgencyEval=true` while live-labelled-metrics is Blocked

## Conditions

1. Matrix blockers remain: MIG-002/DATA-002/GOV-001/GOV-003-merge=`Satisfied`, agency-extract-access=`Blocked`, live-labelled-metrics=`Blocked`, L10N-002=`NotStarted`.
2. `MigMatch:EnforceOnStartup` remains true in Staging/Production.
3. Default `LiveIdentityMatchClaim=false`.
4. Negative fixtures (`high-risk-auto-merge-fail`, `cross-tenant-review-fail`) remain in the harness.

## Sign-off slots

| Role | Acknowledgement |
|---|---|
| Data Quality | Lab match binding acceptable for interim |
| Privacy | Review privacy + signed thresholds accepted as gate |
| Migration Office | Matcher workers deferred to MIG-004a–d |
| Localization | Arabic normalization deferred with L10N-002 |
