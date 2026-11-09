## PLAN-48: national settings for every agency, the agency ceiling, and sound numbering in every tenant

**What changes**
- **One national read path.** The ten national tables are read only through `OwnOrNational`/`NationalOnly` (20 general bypasses removed). Under forced row security a SELECT-only policy, `national_shared_read`, is applied and checked on both migration paths and by the VPS bootstrap. `NationalSharedWriteGuard` refuses a national row written outside the national tenant (403).
- **Numbers keep their prefix in every tenant.**
  - Code policies resolve overlay → national → none.
  - `MissingPolicy=Refuse` outside Development and Testing: 422, never a number without its prefix.
  - Missing facts answer 422, collisions 409.
  - Each policy passes a validator.
  - FundedPosition and RecruitmentRequisition were corrected once on existing databases (`ten048-code-policy-upgrade-v1`).
  - Counters were reseeded per tenant and never lowered.
  - The cost centre is derived.
- **The agency ceiling (D-POL-4).** An agency inherits the national policy and only adds stricter rules. A weaker rule is refused when added and at publish, and dropped at evaluation with an explanation. There is no national draft inside an agency (403). Agency overlay packs go through the validator and the publish service.
- **Tests.**
  - AC-TEN-48 (a..g on the prod-like stack, plus a least-privilege class as `hr_core_app`) is new and required.
  - AC-13 was rebuilt: 120 parallel generations in two tenants and three kinds, with real DB checks. It is now required.
  - `Ten048NationalRlsIntegrationTests` and the upgrade test run as a non-owner role under FORCE.

**Evidence**
- Local: unit 5758/5758, the Testcontainers subsets, and the prod-like, lab and least-privilege acceptance lists are green (`reports/plan48-progress.md`). The evidence of record is this PR's Acceptance artifact.
- One independent review plus an adversarial verification workflow. Their findings are fixed or recorded (`reports/plan48-final-report.md` §9-10).

**Demo server** (already on `8abd5aa7fe93`, the same product code)
- Backup: `/opt/hrm/backups/pre-plan48-20261109T194711Z`.
- The read policy is applied by the bootstrap. The upgrade is recorded once and no counter went down. New requisitions are `REQ-2026-00016/17`, and there are 0 prefixless numbers.
- Smoke 50/0; the server suites are green.
- Rollback note: roll forward (the SQL for a rollback is in `docs/OPS_VPS_SINGLE_HOST.md`).

**Ceiling impact:** `reports/plan48-ceiling-impact.md`.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
