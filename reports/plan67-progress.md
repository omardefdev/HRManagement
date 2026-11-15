Started (UTC): 2026-11-13T08:06:22.4518317Z

# PLAN-67 progress

## P0 — preconditions

- **BLOCKED (UTC 2026-11-13T08:08Z): preconditions 1, 2 and 3 fail. PLAN-52 is not merged and is still running.**
  - Precondition 2: after `git fetch origin`, `origin/main` is `e6a93705` (PLAN-51, PR 49). There is no merge of `release/plan-52`, and no `release/plan-52` branch exists locally.
  - Precondition 1: this folder is on `feature/plan-52-iam-branch-scope-bola`. That session ([Branch scope and BOLA test]) is in its P8 (the demo server). Its server suites were running when this check ran:
    - a `testhost` started at 07:57Z;
    - an `ssh` to the server started at 08:07Z;
    - `reports/msg040-replay-server-20261113T080523Z.json` was written at 08:05:23Z;
    - its progress file was last written at 07:58:51Z.
  - Precondition 3: the tree carries PLAN-52's uncommitted P8 work: the realm manifest, `add-demo-user.sh`, `AcIam52VpsChecks.cs`, docs, `reports/plan52-final-report.md`, and the server suites' evidence rewrites.
- Nothing else was done. No branch switch, no keep-awake process, no checkout, no restore. PLAN-52's working tree is untouched.
- This file was moved from `reports/` into the untracked staging folder `docs/acceptance-plans/execution/PLAN-67-staged/`. That way PLAN-52 cannot stage it or trip on it, and the start stamp stays as written. P-1 moves it back to `reports/plan67-progress.md`.
- **Next step, exactly:** when PLAN-52 has merged (its PR green and merged by that session) and its session has stopped:
  1. Move this file back to `reports/plan67-progress.md`.
  2. Record `Resumed (UTC):` with the reason.
  3. Recheck the five preconditions.
  4. Start the keep-awake process.
  5. Continue with P0 step 2 (P-1).
- **Resumed (UTC): 2026-11-13T08:13:32Z.** The owner answered "PLAN-52 merged, go".
- **Still BLOCKED (UTC 2026-11-13T08:14Z):** the recheck contradicts the message, so nothing was started.
  - After `git fetch`, `origin/main` is still `e6a93705` (PLAN-51).
  - No PLAN-52 PR exists on GitHub; the newest is PR 49 (PLAN-51, merged).
  - No `release/plan-52` branch exists.
  - The folder is still on `feature/plan-52-iam-branch-scope-bola`, with PLAN-52's uncommitted P8 work.
  - PLAN-52's server tests are still running: the `testhost` from 07:57Z, and a new `ssh` at 08:13:46Z. Two more evidence files were rewritten since 08:08Z (`36h-after.json`, `timing.json`).
  - Its progress file stops at `AcIam52VpsChecks` 4/4. Still ahead: the PLAN-36..51 server suites, smoke, OPS doc, the P8 commit, then P9 docs, P10 local gates, the PR, CI (about 1 h) and the merge.
