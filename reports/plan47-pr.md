## PLAN-47: the policy rule lifecycle

Four eyes on every publish, rules validated before they are saved, published versions that never change, effective dating and rollback. The national policy engine's history is now append-only.

- **Four eyes and roles** (`ServiceDefaults`, `HR.Core.Web`):
  - policies `Policy.Author`, `Policy.Approver` and `Policy.Reader`, with roles from `PolicyEngine:Roles:*`; realm roles `policy-author` and `policy-approver`;
  - nobody publishes a version they created or edited (`PolicyVersionEdits`); the Lab flag is gone;
  - publishing carries the revision the approver reviewed; a draft changed since then gets 409.
- **Validation** (`SharedKernel/Policy/PolicyRuleJson`, `PolicyRuleValidator`):
  - strict When/Then parsing, known fields, operators and effects, invariant-culture numbers, depth and size limits;
  - checked on every rule change and on the whole version at publish;
  - a malformed stored rule matches nothing and is logged (`PLAN-47 validator inventory`).
- **Immutability:**
  - an EF interceptor and PostgreSQL triggers (`policy_versions_guard`, `policy_rules_guard`, SQLSTATE P0047) refuse any change to a published version, through the app or SQL;
  - seed changes are recorded change sets (`PolicySeedChangeSets`, `PolicySeedApplications`) applied as new versions under an advisory lock; R-REC-HIRE is retired by a version.
- **Drafts, effective dating and rollback:**
  - a draft is a copy of the effective version; rules can be added, changed and removed; one draft per template;
  - a version can take effect later; evaluation runs as of a date (`EvaluatedAsOf` in the ledger); only "now" is cached;
  - rollback is a new four-eyes version linked to its target, with a reason.
- **One publish path:** `PolicyPublishService` sends `IPolicyPublished` through the outbox in the same transaction; every HR.Core instance clears its cache on its own temporary queue.
- **Tests:**
  - unit (`Pol047*`, 97 tests);
  - PostgreSQL (immutability as a non-superuser under forced row security, concurrent publish, the seed applier on three boots, the two-bus outbox);
  - AC-POL-47a..g on the prod-like stack, plus AC-10/11 on the lab stack and the regressions of PLAN-25 and PLAN-36..46.
- **Demo server:** runs this code. The 20 earlier published versions are unchanged; a four-eyes publish and a rollback were made with the new demo author. The PLAN-36..47 server checks pass 33/33.

Details: `reports/plan47-final-report.md`.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
