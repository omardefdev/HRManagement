# PLAN-67 final report — a readable Arabic interface, one content security policy per page, and a clean demo (UX-A)

Plan: [PLAN-67-ux-readable-arabic-interface.md](../docs/acceptance-plans/PLAN-67-ux-readable-arabic-interface.md).
Prompt: [PLAN-67-AGENT-PROMPT.md](../docs/acceptance-plans/execution/PLAN-67-AGENT-PROMPT.md). Study: [UX_004_INTERFACE_GAP_STUDY.md](../docs/UX_004_INTERFACE_GAP_STUDY.md).
Progress and every stamp: [plan67-progress.md](plan67-progress.md). Baseline (the CSP cause on the demo, the inventory, the demo's residue): [plan67-baseline.md](plan67-baseline.md).

## 1. Summary per phase

| Phase | What it did | Finished (UTC) |
|-------|-------------|----------------|
| P0 | Preconditions (blocked twice while PLAN-52 was still merging, then green: `main` = `origin/main` = `aa8be851`, SSH through the VPN, tag `92a69960c826` on the demo); build 0/0; the cause of G-UX-01 proved on the demo (two CSP headers on every forwarded page); the inventory by the guard's own scanner (652 views); «before» screenshots on the demo and locally; the demo's «server check» residue, read-only | 2026-11-13T16:27:52Z |
| P1 | Security headers written once, in `Response.OnStarting`, each only if absent (D-67-1); ten `confirm()` handlers become `data-hr-confirm`; 46 `style` attributes become classes; `Security__CspMode=Enforce` on the 15 apps of the prod-like and E2E stacks | 2026-11-13T16:29:33Z |
| P2 | `InterfaceTextGuardTests` over every view, its per-project lists that only shrink (649 rows seeded), planted defects for every rule | 2026-11-13T16:30:47Z |
| P3 | `HrLocalTime` (Asia/Damascus from `Localization:TimeZone`, one date and one time format), the display and input helpers, the `datetime-local` tag helper and model binder; 423 time rows, 61 raw statuses and 41 English labels to zero; Arabic names for the 20 policy domains and the policy enums | 2026-11-13T17:01:15Z |
| P4 | The gateway's names registry (`gateway_user_names`) and `POST api/user-names/by-ids`; `HrUserName`, `display-names.js`; 88 printed ids and 29 empty tables to zero | 2026-11-13T17:13:41Z |
| P5 | `PolicyRuleReadableRenderer`: every rule as an Arabic sentence; the simulation as a form; JSON behind «عرض تقني» for the D-67-4 roles; the last 7 JSON rows to zero | 2026-11-13T17:21:54Z |
| P6 | The portal at `/careers` alone (`[PublicPortalOnly]`); the server checks write Arabic text, their markers in fields staff do not see; every guard list empty and deleted | 2026-11-13T17:26:55Z |
| P7 | AC-UX-67a..f on the prod-like stack under `Enforce`, 7/7; `ux067-validate.ps1`; the independent review (two passes, 15 findings: 1 high, 2 medium, the rest low or test-only; all fixed but one accepted as designed); P-15's stages; the full integration suite brought back under EF's provider limit. Stopped 23 h 43 min for drive C and 3 min for the network | 2026-11-15T00:34:20Z |
| P8 | `origin/main` had not moved (`aa8be851`): no merge | 2026-11-15T00:34:40Z |
| P9 | SSH blocked at its start (00:39Z, `BLOCKED:`); resumed at 06:34Z through ProtonVPN. Verified backup; build attempt 1 stopped on a full server disk (the demo untouched), 56 GB freed, attempt 2 17/17; deploy on the first attempt (the gateway's migration); the residue posting closed through the application; `AcUx67VpsChecks` 5/5 (the CSP check now makes the approval and the draft the demo lacks); the server suites 48/49 + the WF-46 sweeps rerun 1/1; final smoke 50/0/1 | 2026-11-15T08:45:36Z |
| P10 | Docs, this report, local gates, the release branch, the PR and its merge | in the merge note |

## 2. Commits

- `f10933f8` docs(iam052): merge note for PR 50
- `672b9490` docs(ux067): the study UX-004, PLAN-67 and its prompt; PLAN-52's PR body
- `8c3c287a` chore(ux067): baseline, the CSP cause on the demo, the interface inventory, before screenshots, the demo's residue
- `3ed23cb2` fix(ux067): one content security policy per response; confirmations and reviewer comments without inline code; style attributes become classes; the prod-like and E2E stacks enforce the CSP
- `fb9f8dcc` test(ux067): a guard over every view for English labels, raw statuses, ids, JSON, inline code, empty tables and unformatted times, with lists that only shrink
- `0a1abee5` feat(ux067): Arabic statuses, labels and policy domains; every time in Damascus time and one format
- `ee058c98` feat(ux067): people and records by name, from a gateway registry of signed-in users; empty tables say so
- `0c4cc6ad` feat(ux067): policy rules read as Arabic sentences, the simulation takes a form, and the JSON stays behind a technical view
- `63633f57` feat(ux067): the public portal lives at /careers alone; server checks leave no public residue; the guard holds no rows
- `1ee455bf` test(ux067): AC-UX-67a..f pass on the prod-like stack under an enforced CSP; review fixes; CI scope and ownership updated
- `3579161f` test(ux067): demo server with one CSP per page, a styled portal at /careers, Arabic pages and readable rules; PLAN-36..52 server checks still green
- this report's commit: docs(ux067): one policy per response, the guard, Damascus time, the names registry and the portal in the docs; the study's status lines; the plan's status and notes; the final report

## 3. Decisions applied and deviations

**Decisions (D-67-1..7) as applied:**
- **D-67-1:** the service that renders a page owns its policy. `UseHrSecurityHeaders` writes every security header in `Response.OnStarting`, and each only if the response does not already carry it, so a forwarded page keeps its service's policy and nonce, and a gateway page (or a forwarded response without headers) gets the gateway's. The policy itself is unchanged: no `unsafe-*`, no wildcard, no new source. An enforcing host promotes a lone report-only policy to enforced (review finding 3).
- **D-67-2:** display in `Asia/Damascus` from `Localization:TimeZone` (an unknown id stops the service at start-up); storage stays UTC. `yyyy/MM/dd` and `yyyy/MM/dd HH:mm`, with the title «بتوقيت دمشق». A posted `datetime-local` is local time and binds as UTC; a calendar date (`[DataType(DataType.Date)]`, `type="date"`) is never shifted.
- **D-67-3:** the gateway records `sub`, the display name and the agency of each signed-in interactive user (never a service account), off the request path; `POST api/user-names/by-ids` answers interactive staff, for their own agency only (`platform-admin` all), at most 50 ids, the name only, with antiforgery. «النظام» for an automated actor, «مستخدم غير معروف» with the id in a tooltip otherwise.
- **D-67-4:** the rule JSON shows, under «عرض تقني», to `hr-admin`, `Policy.Author` and `Policy.Approver` only; everyone else reads the sentence (the shadow report's differences too).
- **D-67-5:** the portal answers at `/careers` alone; `/ui/hr-core/` opens the dashboard.
- **D-67-6:** the residue's public posting is closed through the application, nothing deleted (§8); a reset of the demo stays a separate decision.
- **D-67-7:** Arabic first, the abbreviation in brackets: «رمز الصنف (SKU)», «إفصاح تضارب المصالح (COI)», «مكافئ الدوام الكامل (FTE)».

**Deviations (none defers part of the plan):**
1. **The portal is not re-routed** (P-9 says it "moves under careers"). HR.Core already strips `/careers` into the path base; an action constraint (`[PublicPortalOnly]`) refuses the module's path base instead. Re-routing would have sent the portal's assets to the gateway's own static files. Same result: `/careers` alone, `/ui/hr-core/` the dashboard, `/ui/hr-core/track` and `/my` 404.
2. **The registry's column is `AgencyId`, not `TenantId`** (review, second pass): the bootstrap's TEN-002C hardening forces tenant row security on every `TenantId` column, and the gateway sets no tenant, so under the production role the registry would have read and written nothing. The unreleased migration was edited in place; a test keeps `TenantId` off the table.
3. **The registry writes from a background service** (`GatewayUserNameWriter`), not on the request path (review finding 2): an unreachable audit database no longer holds a signed-in page for the connect timeout.
4. **The gateway's own API endpoints answer their status as it is** (the status-code pages feature is off for `[ApiController]` endpoints): a refused POST answered 405, because the error page re-executed it as a POST to a GET-only action. Older than this plan; found by AC-UX-67b. Three older tests that met it accept 403 too.
5. **17 inputs that mean a calendar date are `type="date"` now** (payroll periods and run date, allowance and pay-profile dates, asset purchase, commencement notification, health entry, loan dates), and their 13 properties carry `[DataType(DataType.Date)]`: they were `datetime-local` before this plan, and the new binder would have moved them by three hours (review finding 1, high). A new guarded rule refuses a `datetime-local` bound to a date.
6. **Two rules beyond P-12's eight:** `date-as-time` (above) and `unconfirmed-destructive` (a destructive form without `data-hr-confirm`; 14 forms in 7 services fixed, the policy editor's «إهمال المسودة» among them). Both with planted defects.
7. **Integration test hosts are shared** (`SharedHrCoreHost`, a class fixture for the portal tests, one EF internal provider per database kind in `Ux067UserNamesTests`): PLAN-67's new tests had pushed the integration suite past EF Core's twenty internal service providers per process (175 failures). No test was excluded; each keeps its data apart by its own ids.
8. **Ten acceptance and E2E posts sent UTC digits to `datetime-local` fields** (interview times, committee dates, delegation windows): with D-67-2 the field means Damascus time, so they now go through `DamascusClock.Input(utc)`. Two literal checks (AC-POL-49a's reason codes, AC-WF-23f's English badge and raw type) read the readable text now; an absent reason or badge still fails them.
9. **The E2E suite with `Enforce` was not run on this machine:** its preparation needs PowerShell 7 and Python, neither installed here (the owner's machine). The PR's E2E check runs it on `docker-compose.ci-e2e.yml`, which enforces the policy on the fifteen applications.
10. **AC-UX-67b proves the names in the browser by intercepting the dashboard's HTML** (adding the span `_UserDisplayName` renders for a known and an unknown user), not through a test page in a service: no service gained a test-only page. The planted handler of AC-UX-67a is injected the same way.
11. **PLAN-52's PR body** (`reports/plan52-pr.md`, left untracked) is committed with the plan's documents (`672b9490`), as PR bodies 50 and 51 are tracked.

## 4. The CSP, before and after

**The cause (P0, proved on the demo, `plan67-baseline.md` 0.3):** every forwarded page carried two `Content-Security-Policy` headers, the gateway's (written before `MapReverseProxy`) and the service's (added by YARP). An inline block must satisfy both and carries only the service's nonce, so the gateway's policy blocked the agency colour, its script, every page block and the portal's whole stylesheet. The other security headers were doubled the same way. Inline handlers and `style` attributes were blocked even under one policy.

| Key page | Demo before (policies / violations) | Local prod-like after, `Enforce` | Demo after |
|---|---|---|---|
| `/careers` | 2 / 1 (`style-src-elem`) | 1 / 0 | 1 / 0 |
| a posting | 2 / 1 | 1 / 0 | 1 / 0 |
| `/careers/track` | 2 / 1 | 1 / 0 | 1 / 0 |
| gateway home `/` | 1 / 0 | 1 / 0 | 1 / 0 |
| `/ui/hr-core/` | 2 / 1 (it was the portal) | 1 / 0 (the dashboard) | 1 / 0 |
| HR.Core dashboard | 2 / 3 | 1 / 0 | 1 / 0 |
| employee list | 2 / 3 | 1 / 0 | 1 / 0 |
| employee details | 2 / 5 | 1 / 0 | 1 / 0 |
| work inbox | 2 / 3 | 1 / 0 | 1 / 0 |
| approval review | not resolved on the demo | 1 / 0 | 1 / 0 |
| requisition details | 2 / 3 | 1 / 0 | 1 / 0 |
| hire page | not resolved on the demo | 1 / 0 | not resolved (optional: no application with an accepted offer on the demo) |
| policy list | 2 / 3 | 1 / 0 | 1 / 0 |
| policy details | 2 / 3 | 1 / 0 | 1 / 0 |
| policy rules (`EditRules`) | not resolved on the demo | 1 / 0 | 1 / 0 |
| policy simulation | 2 / 3 | 1 / 0 | 1 / 0 |
| establishment change details | 2 / 5 | 1 / 0 | 1 / 0 |
| funded position details | 2 / 4 | 1 / 0 | 1 / 0 |
| HR.Contracts hire wizard | 2 / 3 | 1 / 0 | 1 / 0 |
| payroll dashboard | 2 / 4 (the chart script among them) | 1 / 0 | 1 / 0 |
| asset details | 2 / 3 | 1 / 0 | 1 / 0 |
| transfer list | 2 / 3 | 1 / 0 | 1 / 0 |

Violations by kind before: `style-src-elem` (the `<style>` blocks of `_OrganizationBranding`, `_PublicLayout` and pages), `script-src-elem` (inline scripts), `style-src-attr` (`style` attributes). After, locally (AC-UX-67a and f, final images): none of any kind, and no report-only policy beside the enforced one. The probe's own planted inline handler is reported (the negative). `ux067-validate.ps1 -Headers` on `/careers`, `/careers/track` and `/`: one policy each.

On the demo (`AcUx67VpsChecks` on tag `1ee455bf1a6f`, 2026-11-15T08:00Z to 08:03Z, as `hradmin`, the portal anonymous): 21 of 22 key pages answer 200 with one CSP header, one policy and no violation; the approval review and the rules editor were opened on a pending approval and a policy draft the check made for the read and removed after (the demo holds neither). 42 «after» screenshots at 1366×768 and 390×844 (`artifacts/plan67/vps/after/screenshots/`, beside P0's 44 «before»).

## 5. The interface, before and after

Counts by `InterfaceTextScanner`, the guard's own scanner (before: 652 views, 2026-11-13T15:58Z; after: 655 views, 2026-11-15T00:36Z). Every kind ends at zero; the guard holds no list rows.

| Project | English labels | Raw statuses | Printed ids | Raw JSON | Inline handlers | `style` attributes | Empty tables | Times |
|---|---|---|---|---|---|---|---|---|
| Admin.Logistics.Web | 0 → 0 | 2 → 0 | 2 → 0 | 0 | 0 | 1 → 0 | 0 | 46 → 0 |
| Auth.Directory.Web | 0 | 0 | 2 → 0 | 0 | 0 | 0 | 0 | 9 → 0 |
| Compliance.Web | 0 | 0 | 7 → 0 | 0 | 0 | 0 | 0 | 14 → 0 |
| Dev.Analytics.Web | 0 | 0 | 2 → 0 | 0 | 0 | 2 → 0 | 1 → 0 | 18 → 0 |
| HR.Contracts.Web | 0 | 3 → 0 | 4 → 0 | 0 | 0 | 5 → 0 | 1 → 0 | 27 → 0 |
| HR.Core.Web | 37 → 0 | 45 → 0 | 21 → 0 | 7 → 0 | 12 → 0 | 16 → 0 | 20 → 0 | 187 → 0 |
| HR.Health.Web | 0 | 0 | 3 → 0 | 0 | 0 | 0 | 0 | 29 → 0 |
| HR.Movements.Web | 0 | 0 | 16 → 0 | 0 | 0 | 2 → 0 | 1 → 0 | 25 → 0 |
| HR.Training.Web | 0 | 2 → 0 | 3 → 0 | 0 | 0 | 8 → 0 | 1 → 0 | 11 → 0 |
| HRManagement.Gateway | 0 | 2 → 0 | 3 → 0 | 0 | 0 | 3 → 0 | 1 → 0 | 3 → 0 |
| HRManagement.Web.Shared | 0 | 0 | 0 | 0 | 0 | 2 → 0 | 0 | 0 |
| IDCard.Web | 0 | 0 | 4 → 0 | 0 | 0 | 0 | 0 | 11 → 0 |
| IT.Infra.Web | 0 | 1 → 0 | 4 → 0 | 0 | 0 | 6 → 0 | 0 | 20 → 0 |
| Payroll.Web | 4 → 0 | 6 → 0 | 18 → 0 | 0 | 0 | 1 → 0 | 4 → 0 | 59 → 0 |
| Records.Archive.Web | 0 | 0 | 2 → 0 | 0 | 0 | 0 | 0 | 16 → 0 |
| **Total** | **41 → 0** | **61 → 0** | **91 → 0** | **7 → 0** | **12 → 0** | **46 → 0** | **29 → 0** | **475 → 0** |

- Rules the review widened found more, all fixed in P7: the read-only `value` of disabled fields and `@ViewBag.X` (12 user ids and a raw time in 12 edit forms), an empty table counts only with `_HrEmptyState` or an Arabic empty text (69 tables), literal English badges (the inbox's «legacy», «workflow»), 17 date inputs read as times, 14 destructive forms without a confirmation.
- The 18 inline `<script>` and 4 `<style>` blocks remain: they carry the page's nonce and run under one policy (D-67-1). The 50 `datetime-local` inputs bound with `asp-for` go through the shared tag helper and binder.
- **The named technical exceptions** (in the guard's code, each with its reason): the gateway audit page's `CorrelationId`; the user admin page's `KeycloakUserId`; the national number (`DisplayNationalId` on the employee page, `NationalId` on the application and the posting's applicants); a printed `…Json` only inside `_TechnicalJson`.

## 6. Names on the demo

- **Users the registry knows:** 14, after the server suites (2026-11-15T08:42Z): `hradmin` and the `e2e-*` users the checks sign in as. A name is recorded at a user's first sign-in after the deploy (D-67-3), so the owner's other demo users join as they sign in.
- **People still «مستخدم غير معروف»:** 0 of the 3 distinct people that the policy, approval, requisition and establishment pages print on the demo (`PolicyPublications` maker and checker, `ApprovalRequests` requester and reviewer, `RecruitmentRequisitions` creator and canceller, `EstablishmentChangeRequests` creator and applier). «النظام» shows for an automated actor.
- AC-UX-67b proves the lookup in the browser locally: a known user's span is filled with the registry's name, an unknown id stays «مستخدم غير معروف» with the id in its tooltip, and an employee-only user is refused (403).

## 7. Readable rules

- `PolicyRuleReadableRenderer` renders every rule of the 20 domains' seeds: `Ux067PolicyPagesTests` (on the real seeder) checks that every seeded and published rule names each field, operator, value and effect of its JSON, with no unknown node, and that the page shows the sentences; `Ux067PolicyRuleReadableTests` (30 unit tests) cover the eight operators, `all`/`any`/`not`/`In`, the eleven effects, seven unreadable inputs, a field missing from the schema, 40-deep and 200 random inputs (it never throws).
- **Parity with the evaluator:** the simulation form and its JSON give the same decision, allowed and refused (AC-UX-67e and the integration test); an unknown fact is refused as before (`PolicyFactsValidator.UnknownFieldMessage`, one constant the validator writes and four PLAN-49 checks read). A rule the engine's strict reader refuses reads «قاعدة غير صالحة — لا تُطبَّق»; NotEquals reads «محدد ولا يساوي» (a missing fact never matches).
- AC-UX-67c, locally: the published branch-scope template's 2 rules as sentences with no Latin letter, the JSON under «عرض تقني» for an administrator, none for a reader. On the demo (`AcUx67VpsChecks`, the Core.Establishment template): its 2 published rules as Arabic sentences, Arabic labels, the JSON under «عرض تقني» for `hradmin`, and every time on the page in Damascus time (the stored UTC + 3 h, titled «بتوقيت دمشق»).

## 8. The portal and the demo data

- **Links kept** (`Ux067PortalRoutingTests`, AC-UX-67d, and on the demo: `/careers`, `/careers/index`, `/careers/track`, `/careers/privacy` and the three postings 200, `/careers/my` 302 to the candidate's sign-in; the navbar and buttons carry their gradient, the cards their colour): `/careers`, a posting, `/careers/track`, `/careers/my`, competition results and their PDF, and the links in candidate messages; `/ui/hr-core/` opens the dashboard; `/ui/hr-core/track` and `/my` answer 404.
- **The server checks** (P-11) write realistic Arabic titles, criteria, comments, reasons and notes; their markers stay in fields staff do not see (`JustificationNotes`, `LegacyEntryReason`, `DecisionReference`, the evidence reference, short tokens). None publishes a requisition, so none leaves a posting.
- **Postings closed (D-67-6):** one, the only posting the portal showed with a server check's marker. As `hradmin`, through the requisition's own «إغلاق» action (2026-11-15T07:54:17Z; `AC_UX_67_vps_close_the_public_postings_of_the_server_checks`, which closes any posting a server check left and normally finds none): requisition `REQ-2026-00004` (`23b42bbe-0980-460f-94f4-ca38f3f3a5d6`) Published → Closed, its posting `b4e13f41-3d6b-42e7-b33c-d3ec26b9a875` («محاسب», «PLAN-43 server check») unpublished. No application of either kind was open on it, so the close rejected or cancelled nothing; nothing was deleted. **Undo:** the application has no reopen action, so the backup `pre-plan67-20261115T063540Z`, or narrowly: the requisition back to `Published` with `ClosedDate` NULL, and the posting's `IsPublished` true. A second run found none. The portal now shows three postings.
- Recorded for the owner, not touched (not a server check's): posting `46635d93…`, «موظف فئة ثالثة», described «طلب شاغر تجريبي لاختبار دورة الحياة».

## 9. The guards and their own negative tests

- **`InterfaceTextGuardTests`** (architecture): every view of `src/` (at least 600, every project known) through `InterfaceTextScanner`, ten rules: `english-label` (with literal badges), `raw-status`, `printed-id` (with read-only values and `@ViewBag`), `raw-json`, `inline-handler` (with `javascript:` URLs), `style-attribute`, `empty-table`, `unformatted-time`, `date-as-time`, `unconfirmed-destructive`; plus the reflective check of `<label asp-for>` and `DisplayNameFor` against Arabic `[Display]`. The per-project lists started at 649 rows (P2) and are gone; `PLAN-67` left `CeilingList.Owners`, so a new row is malformed.
- **`GuardSelfTests`:** each rule fails on a planted view and passes once fixed; an internal code as secondary text is not a label; a technical id passes only in its own file; a row needs an owner; stale and unlisted rows are caught; the P7 additions each have their plant (read-only value, `@ViewBag`, a hidden-only empty message, a badge, a date bound as time, an unconfirmed destructive form).
- **`scripts/ux067-validate.ps1`** (ASCII, PS 5.1): the policy builder, the production modes, the compose files' `Enforce`, the views, the guard lists, the gateway, the acceptance wiring; `-SelfTest` refuses 18 planted defects and accepts two clean policies; `-Headers` reads live headers per directive.
- `ux003-no-native-confirm.ps1`: no `confirm()` left.

## 10. Results

- **Build:** `dotnet build HRManagement.slnx -warnaserror` 0 warnings, 0 errors (final, 2026-11-15).
- **Unit:** 6353/6353 (full suite, 2026-11-14); after the last changes the touched subset 427/427.
- **Architecture:** 313/313.
- **Integration (`Category!=Testcontainers`):** 453/457, 4 skipped as on `main` (twice).
- **Integration (Testcontainers, the CI step on the host, VPN off, on `1ee455bf`):** 227 passed, 1 skipped (`Ops038BootstrapIdempotencyTests`, skipped by its own condition), 3 min 41 s (2026-11-15T00:35Z to 00:39:35Z).
- **Acceptance, local** (`artifacts/plan67/p15/`):
  - AC-UX-67a..f on the final images: 7/7 Pass (1 min 32 s), and again 7/7 in the final main group.
  - prod-like: journey 1/1; main group 44/45 with AC-TEN-48 Blocked as designed without the journey before it, then journey + AC-TEN-48 1/1 + 1/1; isolated AC-INT-37 8/8 (after the Damascus-clock fix), AC-MSG-40 8/8, AC-WF-44 5/5, AC-WF-45 8/8, AC-WF-46 9/9, AC-POL-47 1/1, AC-OPS-39 8/8.
  - overlays: AC-POL-49 Enforce 2/2, AC-S2S-42 9/9, AC-WF-46 orphans 1/1.
  - lab: every required class passes (AC-EST-10's token reset is the known one-off, rerun 7/7 with AC-WF-23); the optional failures are environmental and older than this plan (AC-08, 19, 21, 30 need PowerShell 7; AC-23, 24, 25: the lab seed's open service-regime exceptions refuse payroll, PLAN-58/59's).
  - leastpriv: AC-OPS-38 and AC-TEN-48 9/9.
- **E2E with `Enforce`:** the PR's E2E check (deviation 9).
- **The PR run's artifact (evidence of record, with the screenshots):** linked in the merge note (the PR is opened after this report).

## 11. The server

**Before** (P0 and P9's read-only pictures; tag `92a69960c826`): every forwarded page with two policies and 3 to 5 violations (§4); the portal unstyled and also at `/ui/hr-core/`; the residue of «server check» rows in 18 tables of 6 databases (`plan67-baseline.md` 0.6), with one public posting among them (`b4e13f41…`, closing 2026-11-15); 22 containers, every one with a health check healthy; 18 GB free on the disk.

**Changes and their undo** (each also in `OPS_VPS_SINGLE_HOST.md` §10):

| Change | Undo |
|--------|------|
| Tag `1ee455bf1a6f` (previous `92a69960c826`); the gateway's migration `UX067_UserNameRegistry` in `gateway_audit_db` (table `gateway_user_names`), by `gateway_audit_migrator`, 1 migration applied in all | `scripts/vps/deploy.ps1 -Rollback`, no SQL (the rollback note below) |
| `REQ-2026-00004` closed through the application, its posting `b4e13f41…` unpublished (§8) | the backup, or the requisition back to `Published` (`ClosedDate` NULL) and `IsPublished` true |
| The server's build cache (13.3 GB) and the local images of seven older releases removed (they stay in the loopback registry) | `docker pull 127.0.0.1:5050/hrmanagement/<image>:<tag>` |
| Backup `/opt/hrm/backups/pre-plan67-20261115T063540Z` (700/600: `pg_dumpall.sql`, `env.backup`, `SHA256SUMS`, both OK; 17 `CREATE DATABASE`) | delete when no longer needed |

**Build and deploy:** upload 32.4 MB in 26 s (through the VPN). Build attempt 1 (06:38:28Z to 07:09:14Z) built 16 of 17 images and stopped at `hr-migrate`'s EF bundle of dev-analytics when the server's disk reached 98% (3.3 GB free: a 13.3 GB build cache beside 61 GB of images); the running demo was untouched. After freeing 56 GB, attempt 2 (07:13:05Z to 07:48:42Z): build 2077 s, push 37 s, 17/17. Deploy, first attempt, 07:49:01Z to 07:52:56Z: pull 5 s, up 139 s, bootstrap 63 s, reconcile 15 s (0 changes), healthy 15 s; services away about 3 min at most.

**Checks** (evidence under `artifacts/plan67/vps/after/`, never in `acceptance-evidence/`): `AcUx67VpsChecks` 5 of 5 (the CSP check after its fix, §4; the portal; the policy page; the confirmation and the reviewer's comments, on a draft and an approval it made and removed; the names after the suites, §6) and the residue close (§8). **The server suites 48 of 49** (08:04:05Z to 08:37:49Z, 33 min 42 s): `AcOps36VpsBootTests` without 36j (excepted as before), `AcInt37VpsServerTests` and the server checks of PLAN-38..52; AC-WF-46's sweeps check failed as in PLAN-46..52 (§13) and passed alone right after the next cycle (12 s). The suites rewrote 37 committed evidence files and added 4: copied to `artifacts/plan67/vps/acceptance-evidence-from-run/` and restored.

**At the end** (08:44:33Z): tag `1ee455bf1a6f` (previous `92a69960c826`), 22 containers, every one with a health check healthy, 51 GB free; `smoke.ps1 -RequireIntegrationWiring` 50/0/1 (the warning is the client-side port probe, as before).

**Rollback note** (PLAN-52's code read with `git show`; `OPS_VPS_SINGLE_HOST.md`): `92a69960c826` never reads `gateway_user_names` and sees no pending migration (the extra history row is harmless); the table stays for a roll-forward. Its pages are back to two policies (the unstyled portal and the blocked agency colour), UTC times and the portal at `/ui/hr-core/`; what the new tag stored is UTC (calendar dates at midnight UTC), so the old tag reads it correctly; the closed requisition stays closed.

## 12. Items that other plans own

- **POL-G (wave 3):** the structured rule editor, conflict detection, simulation on the draft. The rules editor still takes JSON from an author, with the sentence above each rule.
- **WF-H (wave 3):** the workflow definitions screen.
- **D-67-6's reset:** reloading the demo with clean data is the owner's separate decision; this plan only closed the residue's public posting.
- **UX-002:** the accessibility audit.
- **PLAN-58/59:** payroll on the lab seed (AC-23..25).

## 13. Defects for later plans

- **The gateway's audit tables under the production role (unverified, older than this plan):** `gateway_audit_entries`, `security_audit_events` and `EvidenceRecords` carry forced tenant row security (policy `tenant_isolation`, read on the demo), and the gateway's `GatewayAuditDbContext` has no tenant session interceptor, so with `docker-compose.production.yml`'s `gateway_audit_app` an insert would fail the policy's check. The demo cannot show it: in demo mode the gateway connects as the database administrator (`docker-compose.vps-demo.yml`), which bypasses row security (2262 audit rows in the two hours before 08:44Z, each with its tenant). The leastpriv stage uses `gateway_audit_app` but no check there reads the audit. The names registry avoids it (`AgencyId`, no row security).
- **The server build does not check its disk first:** attempt 1 filled the server's disk (98%) and failed late, at the migrator's EF bundles. `build-on-host.sh` could refuse to start below about 30 GB free and say what to remove.
- **The hire page is never reached on the demo:** no application there has an accepted offer, so it stays optional in the server's CSP check (locally it is checked).
- AC-WF-46's sweeps check reads the last 5000 lines of a noisy log: after AC-OPS-39f's recreate a cycle can fall outside that window (PLAN-46..52, and here).
- The E2E suite cannot run on the owner's machine without PowerShell 7 and Python (CI's preparation).
- A posting reached by `HEAD /careers` answers 302 to sign-in while `GET` answers 200 (the public route matches GET only); no effect on a browser.

## 14. Timing

Started 2026-11-13T08:06:22Z. To this report's commit (about 09:05Z on 2026-11-15): **wall-clock about 49 h**, of which **about 37 h 09 min were stops** and **about 11 h 50 min active**. Waiting for CI, the merge and the post-merge run are recorded in the merge note (`plan67-progress.md` on `docs/plan-67-merge-note`), not here: this report is committed before the local gates, as the prompt requires.

**Stops:**

| From (UTC) | To (UTC) | Length | Cause, and what ended it |
|------------|----------|-------:|--------------------------|
| 11-13 08:08 | 11-13 08:13 | 5 min | P0: PLAN-52 not merged yet (`BLOCKED:`); the owner's "PLAN-52 merged, go" |
| 11-13 08:14 | 11-13 15:38 | 7 h 24 min | P0: the recheck still found PLAN-52 running and unmerged; the owner's "continue" after its merge (09:41Z) |
| 11-13 19:40 | 11-14 19:22 | 23 h 43 min | P7: drive C full (six image builds in a day; the build cache reached 219 GB), Docker down (`BLOCKED:`, P-2); the owner freed C |
| 11-14 19:55 | 11-14 19:57 | 3 min | P7: the cleanup had removed the images, and the network gave 5 to 12 KB/s under the VPN; the owner turned it off |
| 11-15 00:39 | 11-15 06:34 | 5 h 54 min | P9: port 22 blocked without the VPN (`BLOCKED:`, P-2); the owner's "VPN is on" |

**Build, acceptance and deploy:**
- Local images: P0 about 12 min and the stack 4 min more; P7's first build and stack about 15 min; after the disk cleanup, the cold rebuild of every image with the base images pulled anew and the NuGet seed 39 min (2026-11-14 21:50Z to 22:29Z); later rebuilds cached, a few minutes each.
- Local acceptance: AC-UX-67 1 min 32 s to 1 min 38 s per run; P-15's stages: prod-like 19 min, overlays 11 min, lab 26 min, leastpriv 11 min; the reruns 22 min; the final main group 17.5 min; journey + AC-TEN-48 5.5 min.
- Host suites: unit 1 min 35 s; Testcontainers full step 3 min 41 s.
- Server: upload 26 s; build attempt 1 30 min 46 s (failed, full disk), attempt 2 2077 s + push 37 s; deploy 3 min 55 s with the smoke (services away about 3 min); the PLAN-67 server checks 3 min 41 s, then the CSP check alone 3 min; the server suites 33 min 42 s; the WF-46 rerun 12 s.
- Actions minutes: in the merge note (the PR run).

**`scripts/plan-timing.ps1 -From 25 -To 67`** (Riyadh time; PLAN-67's line is before its merge):

```
Plan    Commits Started          FirstCommit      End              Merged Span   WallClock
PLAN-45      13 2026-11-07 16:55 2026-11-07 16:56 2026-11-07 23:11  False 6:15   6:15
PLAN-46      14 2026-11-07 23:33 2026-11-07 23:34 2026-11-08 15:06  False 15:31  15:32
PLAN-47      15 2026-11-08 15:06 2026-11-08 15:06 2026-11-09 15:30  False 24:23  24:24
PLAN-48      12 2026-11-09 18:27 2026-11-09 18:28 2026-11-10 02:18   True 7:50   7:51
PLAN-49      11 2026-11-10 11:35 2026-11-10 11:37 2026-11-11 04:35   True 16:58  17:00
PLAN-50      14 2026-11-11 13:00 2026-11-11 13:02 2026-11-11 23:10   True 10:08  10:09
PLAN-51      15 2026-11-12 02:26 2026-11-12 02:30 2026-11-12 20:04   True 17:34  17:38
PLAN-52      11 2026-11-12 23:25 2026-11-12 23:26 2026-11-13 12:40   True 13:13  13:14
PLAN-66      12 2026-11-06 20:46 2026-11-06 21:03 2026-11-07 00:36  False 3:33   3:50
PLAN-67      10 2026-11-13 11:06 2026-11-13 18:39 2026-11-15 11:45  False 41:05  48:39
```

(PLAN-25..44 as printed by the script are unchanged from PLAN-52's report; the script prints no line for PLAN-42.)

## 15. Readiness

- **Wave 2a, numbered from PLAN-68** (`PROGRAM_COMPLETION_ROADMAP.md`): every new view meets the guard on its first commit (Arabic labels and statuses, names, `HrDate`/`HrDateTime`, an empty state, a confirmation on a destructive form, no inline code); there is no list to add a row to.
- **POL-G:** the renderer gives the structured editor its vocabulary and its segments: each condition as field, operator and value with the schema's Arabic names and types, each effect as a verb with its parameters, and the strict reader's verdict; the editor can write the same JSON and show the same sentence, so the published versions read the same before and after it.
