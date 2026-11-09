# PLAN-48 — what the agency ceiling changes (D-TEN-8 = D-POL-4)

Written in P4, before the ceiling was switched on (prompt P-6). Sources: the acceptance stack left by PLAN-47's run, read in P0
(`artifacts/plan48/baseline-acceptance-policies.txt`, not committed), the seed packs, and the demo server's read-only P0 picture
(`artifacts/plan48/server-p0.txt`, not committed).

## The rule

An agency inherits the national policy and only adds stricter rules. It may add `Deny`, `RequireApproval`,
`RequireDualApproval`, `BlockUntilChecklist`, `RequirePrivacyMask`, `RequireOriginReceipt` (and `RequireEvidence`, which only
adds a constraint). It may not:

- change with `SetField` a field the national policy sets;
- add a `TriggerWorkflow` where the national policy has one;
- use `SelectCodePolicy`.

The ceiling applies twice:

- **At publish** (`PolicyRuleValidator`, through `PolicyRuleValidationService` for a `TenantOverride` template): the rule is
  refused with `CEILING_SETFIELD`, `CEILING_TRIGGER_WORKFLOW` or `CEILING_SELECT_CODE_POLICY`. The same check runs when a rule is
  added to a draft and on every agency overlay pack.
- **At evaluation** (`PolicyCeiling.Apply` in `PolicyEvaluator.Evaluate`): an agency effect that would weaken the national
  decision of this evaluation is dropped, and the decision carries «أُهمل أثر الجهة بسقف الوطني (D-POL-4)» with the rule's key.
  This covers the overlays written before the check existed.

## Before this plan, an agency did not see the national policy at all

`PolicyOrchestrator.LoadPublishedAsync` read the national template through the agency's EF filter, so in an agency session the
national rules were never loaded (G-POL-05). An agency with an override was decided by its override alone. An agency without one
fell back to `PolicyStaticFallback`. From P4 the national template is read through `NationalOnly` and merged first. So the
first change in every agency is the national rules themselves; the ceiling only bounds what the agency adds on top.

## Acceptance data (agencies A, B, C)

