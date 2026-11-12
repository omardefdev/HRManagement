## PLAN-51 — an explicit policy for every action, and no wide paths or wide policies left (IAM-E)

**What changes**
- **Self-service:** the gateway decides `/ui/ess` and `/api/ess` by exact segment: the named controllers (`EssPortal`, `EssTalent`;
  Payroll `Ess`), GET static files, otherwise 404 before forwarding, audited `Denied:EssNotExposed`. No partial `/Ess` match is left.
- **The directory and internal routes:** `HrCore.Lookup` admits staff and services with `hr-core.lookup` only; no `api/internal` route
  accepts an interactive user.
- **The gateway's audit:** `Gateway.Audit` (`Gateway:Audit:Roles`: platform-admin all agencies, auditor and compliance their own) on the
  audit, operations and metrics pages, with the agency filter in the query service. The audit queue's copy no longer drops `TenantId`.
- **Every write names its policy:** the module filter has no name list; an unnamed write meets its module's `{Module}.Write`; 307
  actions named; sensitive actions on approval policies; roles from `Authorization:Policies:{name}:Roles` (D-51-2); D-IAM-4 in
  Production/Staging, off in demo mode; HR.Movements binds view models. Payroll's 16 wait for PLAN-53.
- **Service accounts and onboarding:** `hr-core.workflow` reaches three endpoints only; onboarding is `Platform.AgencyOnboarding`
  (platform-admin), `apply-file` deleted; evidence ingest takes `Evidence:IngestClients` (empty).
- **`HRManagement.ArchitectureTests`:** one project over the fifteen web assemblies; the lists only shrink and are empty except Payroll
  (PLAN-53) and the pinned entity bindings outside HR.Movements (IAM-F).

**Evidence**
- AC-IAM-51a..f (required, prod-like): this PR's Acceptance artifact is the evidence of record.
- Unit 6270/6270, architecture 131/131, integration 407; Testcontainers step 216 passed, 1 skipped; P-11 lab and least-privilege lists
  green; ci-local and the pre-push check green on 566b44ad.
- Demo server on `d0e446fe3280` (no migration, no realm change): the four PLAN-51 server checks 4/4; the PLAN-36..50 server suites
  44/45 plus the WF-46 sweeps rerun alone; smoke with mandatory wiring 50/0/1.

Report: `reports/plan51-final-report.md`. Progress and decisions: `reports/plan51-progress.md`.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
