# PROD-001 — Versioning

Templates (policy/rule/workflow/form) must be versioned. Unversioned templates fail validation.

## PLAN-47: policy versions

- **A published version never changes.** `PublishedPolicyImmutabilityInterceptor` refuses, in the application, any change of a
  rule of a version that is not a draft and any change of a published version but `EffectiveTo`, `UpdatedAt`, `UpdatedBy`. The
  PostgreSQL triggers of `POL047_PolicyRuleLifecycle` (`policy_rules_guard`, `policy_versions_guard`, SQLSTATE `P0047`)
  refuse the same through raw SQL and `ExecuteUpdate`, for every role, the superuser included. Under forced row security the
  rule guard looks its version up as the invoking role and refuses a version it cannot see.
- **Drafts.** One draft per template (a filtered unique index). A draft starts as a copy of the effective version; its rules are
  added, changed and removed in place; a draft that will not be published is discarded (`Archived`). No empty version is
  published, except the explicit «withdraw every rule of the domain» draft with a written reason.
- **Strict rules.** Every added or changed rule, every version at publish and every seed change set passes
  `PolicyRuleValidator` (`When`: one of `all`/`any`/`not` or a leaf, depth ≤ 6, ≤ 50 leaves, nothing empty; eight operators;
  fields of the domain's facts schema; numbers in the invariant culture; `Then`: a non-empty array of known effects with their
  required parameters; key `^[A-Z][A-Z0-9:-]{2,99}$`, name ≤ 200, priority 1–1000). A stored rule the strict parser refuses
  matches nothing and is listed in `InvalidRuleKeys`; HR.Core logs the published rules the validator refuses at every start-up,
  in two parts: the national templates ("PLAN-47 validator inventory", with the number of templates it read) and, since PLAN-49,
  the agencies' overrides, each tenant in its own session ("PLAN-49 agency override inventory"). `scripts/pol047-validate.ps1`
  reads both parts of the last start-up and fails on a rule of an effective version, on a part that could not be taken, and
  on a national part that read no template.
- **Seed changes are versions.** `PolicySeedChangeSets` (append only) are applied once per template by
  `PolicySeedChangeSetApplier` and recorded in `PolicySeedApplications` with their digest; an applied set whose digest changed
  fails the boot. `POL-2026-10-30-BASELINE` holds what the old start-up patches added in place;
  `POL-2026-10-30-RETIRE-R-REC-HIRE` retires `R-REC-HIRE` with a new version. The writers that patched published versions are gone.
- **Publication.** One path (`PolicyPublishService` → `PolicyVersionPublisher`), which stages `IPolicyPublished` (domain,
  version, tenant, number, effective time) in the same save; two publications of one template race on the template's `xmin`
  and the loser gets 409 «نُشر إصدار آخر للتو؛ أعد المحاولة».
- **Proof:** `Pol047*Tests` (unit), `Pol047*PostgresTests` (integration), AC-POL-47a..g.
