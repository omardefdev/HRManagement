# Task: FIX-CAREERS-REF — The candidate sees his reference number, and every refusal, on the public careers portal

The deliverables:
- **A candidate who applies on `/careers` sees his reference number** on the confirmation page, through the gateway, with production settings, for a posting and for a competition alike.
- **Every refusal the portal reports reaches the candidate:** an unavailable posting, a duplicate application, a closed competition.
- **The confirmation page is never blank:** when the number can no longer be shown (a reload), it says so, and says what to do.
- **Tests that read the number from the page,** not from the database, so the defect cannot come back unseen.
- **The demo server** runs the fix.

This is a side task outside the plan sequence, with no plan number. It runs in one session with no owner action: the owner asked the agent to merge, so you merge after green checks.

## How to run this task — read before anything else

- **One session at a time** (owner, 2026-11-07). Start only when no plan or side-task session is running: after PLAN-60's merge, and before DEMO-PAY or PLAN-61. If a session is running, write `BLOCKED:` in one line.
- **Run it as phases F0–F5 in order.** Keep a checkpoint in **`reports/fix-careers-ref-progress.md`**:
  - its first line is `Started (UTC): <timestamp>`, the timestamp taken from `date -u +%Y-%m-%dT%H:%M:%SZ`;
  - it records each phase's `Finished (UTC):`, every server change with its undo command, and the exact next step;
  - never edit an earlier stamp.
