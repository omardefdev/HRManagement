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