- Next step: unchanged (above).
- **Resumed (UTC): 2026-11-13T15:38:20Z.** The owner said "continue" (اكمل). Stood still 08:14Z to 15:38Z (7 h 24 min): waiting for PLAN-52 to finish and merge.
- **Preconditions rechecked:**
  1. The folder is on `main`. PLAN-52's processes are gone: no `testhost`, `ssh` or `dotnet` running.
  2. `origin/main` = `main` = `aa8be851` (PR 50, `release/plan-52`, merged 09:41:55Z).
  3. The tree is clean except the allowed files, plus `reports/plan52-pr.md`. That is PLAN-52's PR body, left untracked; PR bodies 50 and 51 are tracked, so it is committed with the merge note (deviation, recorded).
  4. `ssh -o BatchMode=yes hrm-vps whoami` prints `hrm`. The running tag is `92a69960c826` (PLAN-52), the rollback target.
  5. `gh` is signed in as `omardefdev`, with `repo` and `workflow`.
  - Disk: C 172.9 GB free (Docker's data disk), D 250.1 GB free.
- Keep-awake: PID 22780 (`artifacts/plan67/keep-awake.ps1`, same script as PLAN-52's).
- The progress file was moved back to `reports/plan67-progress.md`.
- Branch `feature/plan-67-ux-readable-arabic-interface` from `origin/main` (`aa8be851`). `f10933f8`: PLAN-52's merge note, cherry-picked. `672b9490`: the study, the plan, the prompt, and PLAN-52's PR body. `docs/plan-52-merge-note` deleted (its content is on this branch).

## P0 — baseline (in progress)

- **0.2:** `dotnet build HRManagement.slnx -warnaserror`: 0 warnings, 0 errors, 71 s. Architecture tests 203/203.
- **0.3, the cause, on the demo** (`curl.exe` and Playwright as `hradmin`, `artifacts/plan67/vps/before/`, 5 min 28 s):
  - **Two policies confirmed.** Every proxied page carries two `Content-Security-Policy` headers: the gateway's (its `connect-src` names the SSO host) and the service's. The other security headers are doubled the same way.
  - The gateway's own home page carries one policy and raises no violation.
  - The violations, by kind:
    - `style-src-elem`: the `<style>` blocks (`_OrganizationBranding`, `_PublicLayout`, and blocks inside pages);
    - `script-src-elem`: inline scripts;
    - `style-src-attr`: `style` attributes.
  - The portal (`/careers`, a posting, `/careers/track`) raises one `style-src-elem` violation: `_PublicLayout`'s style block, which holds all of the portal's own styles. That is the unstyled portal.
  - Three key pages could not be resolved on the demo: no pending approval to review, no application to hire, no draft version for `EditRules`.
  - Side finding: `HEAD /careers` answers 302 to sign-in; `GET` is public.
- **0.4, the inventory:** generated by `InterfaceTextScanner` (the guard's own scanner), into `artifacts/plan67/ui-inventory.{md,tsv}`. 652 views.
- **0.6, the demo's residue (read-only):**
  - Rows marked «server check» per table are in `artifacts/plan67/baseline/residue-by-table.txt`.
  - 25 requisitions are marked: 23 Approved, 1 PendingApproval, and 1 Published (`REQ-2026-00004`, `23b42bbe…`). The Published one has the only marked public posting: `b4e13f41…`, «محاسب», description «PLAN-43 server check», closing 2026-11-15.
- **0.5:** the first image build was stopped. It sends a fresh context for each service, so it would have picked up P1 edits made during the build. The P1 edits were stashed (`stash@{0}` plan67-p1-wip), and the images were rebuilt from the clean tree with `Security__CspMode=Enforce` in the prod-like compose file. That compose change is uncommitted and lands in P1.
- Rule for this session: never edit `src/` outside `src/Tests` while an image build runs.
- Finished (UTC): 2026-11-13T16:27:52Z — P0 committed (baseline, the CSP cause on the demo and locally, the inventory, before screenshots, the demo's residue).

## P1 — one CSP per response, nothing inline

- **Headers (D-67-1):** `UseHrSecurityHeaders` builds the policy as before but writes every security header in `Response.OnStarting`, and each only if the response does not carry it (a policy in either mode counts). A forwarded page keeps its service's policy and nonce; a gateway page, or a forwarded response without headers, gets the gateway's. The policy is unchanged (no `unsafe-*`, no wildcard).
- **Handlers:** the ten `confirm()` calls are `data-hr-confirm` on their forms, with the same Arabic text (`danger` for the destructive ones). The approval review page has one form holding the comments and both decisions; Reject posts to its action through `formaction` (`asp-action` on the button). AC-WF-44 reads the form's own action: unaffected.
- **`style` attributes (46):** classes in `syrian-theme.css` (`hr-col-30`, `hr-minw-*`, `hr-avatar-*`, `hr-text-*`, `hr-notification-container`…). Progress bars use `data-hr-progress`, applied by `shared-ui.js` through the CSSOM. `_FileImagePreview` takes `MaxHeightClass`.
- **Test stacks:** `Security__CspMode: "Enforce"` on the 15 apps of `docker-compose.acceptance-prodlike.yml` and of `docker-compose.ci-e2e.yml`. The E2E compose config validates, with 15 `Enforce`.
- **Tests:**
  - `Ux067GatewaySecurityHeadersTests` 4/4. Through YARP's forwarder: a forwarded page keeps the destination's policy alone; a forwarded response without headers gets the gateway's once; a gateway page's nonce matches its policy. The real gateway (`GatewayTestFactory`) forwards `/careers` with the destination's policy alone.
  - `Ux067ApprovalReviewCommentsTests` 2/2: the page has no `onclick`; approve and reject, posted to the targets the page renders, store the comments.
  - `GuardSelfTests` 53/53.
  - Unit tests over the compose files (`ProductionWiringMatrix`, `Pol049PolicyModeConsistency`, `Wf046Registry`, `Iam051ModuleFilterAndPolicies`, `Qa066Workflow`, `CiStepsParity`) 150/150.
  - `ux003-no-native-confirm.ps1` OK. Solution build `-warnaserror` 0/0.
  - Inventory after P1: 0 handlers, 0 `style` attributes.
- The browser test of the confirmation (the dialog shows, cancel changes nothing) is AC-UX-67a (P7), on the prod-like stack's browser.
- `acceptance-up.ps1` stops at its live verification without PowerShell 7 (the stack itself is up): use `-SkipLiveVerification`.
- Finished (UTC): 2026-11-13T16:29:33Z

## P2 — the guard

- **`InterfaceTextGuardTests`:** every view of `src/` through `InterfaceTextScanner`, the eight rules of P-12. Its tests:
  - the scan reads every view (at least 600, every project known);
  - every finding is a row of its project's list;
  - no row outlives its finding;
  - every list is well formed;
  - no stray list;
  - every technical exception names a view that exists, with its reason.
- **The lists** (`tests/architecture/ui-text-allowlist/<project>.txt`, `CeilingList`) were seeded from the scan after P1 (`UX067_SEED_LISTS=true`). 649 rows in 14 lists, each signed `PLAN-67:` with the phase that empties it:

  | Kind | Rows | Emptied by |
  |---|---|---|
  | English labels | 41 | P3 |
  | Raw statuses | 61 | P3 |
  | Times | 423 | P3 |
  | Printed ids | 88 | P4 |
  | Empty tables | 29 | P4 |
  | Raw JSON | 7 | P5 |

  - Inline handlers and `style` attributes: none (P1).
  - The 52 `datetime-local` inputs bound with `asp-for` are not rows: the shared tag helper and model binder of P3 convert them all.
  - `CeilingList.Owners` gains `PLAN-67`.
- **Technical exceptions, in the test's code:** the gateway audit page's `CorrelationId`. More come with P4.
- **Planted defects** (`GuardSelfTests`):
  - each rule fails on a planted view and passes once it is fixed;
  - an internal code shown as secondary text is not a label;
  - a technical id passes only in its own file;
  - a row needs an owner, and a stale or unlisted row is caught.
- Architecture tests: 292/292.
- Finished (UTC): 2026-11-13T16:30:47Z

## P3 — Arabic vocabulary and Damascus time

- **Time (D-67-2):**
  - `HrLocalTime` (ServiceDefaults): the zone from `Localization:TimeZone` (Asia/Damascus by default; an unknown id stops the service), one date format `yyyy/MM/dd` and one time format `yyyy/MM/dd HH:mm`, the input formats, and `TryParseUserTime` (local without offset, or ISO with an offset).
  - `DamascusCalendarService` reads the setting and publishes it as `HrLocalTime.Zone`, for code outside views.
  - `HrTimeHtmlExtensions` (Web.Shared): `HrDate`, `HrDateTime` (a `<time>` with the UTC value and the title «بتوقيت دمشق»), `HrTime` (a time of day, never converted), their `…Text` forms, and the input values `HrDateInputValue`, `HrDateTimeLocalValue`, `HrMonthInputValue`.
  - `HrDateTimeLocalTagHelper`: every `datetime-local` bound with `asp-for` shows the local value (52 inputs, no view change); a typed value is kept on redisplay.
  - `HrLocalDateTimeModelBinder` (registered by `AddHrLocalization`): a posted `datetime-local` value is local and binds as UTC. Not for `[ApiController]`, a date alone, or a value with an offset.
- **The time pass:**
  - 370 formats replaced by script (`artifacts/plan67/p3-times.js`; input, route and data attributes get the ISO input helpers);
  - 47 raw prints and 4 interpolations converted;
  - the candidate portal's `Date`/`Time`, the interview `min`, and the requisition closing `min` fixed by hand;
  - `CandidatePortalCompetitionResult.PublishedOn` (a preformatted string) becomes `PublishedAtUtc`;
  - the candidate portal, its notifications and the exam admission card used `TimeZoneInfo.Local` (UTC in the containers): now `HrLocalTime.Zone`;
  - `EditRules` effective-from and `Simulate` as-of are `datetime-local` and parsed with `TryParseUserTime` (PLAN-47's API tests send ISO with Z: unchanged).
- **Vocabulary:**
  - 61 raw prints go through `HrEnumDisplay` (new nullable overload), `HrStoredEnumDisplay` (new labels: planned, ended, completed, cancelled, submitted, permanent, temporary, the payroll codes), or `HrEntityTypeDisplay` (new `EntityTypeNamesAr`; the class's `[Display]` wins).
  - Arabic `[Display]` on `PolicyTemplateScope`, `PolicyVersionStatus`, `PolicyPublicationKind`, `PolicyVersionEditAction`, both enforcement-mode enums, `PolicyDomainStatus`, `ServiceRegimeExceptionType`, `CodeEntityKind`.
  - `PolicyDomainDescriptor.NameAr` is required and set in `PolicyDomainCatalog` for all 20 domains (the catalog is the only file that may name out-of-scope domains: PLAN-49's test), plus `OwnerServiceAr`.
  - `PolicyVocabularyAr`: operators, effects, actions, workflow codes, services.
  - `HrTextWithCodes`: in stored text, effects, services, domains and workflows by name; any other code as secondary text (`hr-internal-code`).
  - The six policy pages in Arabic; Ledger and ShadowReport rewritten, with empty messages.
  - Card codes in headings (`EST-001`…) as secondary text; `as-of`, `vs`, `workflow`, `legacy` and the English glosses translated. D-67-7: «رمز الصنف (SKU)», «إفصاح تضارب المصالح (COI)», «مكافئ الدوام الكامل (FTE)».
- **The guard:**
  - It gains a reflective check: a `<label asp-for>` with no text, or `DisplayNameFor`, whose property has no Arabic `[Display]`. 7 found and fixed; 0 view models unresolved.
  - `…Category` and `…Decision` leave the raw-status rule: they are free text in this codebase. The coded ones were fixed anyway.
  - `.ToLocalTime()` prints are now found.
  - Lists pruned: English labels 41→0, raw statuses 61→0, times 423→0.
- **Tests:**
  - `Ux067TimeAndVocabularyTests` (unit): the midnight, month-end and year-end boundaries; 00:30 Damascus stored as 21:30 UTC the day before and read back; the offset cases; the configured zone; the binder (page vs API vs date alone); the vocabulary.
  - `Ux067LocalTimeInputTests` (integration): the delegation form shows the Damascus clock.
  - `AcPol47VpsChecks` reads «تراجع»/«يدوي» (P-13).
  - Unit suite 6313 run: the 4 failures (my acceptance classes' gate pattern, and the domain-name file) fixed and rerun (43/43).
  - Integration (no Testcontainers) 419 passed, 4 skipped. Architecture 298/298.
- Finished (UTC): 2026-11-13T17:01:15Z

## P4 — names and empty tables

- **The registry (D-67-3):**
  - `GatewayUserName` (`gateway_user_names`: sub, display name, agency, first and last seen) in `GatewayAuditDbContext`.
  - Migration `UX067_UserNameRegistry`, generated by `dotnet ef`, no byte order mark. The snapshot was regenerated; it lost its own BOM, as `.editorconfig` asks (`charset = utf-8`).
  - The gateway database has no row security (no `ROW LEVEL SECURITY` in its migrations), so nothing to lift.
  - `GatewayUserNameRegistry` and its middleware (after `UseAuthorization`) record `sub`, `name` (else `preferred_username`) and `tenant_id` once an hour per user (memory cache), or when the name changes. Never a service account. A failure is logged without request data and never fails the page.
- **The lookup:**
  - `POST api/user-names/by-ids` (`UserNamesController`, `[ApiController]` with a JSON body, as HR.Core's `api/lookup/*/by-ids`).
  - Policy `Gateway.UserNames` = PLAN-50's `GatewayApprovalReachRequirement`: interactive staff, never a second definition.
  - At most 50 ids; the caller's agency only, `platform-admin` all; the name only; `[OrgScopeNotApplicable]`.
  - The gateway's path authorization passes the unknown `/api/user-names` segment to MVC, so the controller's policy governs it.
- **The page:**
  - `display-names.js` fills `[data-hr-user-id]` from the gateway (base `data-user-names-base`, default the origin) and removes the tooltip once named.
  - `HrUserName`: «النظام» for `system` or an empty GUID; a value that is already a name printed as it is; otherwise «مستخدم غير معروف» with the id in the tooltip. The `_UserDisplayName` partial wraps it.
  - Also `HrEmployeeName`, `HrOrgUnitName`, and `HrLegalName` (`law50` → «نظام القانون رقم 50», `SYR-LAW-50-2004` → «القانون رقم 50 لعام 2004», `special_*` → «نظام خاص — …», the code as secondary text).
- **The 87 printed ids:**
  - 38 users → `HrUserName`;
  - 2 employees and 5 units → their lookups;
  - 11 regime or legislation codes → `HrLegalName`;
  - about 25 records → their links («عرض مسير الرواتب»، «عرض طلب النقل»، «عرض الشكوى»…; the establishment change's target by its type); the service transaction shows its reference number; a correction request its date; the ledger's workflow «بدأ مساراً»; the fraud rule code as secondary text.
  - Technical exceptions in the guard's code, each with its reason: the audit page's `CorrelationId`, the user admin page's `KeycloakUserId`, the national number (`DisplayNationalId` on the employee page, `NationalId` on the application and the posting's applicants).
- **Empty tables:** the 27 views (29 tables were seeded in P2; one of the files is gone from the count) get an empty row with `_HrEmptyState`, built from each loop's own collection; the policy rule tables say «لا قواعد في هذا الإصدار».
- **The guard:** lists pruned, printed ids 88→0 and empty tables 29→0; only the 7 raw JSON rows remain (P5).
- **Tests:**
  - `Ux067UserNamesTests` 13/13: the upsert (once, then on a new name); a service account and a nameless user are never recorded; the policy for staff, `hr-employee`, a service account, no tenant, `platform-admin`; the agency filter and `platform-admin`; at most 50; the gateway endpoint carries the policy and no anonymous access; the page's three forms; the legal titles.
  - Architecture 298/298.
- Finished (UTC): 2026-11-13T17:13:41Z

## P5 — readable policies

- **The renderer:** `PolicyRuleReadableRenderer` (SharedKernel/Policy) turns When and Then, with the domain's facts schema, into segments and one Arabic sentence.
  - The groups: `all` «جميع الشروط التالية», `any` «أيٌّ مما يلي», `not` «ليس».
  - The eight operators in Arabic (`PolicyVocabularyAr.Operators`).
  - Values by type: a flag as نعم/لا, a number as it is, an action by its Arabic name, otherwise text in «».
  - The eleven effects as verbs, with their parameters: the deny reason, the workflow by its Arabic name, `SetField`'s field and value, the code policy key.
  - It never throws: an unreadable node says «شرط غير معروف — انظر العرض التقني» (or the effect form), and a field missing from the schema shows its key.
- **The pages:**
  - `_PolicyRuleReadable` (the sentence, segments styled, keys in `data-key`) and the shared `_TechnicalJson` (Web.Shared: `<details>` «عرض تقني», indented JSON). The guard allows a printed `…Json` only there.
  - Details: rule table with fixed columns and wrapped text (`hr-policy-rules`); the JSON shows only to `hr-admin`, `Policy.Author` and `Policy.Approver` (D-67-4, `CanSeeTechnicalAsync`); the empty message «لا قواعد في هذا الإصدار».
  - EditRules: the sentence above each rule's JSON fields (author) or instead of them (reviewer, with the technical view).
  - Simulate: a form generated from the schema (bool → نعم/لا select, number → number input, `action` → the subject's actions in Arabic, otherwise text). The JSON input is folded as «إدخال متقدم»; the controller turns either into the same dictionary, so the evaluation is unchanged. The decision shows Arabic labels, effects as verbs and the version number.
  - The establishment change payload, the service regime conflict and the shadow diff also go through `_TechnicalJson`.
  - `PolicyFactsValidator`'s unknown-field message is now Arabic: «حقل غير معروف في مخطط الحقائق».
- **The guard:** raw JSON 7→0, so every list is empty and the list files are gone. A `javascript:` URL counts as an inline handler (planted). `NationalScopeError`'s `javascript:history.back()`, which the CSP blocks, now links to the dashboard.
- **Tests:**
  - `Ux067PolicyRuleReadableTests` (unit) 30/30: the seeded branch rule's exact sentence with no Latin letter; the eight operators; any, not and in; the eleven effects; seven unreadable inputs; a field missing from the schema; 40-deep and 200 random inputs never throw.
  - `Ux067PolicyPagesTests` (integration, SQLite with `PolicyEngineSeeder`) 7/7: every seeded and published rule names each field, operator, value and effect of its JSON, with no unknown node; the details page shows the sentences and the JSON to `hr-admin` and `policy-approver` only (not `hr-core-supervisory`); the form and the JSON give the same decision, both allowed and refused; an unknown fact is refused as before.
  - All `Ux067` integration tests 30/30; architecture 299/299.
- Finished (UTC): 2026-11-13T17:21:54Z

## P6 — the portal at /careers, the server checks, the guard empty

- **The portal (D-67-5):**
  - HR.Core strips `/careers` into the path base, so the portal's links and anonymous assets resolve under `/careers`, but its routes sat at the root and answered `/ui/hr-core/` too.
  - `[PublicPortalOnly]` (an action constraint on `PublicCareersController` and `CandidatePortalController`) accepts a request that came through `/careers`, or one with no path base (the service reached directly, as the in-process tests do). It refuses the module's path base, so the conventional route opens the dashboard at `/ui/hr-core/`; an anonymous visitor is sent to sign-in by the gateway, as for every `/ui` path.
  - Every public link keeps its address: `/careers`, a posting, `/careers/track`, `/careers/my`, competition results and the PDF, and the links in messages.
  - The portal's dates were converted in P3 (`dd-MM-yyyy` → `HrDate`).
  - This deviates in form from P-9's "moves under careers": the controller is not re-routed. Re-routing would have sent the portal's assets to the gateway's own static files; the constraint gives the same result, `/careers` alone.
- **The server checks (P-11):**
  - The requisitions of PLAN-43, 44 and 50 have realistic Arabic titles («محاسب أول», «أمين مستودع», «مدقق داخلي»), Arabic criteria titles and Arabic approval comments; the marker stays in `JustificationNotes` (admin only).
  - PLAN-45's assets are «حاسوب محمول (عهدة|مفقود|مخزن) xxxxxx»; the marker stays in `LegacyEntryReason` and the loss notes.
  - PLAN-47's rollback reason is Arabic; the marker stays in the evidence reference.
  - PLAN-49's freeze reason is Arabic; the marker stays in `DecisionReference`, which its clean-up looks rows up by.
  - The leave, delegation, transfer and disciplinary notes keep their markers (internal fields the checks look rows up by).
  - None of the checks publishes a requisition, so none leaves a posting. The posting on the demo predates this plan and is closed in P9.
  - Each check keeps its meaning; PLAN-49's third check reads ledger rows by domain, not by marker.
- **The guard:** every list is empty and the files are gone; `PLAN-67` leaves `CeilingList.Owners`, so any new row would be malformed.
- **Fix:** P4's empty row in `Payroll/PayrollReports/MonthlySummary` checked a nullable collection (CS8604). Incremental builds had skipped the view; now `runs ?? []`. A no-incremental solution build is clean.
- **Tests:** `Ux067PortalRoutingTests` 4/4, on HR.Core with the module path base:
  - `/careers` is the portal (its navbar) and `/careers/track` answers;
  - `/ui/hr-core/` is the dashboard (its sidebar, no portal);
  - `/ui/hr-core/track` and `/ui/hr-core/my` answer 404.
- Architecture 299/299.
- Finished (UTC): 2026-11-13T17:26:55Z

## P7 — AC-UX-67 on the prod-like stack, the review

- Started (UTC): 2026-11-13T17:26:55Z (right after P6's commit).
- **AC-UX-67a..f** (`AcUx67ReadableInterfaceTests`, `[RequiredAcceptance]`, evidence `AC-UX-67`): written on a real Chromium with the probe's violation listener. What a scenario creates it cancels or deletes through the application (the freeze request is cancelled, the requisition draft is deleted through its own confirmation); the one pending approval it needs is inserted for the check and removed (a stated test-only precondition, as AC-WF-44's). No container is stopped or recreated.
- **Found while writing it:**
  - Seeded rule names end in «→ workflow», and a published version is never rewritten: the template page now reads that suffix in Arabic (`PolicyVocabularyAr.RuleName`, display only).
  - The gateway's `POST api/user-names/by-ids` (P4) had no `[ValidateAntiForgeryToken]`: the CodeQL gate (`cs/web/missing-token-validation`) would fail after the push. It now takes the token; `GET api/user-names/token` (same policy) hands it out with its cookie, and `display-names.js` fetches it once per page.
- **CI wiring:** `ci-scope.json` lists the class in `prodLike[]` (required, group `main`, `"browser": true`); the stage job installs Chromium once when a selected class has the flag (through `pwsh`; Windows PowerShell 5.1 cannot load Playwright and only says so). `ac-ownership.json`: `AC-UX-67`, PLAN-67, proves its claim.
- **`scripts/ux067-validate.ps1`** (ASCII, PS 5.1): passes; `-SelfTest` refuses 16 planted defects and accepts one clean policy.
- Keep-awake: PID 22780 had exited by 17:55Z; restarted as PID 17544 at 2026-11-13T17:55:20Z.
- Image build and stack start (`acceptance-up.ps1 -Profile ProdLike -SkipLiveVerification`) from 2026-11-13T17:56Z, in the background; the independent review runs meanwhile (read-only).
- **AC-UX-67, first run** (stack ready 2026-11-13T18:11:45Z, about 15 min after the build began; run 18:12Z, 1 min 38 s): 5 of 7 passed.
  - 67b: the template page printed «D-POL-4» (the inherited note, and the agency-override note of `CreateDraft`) and the seed's evidence «seed-Core.Establishment». The decision reference is gone from both notes; a seed's evidence reads «بذرة النظام» with the raw value as an internal code.
  - 67d: the portal's navbar is a gradient, a `background-image`; the check read `background-color` only. The test (and the server check) now reads the gradient. An unstyled portal still fails it.
- **The independent review** (one subagent, read-only, during the build; 8 findings). Each was checked against the code before acting:
  1. **High, confirmed:** the P3 binder read every `datetime-local` value as Damascus time, including 17 inputs that mean a calendar date and were `datetime-local` before this plan (payroll periods and run date, allowance and pay-profile effective dates, asset purchase date, commencement notification date, health entry date, loan dates). A payroll period typed as 1 December was stored as 30 November 21:00 UTC, and payroll reads its date part. **Fix:** the binder leaves a `[DataType(DataType.Date)]` property as it is posted; those 13 properties carry it and their 17 inputs are `type="date"`. A new guarded rule, `date-as-time`, refuses a `datetime-local` input bound to a date-typed or `…Date` property (planted defects in `GuardSelfTests`); unit tests for the provider and the payroll properties.
  2. **Medium, confirmed:** the registry wrote on the request path, so an unreachable audit database would have held every signed-in page for the connect timeout, and any save failure was cached as written for an hour. **Fix:** the middleware only queues (`GatewayUserNameRegistry.Queue`, at most once a minute per sighting); `GatewayUserNameWriter` (a background service) writes; only a unique-key race (23505) counts as written; any other failure is logged and retried a minute later. Integration test on an unreachable database.
  3. **Medium-low, confirmed:** a service left in `ReportOnly` behind an enforcing gateway would have served no enforced policy. **Fix:** an enforcing host promotes a lone report-only policy to enforced, as it is (its nonce kept). Test.
  4. **Low, confirmed:** a time inside an hour Damascus skipped (summer time until 2022) threw in `ToUtc` (HTTP 500). **Fix:** it is read as the same time after the change. Unit test.
  5. **Low, confirmed:** the shadow report printed the differences' JSON to every policy reader. **Fix:** JSON for the D-67-4 roles only; a reader sees «فرق مسجّل». Integration test (admin, approver, supervisory reader).
  6. **Low, accepted as designed:** the simulation form leaves out a blank field, while JSON can send `""`. A blank form field means "no value"; equivalent inputs give the same decision (AC-UX-67e, `Ux067PolicyPagesTests`).
  7. **Test, fixed:** AC-UX-67b did not prove a name is shown. It now intercepts the dashboard's HTML in the browser (no test page in any service), adds the span `_UserDisplayName` renders for a known user and for an unknown id, and checks `display-names.js` fills the first with the registry's name and leaves the second «مستخدم غير معروف».
  8. **Test:** AC-UX-67a's draft is now deleted through its own form if a step fails; AC-UX-67c is renamed to what it proves (an administrator sees the JSON, a reader does not; no acceptance persona holds a policy role, and the integration test covers `policy-approver`). Not confirmed: the simulation writes no ledger row (`PolicyOrchestrator.EvaluateAsync` records nothing).
  - Also: `UseHrLocalization` now resolves the calendar service at start-up, so `HrLocalTime.Zone` is the configured zone before the first request and an unknown `Localization:TimeZone` stops the service (P3's note said so; it was resolved lazily). Unit test.
  - A second, shorter pass of the same reviewer covers the rest of P7's checklist (deferred parts, the rollback tag beside this code, server checks' meaning, public links, sentences against the evaluator, guards that cannot fail).
- After the fixes: solution build `-warnaserror` 0/0; unit (`Ux067*`, skip pattern) 70/70; architecture 303/303; integration `Ux067*` 39/39; format clean on the 26 changed C# files.
- Images rebuilt with the fixes from 2026-11-13T18:25:27Z (background).
- **The review, second pass** (same reviewer, the rest of P7's checklist with the prompt, the plan and the study; 7 findings, each checked against the code):
  1. **Confirmed:** P3 reworded the refusal of an unknown fact, and four PLAN-49 checks (`AcPol49EngineScopeTests`, `AcPol49VpsChecks`, `Pol049ShadowLedgerRlsPostgresTests`, `Pol049FactsBuildersMatchSchemasTests`) still looked for the old words, so they could not fail. **Fix:** `PolicyFactsValidator.UnknownFieldMessage`, one constant the validator writes and the four checks read.
  2. **Confirmed (by reading; the least-privilege stage will show it live):** the bootstrap's TEN-002C hardening forces tenant row security on every table with a `TenantId` column, and the gateway sets no tenant, so under the production role the names registry would have written and read nothing. **Fix:** the column is `AgencyId` (the unreleased migration `UX067_UserNameRegistry` edited in place; the lookup already filters by agency in its query), and a test keeps `TenantId` off the table. The gateway's audit table has the same exposure: pre-existing, checked on the least-privilege stack and recorded.
  3. **Confirmed:** the guard did not read a `value` shown in a disabled or read-only field, nor `@ViewBag.X`. Widened: 12 user ids and one raw time surfaced in 12 edit forms of 7 services; they now show the name (`HrUserName`) or the formatted time as read-only text. Planted defects added.
  4. **Confirmed:** a stored rule that the engine's strict reader refuses (it never matches) read as a normal sentence; and «لا يساوي» suggested a missing fact matches, which the evaluator never does. **Fix:** the renderer runs the strict reader and the page marks such a rule «قاعدة غير صالحة — لا تُطبَّق»; NotEquals reads «محدد ولا يساوي». Unit tests.
  5. **Confirmed:** PLAN-49's server check moved its marker to the decision reference (set only at Submit), so a failure before Submit left a draft freeze its clean-up could not find. **Fix:** the drafted change is cancelled by its id.
  6. **Confirmed:** the empty-table rule took any `.Any()` or `Count > 0` within 25 lines as a message. Tightened to `_HrEmptyState` or an Arabic empty text: 69 tables surfaced; 68 got the empty row built from their loop's collection (P4's script), one (`ReceiptRecords/CreateFromOrder`, a `for` loop) an empty state. Planted defect added.
  7. **Confirmed:** the hire page was never checked (optional, unreachable through the list links), and server checks still left «PLAN-4x server check» in approval comments, a loss report's notes and disciplinary notes, which staff see. **Fix:** AC-UX-67a and f open the hire page of agency A's application whose offer is accepted (or, without one, show one with an active offer as accepted while the pages are read, statuses put back after; a stated test-only precondition). AC-WF-45 and AC-WF-46's server checks write Arabic comments and notes; the disciplinary note carries a short token the check finds it by. On the demo the hire page stays optional: its data is not changed for a check.
  - Looked correct: rolling back to PLAN-52's tag beside this code (the extra migration row, the new table, the gateway's antiforgery cookie, `/careers`, times stored in UTC, date inputs at midnight), public links, and the sentences against the evaluator's `all`/`any`/`not`, `In`, and case rules.
- After the second pass: solution build `-warnaserror` 0/0; unit (`Ux067*`, `Pol049*`, skip pattern) 296/296; architecture 308/308; integration `Ux067*` 40/40; format clean on 40 changed C# files; `ux067-validate.ps1` passes, self-test 17/17.
- Images rebuilt with both passes' fixes, on a reset stack, from 2026-11-13T18:49:43Z (background).
- **AC-UX-67, second run** (stack reset and rebuilt, ready 2026-11-13T19:04:41Z; run 1 min 34 s): 6 of 7. 67b's new name check used Playwright's `WaitForFunction`, which evaluates a string in the page: the enforced policy refuses it (no `unsafe-eval`, as intended). It now polls the element from the test.
- Then 67b found a gateway defect older than this plan: `UseStatusCodePagesWithReExecute` re-ran an empty 403 as `POST /Home/ErrorStatus/403`, a GET-only action, so a refused POST to the gateway's own API answered **405** (an employee on `api/user-names/by-ids`). **Fix:** the gateway's own `[ApiController]` endpoints answer their status as it is (the status-code pages feature is off for them); proxied routes and pages keep the error pages. The tests that met this before accept 403 too (`Ac05`, `AcS2s42`, `CrossTenantApiPenetrationTests`).
- Gateway image rebuilt from 2026-11-13T19:09:23Z (no reset).
- **Docker stopped, drive C full (2026-11-13T19:26Z):** the gateway-only restart failed twice on a locked compose temp file, then the engine went away mid-build. Drive C had 1.9 to 3.3 GB free; `docker_data.vhdx` was 266 GB (82 GB after PLAN-52's compaction this morning). Docker's build cache had reached 218.8 GB: six full image builds today (two in P0, four in P7). **My miss:** P-2 asks for the free space before every image build; I read it before the first P7 build (135 GB) and not before the next three.
  - Docker Desktop restarted by itself (19:34Z). This repository's leftovers are removed: the acceptance stack with its volumes (`acceptance-reset.ps1`), 15 untagged images of this repository's builds, and the cached build contexts (3.1 GB). No Testcontainers were left. Nothing of another project was touched.
  - Space freed inside the disk image does not shrink the file: C stays at 1.9 GB, under P-2's 15 GB.
- **BLOCKED (UTC 2026-11-13T19:40Z):** freeing drive C is the owner's setting (P-2). What is left of P7: AC-UX-67 on the final images (6 of 7 passed on the images before the last gateway fix; 67b needs the gateway's 403), P-15's stages, the E2E suite with `Enforce`, the full Testcontainers step, and the P7 commit. The demo server is untouched (PLAN-52's tag, up). Local stacks are down. Keep-awake PID 17544 still runs. Nothing is committed since P6.
- **The owner chose to compact Docker's disk himself (reply 2026-11-13, "I'll compact it").** Before it, so the compaction has something to reclaim: BuildKit's regular layers pruned (by age, then all; 215.7 GB → 0.7 GB, the NuGet cache mount kept). `--max-used-space` removed nothing, as in PLAN-39. C stayed at 1.6 GB throughout. Images (36.7 GB) and volumes (22.4 GB, none of this repository's) untouched. Waiting for the owner's compaction (2026-11-13T19:45Z).
- **2026-11-13T19:52Z, after the owner's "Compaction done":** not done. `docker_data.vhdx` is still 266.5 GB, C still 1.6 GB free, and Docker Desktop has run since 19:33Z (the file is in use, so it cannot be compacted). Still BLOCKED on P-2's 15 GB. Keep-awake PID 17544 had exited; restarted as PID 20356 at 2026-11-13T19:52:50Z.
- **2026-11-13T19:54Z, after the owner's "now there is a lot of free space":** Windows (`Get-Volume`) still reports 1.6 GB free on C; `docker_data.vhdx` is still 266.5 GB; Docker Desktop still runs since 19:33Z. Still BLOCKED on P-2's 15 GB. Keep-awake: processes started from the PowerShell tool exit with it (20356 did); it now runs as a background task of the session, PID 19948, from 2026-11-13T19:54:00Z.
- **Resumed (UTC): 2026-11-14T19:22:35Z.** The owner freed drive C ("C has space now"): 26.3 GB free (P-2's floor is 15 GB). `docker_data.vhdx` is 262.2 GB with about 200 GB free inside it since the cache prune, so builds reuse that space instead of growing the file. Stood still from 2026-11-13T19:40Z (23 h 43 min): waiting for space on C. Docker Desktop restarted at 19:20:10Z with its images; `origin/main` unchanged (aa8be851). No reserved port range covers the acceptance or E2E ports. Keep-awake: PID 21012 (a background task of the session), from 2026-11-14T19:22:55Z.
- **2026-11-14T19:23Z to 19:41Z:** the first rebuild stopped at 19:37Z when Docker Desktop exited during the base layer's `apt-get` (slow downloads from archive.ubuntu.com, repeated `Ign:`); C still had 22 GB. A `docker desktop start` at 19:38Z reported "terminated unexpectedly", then Docker came up by itself at 19:41Z with `docker_data.vhdx` at **50.6 GB** and **237.8 GB free on C**. Images went from 121 to 10 (the cleanup that freed C removed the rest); the 96 volumes are untouched.
- **Rebuild from 2026-11-14T19:41:42Z, cold:** the cleanup that freed C also removed the local base images (`hrm-local-dotnet-sdk`, `hrm-local-dotnet-runtime`) and BuildKit's NuGet cache mount, so the build must download the .NET SDK image, the runtime's Ubuntu packages and every NuGet package (about 2 GB). The connection gives about 5 KB/s to Docker's pulls and 12.7 KB/s to the host (a 5 MB test download from Cloudflare, 19:53Z; ProtonVPN on): many hours. No saved image archive exists on D. The build keeps running in the background in case the connection recovers.
- **BLOCKED (UTC 2026-11-14T19:55Z):** the network. The rest of P7 (AC-UX-67 on the final images, P-15's stages, the E2E suite with `Enforce`, the full Testcontainers step) needs these images. Nothing is committed since P6; the demo server is untouched (PLAN-52's tag).
- **Resumed (UTC): 2026-11-14T19:57:49Z.** The owner turned ProtonVPN off ("VPN is off now"). Stood still 19:55Z to 19:57Z. The host downloads at about 90 KB/s now (a 20 MB test from Cloudflare). The cold build's pulls had stalled on connections opened under the VPN, so it was stopped; instead:
  - the two base images are pulled directly (`dotnet/sdk:10.0.400`, `dotnet/aspnet:10.0`, from 20:00:56Z);
  - PLAN-49's NuGet seed, regenerated for today's 61 lock files: 182 package versions (1.8 GB) copied from the host's cache into `artifacts/plan67/nuget-seed/` (none missing), to fill BuildKit's cache mount without a download;
  - meanwhile the full unit suite runs on the host (no image build beside it).
- SSH needs the VPN on again before P9 (P-2).
- **Full suites on the host (2026-11-14, from 20:02Z):**
  - Unit: 6353/6353, 1 min 35 s.
  - Integration with CI's filter (`Category!=Testcontainers`): first **175 failures**, all EF Core's `ManyServiceProvidersCreatedWarning` (EF builds an internal service provider for each new configuration and throws past twenty in one process). The suite without the PLAN-67 classes passed (413/417), so PLAN-67's tests tipped a suite that sat at that edge (P3's run had passed, 419/423). Each test host adds one provider (its own interceptors).
  - **Fix, in the tests only:** `Ux067UserNamesTests` gives EF one internal provider per database kind (`UseInternalServiceProvider`); `Ux067PortalRoutingTests` builds its host once per class (a class fixture; `IAsyncLifetime` on a test class runs per test, so it had built four); and one HR.Core host (`SharedHrCoreHost`, a collection fixture) now serves `Ux067PolicyPagesTests`, `Ux067ApprovalReviewCommentsTests`, `Ux067LocalTimeInputTests`, `Iam050DelegationInternalApiTests` and `Iam051DirectServiceWriteTests` (each keeps its data apart by its own ids). Bisected class by class before the fix.
  - After the fix: 453/457 (4 skipped, as on `main`), twice.
- Base images pulled 20:00:56Z to about 20:46Z (VPN off); `postgres:16-alpine` too.
- **NuGet seed** (21:47Z to 21:50Z): BuildKit's cache mount went from 131 ids / 680 MB to 180 ids / 1806 MB, 182 package versions complete; no restore went to the network.
- **Images rebuilt from the final code, cold, VPN off:** 2026-11-14T21:50:01Z to 22:20Z (16 images, about 1.5 min each after the first's base layer), stack ready 22:29:17Z (39 min in all, with the infrastructure images the cleanup had removed). Every `hrm-acceptance-*` image created 21:53Z to 22:18Z. C stayed above 199 GB free.
- **AC-UX-67, run on the final images: 7/7 Pass** (1 min 32 s). The 22 key pages: HTTP 200, one policy, no report-only policy, no violation each, the hire page included (agency A's application whose offer is accepted). The planted handler is reported.
- `ux067-validate.ps1 -Headers` on `/careers`, `/careers/track` and `/`: one policy each. Its first run refused the gateway's own policy for the sign-in server in `connect-src`, a host the static check allows (UX-001): the live check now allows a named host in `connect-src` only (self-test 18 refused, 2 clean accepted).
- **P-15, prod-like stages (from 2026-11-14T22:31:58Z), first findings, all caused by this plan's own changes:**
  - **The prod-like journey and AC-INT-37b** (which reads the journey's appointment): the journey posted `DateTime.UtcNow + 30 min` as a `datetime-local` value. The new binder reads it, correctly, on the Damascus clock, so the interview was 2.5 h in the past and refused (status stayed Shortlisted, 2 instead of 5). P3's analysis had missed that the scheduling action refuses a past time. Ten posts in seven test files sent UTC digits this way (interview times, committee dates, delegation windows, issue times, and the same in three server checks). **Fix:** `DamascusClock.Input(utc)` (HRManagement.E2E) formats an instant the way a user in Damascus types it; all ten use it.
  - **AC-POL-49a** (and the same server check) looked for each out-of-scope reason verbatim; the page now prints its codes readably (an effect as its Arabic verb). **Fix:** `PolicyScopeJourney.MissingReasonParts` requires the Arabic text between the codes, word for word; an absent reason still fails.
- **P-15's stages, first pass (2026-11-14, 22:31:58Z to 23:39:14Z; `artifacts/plan67/p15/`):**
  - prod-like journey: Fail (the UTC posts, fixed above); main 44/45 (AC-POL-49a, fixed above); isolated: AC-INT-37 7/8 (37b reads the journey's appointment), AC-MSG-40 8/8, AC-WF-44 5/5, AC-WF-45 8/8, AC-WF-46 9/9, AC-POL-47 1/1, AC-OPS-39 8/8. 19 min.
  - overlays: AC-POL-49 Enforce 2/2, AC-S2S-42 9/9, AC-WF-46 orphans 1/1. 11 min.
  - lab: main 152/163 (2 skipped): two required failures, **AC-EST-10** (a connection reset while fetching a token, the known one-off) and **AC-WF-23f** (it read the inbox's English «legacy» badge and the raw type `ApprovedCV`); seven optional ones, recorded and not chased: AC-08, 19, 30 (their scripts need PowerShell 7, PLAN-43 §6), AC-21 (the same: `payrollruns` read by a PS 5.1 script), AC-23, 24, 25 (the lab seed's 1102 employees with an open service-regime exception refuse payroll; payroll is PLAN-58/59's). Journey 1/1, AC-HCM-26 5/5 (its interview times now on the Damascus clock), AC-HCM-27 4/4. 26 min.
  - leastpriv: AC-OPS-38 and AC-TEN-48 9/9. 11 min. The stack is torn down at the end of the stage, so the names registry was not read live under the production role; the rename to `AgencyId` keeps the table outside TEN-002C, which only hardens `TenantId` columns.
- **The inbox's English badges** («legacy», «workflow», «N legacy») and the postings list's «legacy» fallback are Arabic now; the guard's English-label rule now reads literal badges too (a badge whose tag holds Razor code is left out), with a planted defect. Architecture 309/309. AC-WF-23f reads the Arabic badge and the type's Arabic name.
- **Reruns after the fixes (2026-11-14T23:40:24Z to 2026-11-15T00:02:22Z, one cached image build):** prod-like journey 1/1, AC-INT-37 8/8, AC-POL-49 in main Pass; lab AC-WF-23 and AC-EST-10 7/7. AC-UX-67a and f failed: no draft policy version was left for the rules editor (an earlier class in the group had published or discarded the seed's). **Fix:** AC-UX-67 opens the editor on agency A's draft, or makes one through the template's own page (`CreateTemplateDraft`) and discards it through the editor's form after.
- **Destructive forms without a confirmation:** the editor's «إهمال المسودة» and 13 others in 7 services (withdraw a disciplinary action, a card request, a candidate's application; cancel a leave, an approval request, an interview, a payroll job, a disposal request; revoke a delegation, an offer, a permission; remove a rule, a plan line) submitted at once. Each now asks first (`data-hr-confirm`, danger); the public portal's layout gets the shared dialog and `shared-ui.js` for the candidate's withdrawal. A new guarded rule, `unconfirmed-destructive`, refuses such a form, with planted defects. No browser test clicks these buttons. Architecture 313/313.
- **Final runs on the final images (2026-11-15):**
  - prod-like main group, whole (00:02:44Z to 00:20:11Z, one cached build): 44/45. All seven AC-UX-67 scenarios Pass, the rules editor reached on a draft made through the template's page and discarded through its (now confirmed) form; AC-POL-49 and the rest Pass. AC-TEN-48 was Blocked as it should be when the main group runs without the journey before it on that stack.
  - journey and AC-TEN-48 together (00:20Z to 00:25:30Z): 1/1 and 1/1.
  - The acceptance stack is down with its volumes (`acceptance-reset.ps1`).
- **The E2E suite with `Enforce` (P-15)** was not run on this machine: CI's preparation (`prepare-ci-compose-env.sh`) copies `.env.ci` over the developer `.env` and needs PowerShell 7 and Python (`merge-keycloak-realm-extensions.py`), and neither is installed here; installing them is the owner's change. The PR's E2E check runs it on `docker-compose.ci-e2e.yml`, which enforces the policy on all fifteen applications. A deviation, recorded for the report.
- **P7's last checks (2026-11-15T00:25Z to 00:33Z):** solution build `-warnaserror` 0 warnings, 0 errors; `ux067-validate.ps1` passes (self-test 18 refused, 2 clean accepted); `dotnet format --verify-no-changes` on the 57 changed C# files clean; unit subset (AcceptanceSkipPattern, Qa066, Ux067, Pol049) 427/427; `AcceptanceCiScopeTests` 18/18. Test-rewritten `testdata/**/out` files restored (131) before the commit; `acceptance-evidence/` unchanged.
- Finished (UTC): 2026-11-15T00:34:20Z — P7 committed (1ee455bf).

## P8 — the merge from main

- Started (UTC): 2026-11-15T00:34:20Z.
- `origin/main` is still aa8be851 (fetched 00:34Z), the commit this branch starts from, so there is nothing to merge.
- Finished (UTC): 2026-11-15T00:34:40Z.

## P9 — the demo server

- Started (UTC): 2026-11-15T00:34:40Z.
- **The full Testcontainers step on the host** (CI's filter `Category=Testcontainers&Category!=ObjectStorage`, Release, VPN off, on `1ee455bf`; for P10's gate, as nothing it builds or tests changes in P9): **227 passed, 1 skipped** (`Ops038BootstrapIdempotencyTests`, by its own condition), 3 min 41 s, finished 2026-11-15T00:39:35Z. No `testdata` file rewritten.
- Drafted while waiting (uncommitted, for P10): `reports/plan67-final-report.md` (the server's sections left to fill), the plan's Status and «ملاحظات التنفيذ والانحرافات».
- `ssh -o BatchMode=yes -o ConnectTimeout=15 hrm-vps` (00:34:40Z): `connect to host … port 22: Connection timed out`, with ProtonVPN off.
- **BLOCKED (UTC 2026-11-15T00:39:50Z): port 22 is blocked at P9's start (P-2).** The owner turns ProtonVPN on. Done and committed: P0–P7 (`1ee455bf`), P8 (no merge). The demo server is untouched (PLAN-52's tag `92a69960c826`). Local stacks are down; keep-awake still runs (a background task of the session).
- **Resumed (UTC): 2026-11-15T06:34:19Z.** The owner turned ProtonVPN on ("VPN is on"). Stood still from 00:39:50Z (5 h 54 min): port 22 blocked without the VPN. `ssh hrm-vps whoami` → `hrm`; the ED25519 host key matches the recorded one (compared by value, not written here); the running tag `92a69960c826` (previous `d0e446fe3280`); 22 containers; 18 GB free on the server's disk.
- **Backup (P-18):** `/opt/hrm/backups/pre-plan67-20261115T063540Z` (700; `pg_dumpall.sql` 31 MB, `env.backup`, `SHA256SUMS`, each 600; both sums OK; 17 `CREATE DATABASE`: the 16 databases and `hrm_admin`; the dump ends with "cluster dump complete"). Undo: delete the folder when no longer needed.
- **Read-only picture before the deploy (P-17):** tag `92a69960c826` on `origin/main`; 22 containers (20 with a health check, all healthy; `hrm-seq-1` and `hrm-registry` have none); the portal's four postings unchanged since P0: `b4e13f41…` «محاسب» («PLAN-43 server check», REQ-2026-00004 Published, closing 2026-11-15, today in Damascus; no application of either kind), `46635d93…` (REQ-2026-00003, test text, not a server check's), and two seeded postings without a requisition.
- **Build:** archive of `1ee455bf1a6f` 32.4 MB, uploaded in 26 s (through the VPN); `build-on-host.sh` started detached (`setsid nohup`, `/opt/hrm/build-1ee455bf1a6f.done`) at 06:38:28Z; 17 GB free on the server's disk.
- **Build, attempt 1 failed (07:09:14Z, 30 min 46 s):** 16 of 17 images built; `hr-migrate` stopped at the EF bundle of dev-analytics ("Build failed", after its compile had succeeded) while the server's disk reached **98% (3.3 GB free)**: the build cache had grown to 13.3 GB beside 61 GB of images. The running demo was not touched (tag `92a69960c826`, all healthy). **My miss:** 17 GB free was too little beside a cold build (PLAN-52's built from 29 GB); I should have freed space first.
  - Freed, as `hrm`: the build cache (`docker builder prune -af`, 13.3 GB; the build script removes it after every successful build anyway), then the local images of seven older releases (`dbf53280121d`, `ae95ec2fec82`, `a504f6e54921`, `9560b03e51a3`, `8abd5aa7fe93`, `61816ed7dda2`, `046316bde9e7`), which stay in the loopback registry (checked: their tags are listed). Kept: the running `92a69960c826`, the previous `d0e446fe3280`, and the new tag's 16 images. **Undo:** `docker pull 127.0.0.1:5050/hrmanagement/<image>:<tag>`. Disk: 56 GB free (62%).
- **Build, attempt 2** started detached at 07:13:05Z (same archive and script).
- **Build, attempt 2:** passed, 07:13:05Z to 07:48:42Z: build 2077 s, push 37 s, 17/17 images in the loopback registry (`/opt/hrm/build/images-1ee455bf1a6f.json`); the script removed the source, the build cache and the SDK image; 51 GB free after.
- **Deploy** (`scripts/vps/deploy.ps1 -Tag 1ee455bf1a6f`, 07:49:01Z to 07:52:56Z, first attempt): pull 5 s, up 139 s, bootstrap 63 s (**1 migration applied**: the gateway's `UX067_UserNameRegistry`, by `gateway_audit_migrator`; TEN-002C verified on every database), keycloak-reconcile 15 s, healthy 15 s; services away about 3 min at most. `deployed 1ee455bf1a6f (previous: 92a69960c826)`. Smoke `-RequireIntegrationWiring` 50 pass, 0 fail, 1 warn (the client-side port probe, as before). 22 containers, every one with a health check healthy.
- **The residue closed (D-67-6), 07:54:17Z:** as `hradmin`, through the requisition's own «إغلاق» action (the POST its form sends): requisition `REQ-2026-00004` (`23b42bbe-0980-460f-94f4-ca38f3f3a5d6`) Published → Closed; its posting `b4e13f41-3d6b-42e7-b33c-d3ec26b9a875` («محاسب», «PLAN-43 server check») unpublished; no application of either kind was open, so the close rejected or cancelled nothing. Nothing deleted. **Undo:** the application has no reopen action: restore from `/opt/hrm/backups/pre-plan67-20261115T063540Z`, or, narrowly, set the requisition back to `Published` (`ClosedDate` NULL) and the posting's `IsPublished` true. The portal now shows 3 postings. The new opt-in check `AC_UX_67_vps_close_the_public_postings_of_the_server_checks` (`UX067_CLOSE_RESIDUE=true`, skipped otherwise) did it; its first run asserted `f` where PostgreSQL's text is `false` (the close itself had succeeded), so it was fixed and rerun: found 0, Pass.
- **`AcUx67VpsChecks` on the new tag** (07:55:28Z to 07:59:11Z): the portal, the policy page and the confirmation/comments checks passed (3/3); the CSP check reached 19 of the 22 key pages, each with one policy and no violation, but, as in P0, the demo holds no pending approval and no policy draft, so the approval review and the rules editor were not reached (the hire page is optional on the demo: no application with an accepted offer). **Fix in the check:** outside the before run it makes a pending approval (a stated test-only precondition, as the confirmation check's) and a policy draft through the template's own page as `hradmin` (the last active template without a draft, never `Contracts.Leave`, which AC-POL-47's check copies), opens them, and removes both in `finally` (the approval rows deleted, the draft discarded through the editor's form). Rerun (08:00:36Z to 08:03:36Z): **Pass**. 21 of 22 key pages: HTTP 200, one CSP header, one policy, no violation (the hire page optional); 42 «after» screenshots under `artifacts/plan67/vps/after/screenshots/`. After it: 0 policy drafts, 0 marked approval requests.
- **The server suites** started 08:04Z (`AcOps36VpsBootTests` without 36j and without the PLAN-67 checks already run, `AcInt37VpsServerTests`, the server checks of PLAN-38..52), nothing heavy beside them, no ssh from the agent meanwhile.
- **The server suites** (08:04:05Z to 08:37:49Z, 33 min 42 s; `artifacts/plan67/vps/suites/suites-after.*`): **48 of 49**: `AcOps36VpsBootTests` without 36j (excepted as before), `AcInt37VpsServerTests`, and the server checks of PLAN-38..52. The one failure, `AC_WF_46_vps_the_sweeps_run_and_the_metrics_exist` ("No cycle of the CardRequest sweep in the log of idcard-web"), is the one PLAN-46..52 met: AC-OPS-39f recreates the services and the check reads the last 5000 lines of a noisy log. It is rerun alone after the next cycle.
- The suites rewrote 37 committed evidence files under `acceptance-evidence/` and added 4: copied to `artifacts/plan67/vps/acceptance-evidence-from-run/` and restored (P-2).
- **AC-WF-46's sweeps check rerun alone** right after the next cycle (08:41:20Z): **passed** (12 s).
- **After the suites** (`AC_UX_67_vps_after_the_suites_…`, 08:42Z): **Pass**. The registry knows **14 users** (`hradmin` and the `e2e-*` users the suites signed in as). Of the people the policy, approval, requisition and establishment pages print (3 distinct ids in those columns on the demo), **0 stay «مستخدم غير معروف»**; no published posting carries a server check's marker.
- **At the end** (08:44:33Z): tag `1ee455bf1a6f` (previous `92a69960c826`), 22 containers running, every one with a health check healthy, 51 GB free; `smoke.ps1 -RequireIntegrationWiring` 50 pass, 0 fail, 1 warn. No committed evidence left rewritten.
- **The demo for the owner:** the portal styled at `/careers` with three postings and no test posting; Arabic labels and statuses, names, times in Damascus time; confirmations before destructive actions; the policy rules as sentences.
- Finished (UTC): 2026-11-15T08:45:36Z — P9 committed (3579161f).

## P10 — docs, local gates, final report, the merge

- Started (UTC): 2026-11-15T08:45:36Z.
- The server suites also wrote `reports/msg040-replay-server-20261115T080927Z.json` (AC-MSG-40's replay report): moved to `artifacts/plan67/vps/`, not committed.
- **Docs** (P10.1): `UX_001_VERIFICATION`, `UX_003_CONFIRM_DIALOGS` (also the fourteen forms P7 found), `L10N_001_LOCALIZATION`, `GATEWAY_RUNBOOK` (also the gateway API's status codes), `CI_AND_TESTING` (the ten rules), `OPS_VPS_SINGLE_HOST` (what the demo users see, the closed posting and its undo, the backup, the change table, the rollback note, the deploy), the study's ten status lines, `PROGRAM_COMPLETION_ROADMAP`, the plan's Status and «ملاحظات التنفيذ والانحرافات».
- **Checked on the demo, read-only, for the report:** the gateway's audit tables carry forced tenant row security; in demo mode the gateway connects as the administrator (bypasses it), so the production-role risk stays unverified (§13).
- Final report written: `reports/plan67-final-report.md` (2026-11-15T08:47:58Z).
- **`ci-local.ps1 -SkipTestcontainers` on `dd10a474`** (08:50:00Z to 09:05:35Z): **failed at the unit tests**, two failures, both mine:
  1. `A_time_in_an_hour_the_clocks_skipped_…` passed on Windows only. In ci-local's Linux image, .NET reads the tz database's Asia/Damascus so that the offset changes at midnight on 2021-03-26 (23:30 is +02:00, 00:30 is +03:00) while `IsInvalidTime` stays false for every half hour of 2019 to 2022 (probed in the image), so the test found no skipped hour. **Fix:** every hour of 2021, the night of the change included, converts without failing the request on both platforms; where the platform marks the hour skipped (Windows), it is read as the same time after the change. `HrLocalTime.ToUtc` is unchanged.
  2. `AcceptanceSkipPatternArchitectureTests`: the residue check's opt-in `Skip.IfNot` (`UX067_CLOSE_RESIDUE`) is a raw skip. **Fix:** no gate: the check closes, through the application, any public posting a server check left, and normally finds none (it acts on the checks' own residue only, D-67-6).
  - Unit `Ux067*` and the skip pattern test 73/73; format clean on both files.