- **If you are resuming:** read the progress file and `git log --oneline -20` first, and continue from the recorded next step.
- **Record the durations** of the image builds, the stack starts, each acceptance run, the GHCR build, the deploy and every CI run, and any time the session stood still, with its cause.
- **Merges** (the owner's standing instruction since PLAN-38): you push the branch, open the PR, wait for the checks and merge with `gh pr merge <n> --merge` **only when every check is green**. Never ask the owner whether to merge. Never use «Merge when ready» or auto-merge.
- **Waiting:**
  - wait in the turn, never by ending it: `gh pr checks <n> --watch --interval 60` with a limit of about five minutes, again and again until every check ends;
  - for the GHCR build, `gh run watch <id> --exit-status` the same way;
  - poll a long background job in calls of about five minutes at most;
  - a turn ends only with the final reply, `BLOCKED:` or `WAITING-CI:` (the PR, its run, the next step);
  - stop a process by its id, never by a pattern.
- **The machine's rules are the plans'** (P-2 of the PLAN-59 and PLAN-60 prompts):
  - keep it awake with `systemd-inhibit`, stopped by its PID;
  - at least 12 GB free before an image build;
  - images one service at a time from the local base images;
  - before a stack start, nothing heavy beside it and a load below the number of CPUs;
  - one heavy job at a time;
  - `down -v` when done.
- **Economy:** at most two subagents at a time, one independent review before the push. Long outputs go under `artifacts/fix-careers-ref/`; nothing there is committed.
- **Blocking:** when a Stop condition is hit, write `BLOCKED:` with the question and options, commit what is complete, and end the turn.

## The defect, as the code shows it (read on `main`; confirm in F0)

- **The page reads the number from TempData:**
  - `PublicCareersController.Apply` saves the application with its number (`CodeGenerationRequests.CitizenReference()`). It then sets `TempData["Reference"]` (about `:207`) and redirects to `Submitted`;
  - `ApplyCompetition` does the same (about `:335`);
  - `Submitted` (about `:361-366`) shows `ViewBag.Reference = TempData["Reference"]` in `Views/PublicCareers/Submitted.cshtml`, a badge under «احتفظ بالرقم المرجعي التالي لتتبّع حالة طلبك». **With no value the badge is empty,** which is what the owner's test saw.
- **The gateway drops that cookie on the careers route.**
  - `GatewayUpstreamCookieForwarding.BuildUpstreamCookieHeader` (`src/Shared/HRManagement.ServiceDefaults/`, about `:32-55`) forwards the antiforgery cookies, plus either the TempData cookie (`.AspNetCore.Mvc.CookieTempDataProvider`) **on staff routes only**, or `careers_session` on the careers route;
  - `GatewayAccessTokenTransform` (about `:46`, `:75`) calls it with `forwardCareersSession: true` for `careers-public` (`/careers/{**catch-all}`);
  - so the browser sends HR.Core's TempData cookie with the request for `/careers/submitted`, and the gateway removes it before HR.Core sees it;
  - PLAN-42 (`4cb066f8`) added the TempData forwarding, for the staff routes alone. `GatewayUpstreamCookieForwardingTests.BuildUpstreamCookieHeader_on_the_careers_route_still_drops_every_other_cookie_name` pins the drop.
- **The same loss hides every refusal on the portal.** Nine `TempData["Error"]` messages of `PublicCareersController` are lost the same way: about `:86`, `:96`, `:112`, `:123`, `:234`, `:242`, `:249`, `:256` and `:331`. They are shown by `Views/Shared/_PublicLayout.cshtml` (about `:148-155`) and `Views/PublicCareers/Competition.cshtml` (about `:47`).
- **The cookie stays HR.Core's own.** On the careers route HR.Core's path base is `/careers` (`ProductionHostExtensions.UseHrConfiguredPathBase`). So its TempData cookie is scoped to `/careers`, apart from the staff pages' cookies, and only HR.Core's keys can read it. It carries no identity.
- **Why no test saw it:**
  - AC-HCM-30 (`AcHcm30CandidateExperienceTests`, lab `main`, required) says «Ids and references the UI shows only through TempData are read by SQL», and asserts only the final path `/careers/submitted`;
  - AC-HCM-31 (`AcHcm31CompetitionTests`) does likewise;
  - so do both recruitment journeys (`RecruitmentToHireJourneyE2ETests`, lab `journey`; `RecruitmentToHireJourneyProdLikeE2ETests`, prod-like `journey`).
- **A second, separate gap (record it; do not change it):**
  - `Notifications:Candidate:Provider` is `None` in `HR.Core.Web/appsettings.json`. The acceptance stacks set `FileDrop`; Production and the demo set nothing;
  - so on the demo, the «تم استلام الطلب» message (`APP_RECEIVED`, which carries the number) and the portal's one-time code stay pending (`CandidateNotificationDispatcher`);
  - `/careers/track` (the number and the national id, no code) works; `/careers/my` signs nobody in there.
  - A real provider (SMTP or SMS) is the owner's decision, with its credentials.

## Decisions

| # | Decision |
|---|----------|
| F-1 | **Where and how.**<br>• Branch `fix/careers-reference-number` from `origin/main`, in the main folder. Commit this prompt first.<br>• Leave the last plan's merge-note branch untouched: the next plan brings it in. |
| F-2 | **The fix in the gateway:**<br>• on the careers route, forward the TempData cookie by its exact name, beside the antiforgery cookies and `careers_session`;<br>• every other cookie stays dropped, and the careers route still never carries staff credentials (`Authorization` removed);<br>• the comments say why: HR.Core's own flash cookie, scoped to `/careers`, carrying no identity;<br>• `GatewayUpstreamCookieForwardingTests` follow: the careers route keeps the three names, and still drops near-names (`x.AspNetCore.Mvc.CookieTempDataProvider`, `.AspNetCore.Mvc.CookieTempData`) and every other cookie. The pinned test changes with that reason. |
| F-3 | **The confirmation page:**<br>• it shows the number in its badge;<br>• with no number (a reload, an expired cookie, a direct visit), no empty badge: an Arabic message says the number is shown once, and that the agency's recruitment office can help. It adds that a consenting candidate also receives the number by message only when a provider is configured (`Notifications:Candidate:Provider` is not `None`), so the demo never promises a message that cannot come;<br>• **the number never goes into a URL, a log or an evidence file name,** and is not kept for a second read;<br>• the same page serves both apply paths;<br>• `InterfaceTextGuardTests` and the readable-interface rules of PLAN-67 hold. |
| F-4 | **Tests that read the page:**<br>• **AC-HCM-30a:** the confirmation page's badge, decoded, equals the application's number. A refused apply (the duplicate application on the same requisition) shows its Arabic message on the page it lands on, through the gateway. The class's note about TempData changes;<br>• **AC-HCM-31:** a competition apply shows its number the same way;<br>• **both recruitment journeys** read the number from the page, and only check it against the database;<br>• a unit or integration test of the transform proves the careers request reaches HR.Core with the TempData cookie and without `Authorization`, if the gateway's tests can host it (`GatewayRoutingConfigurationTests`, `S2s042GatewayProofV2Tests` are the neighbours). |
| F-5 | **The demo server** (the GHCR path of `docs/OPS_VPS_SINGLE_HOST.md`):<br>• a backup first (`/opt/hrm/backups/pre-fix-careers-ref-<utc>/`, verified), and the running tag recorded;<br>• `gh workflow run deploy.yml --ref fix/careers-reference-number -f action=build`, then `scripts/vps/deploy.ps1 -Tag <sha12>`, again at once if it stops at `keycloak-reconcile`. No migration;<br>• **rollback:** `scripts/vps/deploy.ps1 -Rollback` to the previous tag is safe at any time (a cookie filter and a view, no data);<br>• **a server check that writes nothing** (a new file `AcCareersRefVpsChecks.cs` of the partial class `AcOps36VpsBootTests`): a token from `/careers/track`, then an apply to an unknown posting id, which writes nothing and lands on `/careers` with «الإعلان غير متاح للتقديم» on the page. That proves the flash crosses the gateway on the demo. No application is created on the demo. |
| F-6 | **No regression:**<br>• the staff routes forward exactly what they did;<br>• the antiforgery and `careers_session` behaviour is unchanged;<br>• `/careers/track` and `/careers/my` work as before;<br>• the gateway's proof, path authorization and routing tests stay green. |

**Never:**
- run while another session runs;
- forward any other cookie, or staff credentials, on the careers route;
- put the reference number in a URL, a log or an evidence name;
- read the number by SQL in a test that proves the page;
- create an application on the demo, or configure a notification provider;
- weaken a guard or a test to get green;
- commit evidence or anything under `artifacts/`;
- touch the side PRs, the owner's branches, or another project's Docker objects;
- print a secret, or leave the demo down.

**Stop (`BLOCKED:`) if:**
- another session is running;
- F0 finds a different cause: the cookie is not written, the response's `Set-Cookie` does not reach the browser, or HR.Core's path base on the careers route is not `/careers`. Describe it, with the evidence;
- the server's free space is under 15 GB before the pull;
- a deploy fails, and neither the rollback nor the restore brings the demo back.

---

## F0 — Preconditions and the proof of the cause

1. **Preconditions:**
   - `Started (UTC)`, and no other session;
   - `git fetch origin`: the last plan merged, and a clean tree but for the owner's `global.json` and `tmp-vendors.html`;
   - `ssh -o BatchMode=yes hrm-vps whoami` prints `hrm`, with the host key `SHA256:qWIfQls882KM/nQ99yV/WFU2Wk08t6DIb8o+pUMGsto`;
   - `gh auth status` with push rights.
2. **Reproduce before fixing,** on a local stack with the gateway (lab), as AC-HCM-30 does: apply to a posting through `/careers`. Record:
   - the `Set-Cookie` of the redirect;
   - the cookie the browser sends for `/careers/submitted`;
   - the cookie HR.Core receives (the gateway's forwarded header, in a debug log of the transform or a test);
   - the empty badge.
3. Read the same on the demo's careers page, read-only: an unknown posting's apply refusal shows no message.

Commit: `chore(careers-ref): the defect reproduced — the gateway drops HR.Core's flash cookie on the careers route`.

## F1 — The fix and its tests (F-2, F-3, F-4)

- The cookie filter, the confirmation page's empty state, the tests.
- **Tests:**
  - build with `-warnaserror` and the Release build;
  - `GatewayUpstreamCookieForwardingTests` and the gateway's other tests; the unit suite and the format check;
  - `InterfaceTextGuardTests`.

Commit: `fix(careers-ref): the careers route forwards HR.Core's flash cookie, so the candidate sees his reference number and every refusal; the confirmation page is never blank`.

## F2 — Acceptance on the local stacks

- Build the images from this tree, one service at a time.
- **Lab stack:**
  - `AcHcm30CandidateExperienceTests`, `AcHcm31CompetitionTests` and `RecruitmentToHireJourneyE2ETests`;
  - `AcHcm22RecruitmentWorkflowsUiTests`, as a neighbour on the staff side.
- **Prod-like stack:** `RecruitmentToHireJourneyProdLikeE2ETests`, and the portal check of `AcUx67ReadableInterfaceTests`.
- Then `down -v`, with the committed evidence restored.
- **The independent review** (one fresh subagent) reads the diff against this prompt. It looks for:
  - another cookie, or a credential, crossing to the careers route;
  - the number in a URL or a log;
  - a test still reading the number by SQL to prove the page;
  - a staff route's behaviour changed.

Commit: `test(careers-ref): AC-HCM-30, AC-HCM-31 and both journeys read the reference number from the page`.

## F3 — The PR

- **Local gates:** the Testcontainers step with CI's filter, the Release build, the local CI steps, and `scripts/plan035-prepush.ps1`.
- **Then, in the same turn:**
  - push `fix/careers-reference-number`;
  - `gh pr create --base main --head fix/careers-reference-number --title "FIX-CAREERS-REF: the candidate sees his reference number on the careers portal"`, with a short body from `reports/fix-careers-ref-pr.md`.
- Wait for the checks beside F4. A red check is fixed on the branch, which is pushed again.

## F4 — The demo server (F-5), beside the PR's checks

1. **The backup**, verified; the running tag; the free disk.
2. **The GHCR build, then the deploy.** Record the build's run and duration, the deploy's stretches and the downtime.
3. **The checks**, each under `--blame-hang-timeout 20m`:
   - the new `AcCareersRefVpsChecks` test;
   - `AcOps36VpsBootTests.cs`'s own tests (`AC_OPS_36a`..`k`, 36j excepted);
   - `AC_UX_67_vps_the_portal_is_styled_at_careers_and_the_module_root_opens_the_dashboard`;
   - `scripts/vps/smoke.ps1 -RequireIntegrationWiring`.
   This machine's known flickers are rerun alone.
4. **In `docs/OPS_VPS_SINGLE_HOST.md`:** the change, the backup, the rollback, and the note on the notification provider (the second gap of «The defect»).

Commit (pushed to the branch): `test(careers-ref): the demo shows the portal's flash messages through the gateway; the boot, portal and smoke checks green`.

## F5 — The report, the merge, the note

1. **`reports/fix-careers-ref-final-report.md`:**
   - the cause, with its evidence;
   - the fix;
   - the tests that changed, and why;
   - the demo's before and after;
   - the notification gap, for the owner's decision;
   - the timing.
2. **Merge** when every check is green, then `git switch main; git pull --ff-only`.
3. Record the merge in `reports/fix-careers-ref-progress.md` on a local branch `docs/fix-careers-ref-merge-note`, with no second PR.
4. Stop the keep-awake process by its PID.

## Definition of done

- On the prod-like stack and on the demo, through the gateway, a candidate's confirmation page shows his reference number, and the portal's refusals reach him.
- The confirmation page is never blank.
- AC-HCM-30, AC-HCM-31 and both journeys read the number from the page, and are green in CI.
- The staff routes, the antiforgery and the portal session are unchanged.
- The demo runs the fix. Nothing was created there, and the demo was never down.
- The merge is done by you after green checks.

Then reply in Arabic, briefly:
- the cause in one sentence;
- what changed for candidates;
- the tests that now guard it;
- the demo's tag;
- the notification gap and the decision it needs;
- the time it took;
- the PR link.
