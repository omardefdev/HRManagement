# PROD-001 — Tenant override

Agency overrides are allowed only within national mandatory ceiling.

## The agency ceiling (PLAN-48, D-TEN-8 = D-POL-4)

An agency inherits the national policy of a domain and adds only stricter rules in its override (`Scope = TenantOverride`):
`Deny`, `RequireApproval`, `RequireDualApproval`, `BlockUntilChecklist`, `RequirePrivacyMask`, `RequireOriginReceipt` (and
`RequireEvidence`). It may not change with `SetField` a field the national policy sets, add a `TriggerWorkflow` in a domain
where the national policy has one, or use `SelectCodePolicy`. An agency with no override receives the national decision.

- **At publish:** PLAN-47's `PolicyRuleValidator` refuses such a rule (`CEILING_SETFIELD`, `CEILING_TRIGGER_WORKFLOW`,
  `CEILING_SELECT_CODE_POLICY`), against the domain's national version in force. It runs when a rule is added to a draft, on the
  whole version at publish (a rollback included) and on every agency overlay pack.
- **At evaluation:** `PolicyCeiling.Apply` drops an agency effect that would weaken the national decision of this evaluation,
  and the decision says so: «أُهمل أثر الجهة بسقف الوطني (D-POL-4)» with the rule's key. This bounds overlays written before the
  check. `PolicyRuleDefinition.Origin` (`National` by default, compatible with the stored JSON) is `Agency` for every rule of an
  override.
- **Who writes what (D-TEN-10):** outside the national tenant a draft is always the agency's override, and a request for
  `Scope = National` answers 403 (page and `POST api/policies/drafts`); a write to a national template or draft from an agency is
  refused by `NationalSharedWriteGuard` (403). Inside the national tenant PLAN-47's `policy-author` drafts the national template
  (or the trial agency's override, on request).
- **Agency overlay packs** (`AgencyPolicySeeder`) put every rule of a domain into one draft of the agency's override, validate it
  with the ceiling, and publish it through `PolicyPublishService` as a system seed. A stored key is `{ruleKeyPrefix}:{ruleKey}`.

Impact on existing overlays: `reports/plan48-ceiling-impact.md`.