The acceptance seed (`AcceptanceDataSeeder`, PLAN-47's PLAN-29 data) gives every agency an `Assets.Custody` override. The table
is the data as P0 found it; the change P4 makes to it follows the table.

| Overlay (agency, version) | Rule | Effect | Under the ceiling | What the agency's decision becomes |
|---------------------------|------|--------|-------------------|------------------------------------|
| A, B, C — v2 Published (s2s42's patch adds v4 with the same rule) | `ACC-R-PRIV-EVIDENCE` (10) | RequireEvidence | allowed | unchanged |
| A, B, C — v2 Published; s2s42 v4 | `ACC-R-PRIV-WORKFLOW` (20) | TriggerWorkflow `ASSET-ASSIGN-APPROVAL` | **refused at publish** (the national `R-PRIV-WF` and `R-HIGH-APPROVAL` trigger a workflow in the domain); **dropped at evaluation** whenever a national TriggerWorkflow fires | Same workflow: the national `R-PRIV-WF` (15) triggers `ASSET-ASSIGN-APPROVAL` on the same condition (`isPrivileged = true`). The decision gains one explanation line naming `ACC-R-PRIV-WORKFLOW`. |
| A — v3 (published by AcPol47); B, C — v3 Draft | `ACC-R-PRIV-DENY` (5) | Deny | allowed | unchanged (Deny wins) |
| s2s42 patch — v4 | `ACC-S2S42-DENY` | Deny | allowed | unchanged |

**What the national rules add in the agencies** (the G-POL-05 fix, not the ceiling), for `Assets.Custody`:

- `R-PRIV-CODE` (12, `action = Create` and privileged): `SelectCodePolicy ASSET-PRIVILEGED`. A privileged asset created in an
  agency now gets a `VIP-YYYY-######` tag from its own counter (AC-TEN-48g), not `AH-…`.
- `R-HIGH-APPROVAL` (20, `value > 10000`): `TriggerWorkflow ASSET-ASSIGN-APPROVAL` for high-value assets that are not
  privileged.
- `R-CLASSIFY-HIGH-VALUE` (5) and `R-CLASSIFY-CAPITAL` (6), on `action = Classify`: `SetField isPrivileged = true`.
- `R-PRIV-EVIDENCE` (10): RequireEvidence (the agency's own rule already required it).

The other national domains now apply in the agencies as they do in the national tenant (for example `Contracts.Leave`'s
`R-LEAVE-SELF` and `R-LEAVE-MAX` denials, `Core.Establishment`'s `R-EST-DENY-BRANCH`).

`Core.Recruitment` in an agency used `PolicyStaticFallback`, which mirrors the national rules for requisition submit
(`RECRUITMENT-REQUISITION-APPROVAL`), publish (approved and vacant only), offer issue (`RECRUITMENT-OFFER-APPROVAL`, or
`RECRUITMENT-OFFER-OVERRIDE-APPROVAL` for an exception or a merit override) and hire (an accepted offer). Those decisions keep
their workflow codes. What is new for an agency: a committee submission (`R-COMMITTEE-SUBMIT`) starts
`RECRUITMENT-SELECTION-COMMITTEE`, an appeal (`R-APPEAL-FILE`) starts `RECRUITMENT-APPEAL-REVIEW` (the fallback allowed both
with no workflow), and `R-REC-ORG` denies a hire with no organisational scope (`hasOrgScope = false`).

### What P4 changes in the acceptance seed (P-8: data that relied on an agency overriding the national rule)

`ACC-R-PRIV-WORKFLOW` was a stand-in for the national `R-PRIV-WF`, which agencies could not see before this plan. AC-POL-47
(required, P-15) publishes copies of agency A's v2 (47a, 47c) and rolls back to it (47d). Under the ceiling every one of those
publications would be refused for `ACC-R-PRIV-WORKFLOW`. So:

- **agency A's v2 holds `ACC-R-PRIV-EVIDENCE` only.** Its privileged assets still start `ASSET-ASSIGN-APPROVAL`, now from the
  national `R-PRIV-WF`, and AC-POL-47 runs unchanged;
- **agencies B and C keep the layer written before the ceiling.** Their v2 holds `ACC-R-PRIV-WORKFLOW` plus two more rules the
  ceiling bounds: `ACC-R-OLD-FIRST-WF`, a lighter workflow (`ASSET-LOSS-APPROVAL`) at priority 1, and `ACC-R-OLD-UNPRIV`,
  `SetField isPrivileged = false` on the national `R-CLASSIFY-HIGH-VALUE` condition. The evaluator drops all three whenever the
  national rule fires, which is always on their conditions, so no decision changes. A copy of the layer is refused at publish
  (AC-TEN-48e).

The acceptance classes that read an agency decision closely (`AcWf45`, `Ac32`, `Ac33`, `Ac10`, `Ac11`, `AcPol47`, the
recruitment journeys and `AcHcm30`) are in P5's list (P-10).

## Seed packs

| Pack | Rule | Domain | Effect | Under the ceiling |
|------|------|--------|--------|-------------------|
| `policy-ministry-finance-v1` | `R-FIN-LEAVE-NOTE` | Contracts.Leave | RequireEvidence | allowed |

The pack's stored key was `policy-ministry-finance-v1:R-FIN-LEAVE-NOTE`, which fails PLAN-47's key pattern
`^[A-Z][A-Z0-9:-]{2,99}$`. Renamed in the pack data: `"ruleKeyPrefix": "MOF"` makes it `MOF:R-FIN-LEAVE-NOTE`, and
`"renamedFrom"` keeps the old key, so an agency provisioned under it is not given the rule twice.

Found while routing the pack through the publish service: `AgencyPolicySeeder` read the pack case-sensitively, so the
camelCase names never bound and **no overlay rule was ever added** (and a domain would have received its first rule only). An
agency provisioned before this plan has no `policy-ministry-finance-v1` rule, under either key.

## Demo server

Tenants: PLATFORM only (P0). Policy templates: 20, all national (`Scope = National`, PLATFORM); no `TenantOverride` template and
no agency. **The ceiling changes nothing there.** P7 checks this again, read-only, before the deploy.
