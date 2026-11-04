# Task: PLAN-41 — The acceptance job runs in CI, and the acceptance baseline tells the truth

The deliverables:
- **The `Acceptance` job** in `ci.yml` runs the same stack the tests are written for (`hrm-acceptance`, the acceptance-v2 realm with passwords generated on the runner, File.Storage `Local`), first on the lab profile, then on the prod-like profile, under `ACCEPTANCE_REQUIRE_ALL=true`. It becomes a required check.
- **`acceptance-evidence/summary.md`** states, for every AC: status, SHA, image source, profile, freshness, whether the test proves its claim, and the owning plan. No contractual, stale or incomplete «Pass» counts as a pass.
- **A blocked required scenario turns the job red.** It never passes silently.

It runs in one session with no owner action: the owner asked the agent to merge, so you merge after green checks.

## How to run this task — read before anything else

This is a **single prompt for one session**. Run it as phases **P0–P8** in order.

**There are no owner actions in this plan.** Every decision is taken (see «Decisions»).

**Standing owner instruction on merges:**
- for PLAN-38: «إن لم يكن مدموجاً اجعله يدمجه هو»;
- for PLAN-39: «نفذها انت»;
- for PLAN-40: the same.

So **you** push the release branch, open the PR, watch the checks, and merge — but **only when every check is green** (P-11).

If something truly needs the owner (a Stop condition), write `BLOCKED:` and end the turn.

**At the end of every phase:**
1. Build: `dotnet build HRManagement.slnx -warnaserror`, 0 errors.
2. Run the phase's tests.
3. Commit, using the phase's commit message.
4. Update **`reports/plan41-progress.md`** — your checkpoint. It contains:
   - phase status;
   - decisions (with reasons);
   - files changed;
   - test results;
   - every GitHub change (variables, protection) and its undo command;
   - open issues;
   - the exact next step.

**If you are resuming:** at start, or after any context compaction or restart, read `reports/plan41-progress.md` and `git log --oneline -20` **first**. Continue from the recorded next step. Never redo a committed phase. Check the state of the PR and of its runs (`gh pr view`, `gh run list`) before any remote step.

**Timing (mandatory, the owner compares plans):**
- The **very first action** of the session is to create `reports/plan41-progress.md`. Its first line is:
  `Started (UTC): <timestamp>`
  where the timestamp comes from `(Get-Date).ToUniversalTime().ToString('o')`.
- Every phase records `Finished (UTC):`.
- Every resume records `Resumed (UTC):` with the reason.
- Record the duration of the class inventory, image builds, stack starts, each local acceptance run, and **every GitHub Actions run** (queue time, wall-clock, and the duration of each step of the `Acceptance` job).
- Never edit an earlier stamp.

**Economy mode** (the owner runs this session **without ultracode**, at `xhigh`):
- Use **at most two subagents at a time**. Good uses:
  - the class inventory in P0, read-only;
  - root-causing separate CI failures.
- Run **one independent review** at the end. Use a refuter only for findings rated high.
- Write long outputs (inventories, logs, trx files, downloaded artifacts) under `artifacts/plan41/`. Read only the failing parts.
- Do not re-read large documents you have already read in this session; take notes in the progress file instead.

**Waiting for CI.** This plan has two or three long remote runs (an `Acceptance` run may take about an hour).
- Wait with whatever mechanism this session allows: a background `gh run watch <id> --exit-status` if permitted, or the app's run monitor.
- If the session cannot wait (PLAN-40 found that the desktop app does not allow polling CI from the session), end the turn with a line that starts with **`WAITING-CI:`** and names the PR, the run id, its URL and the exact next step.
- On the owner's next message (even «اكمل»), read `gh pr checks <n>` **once** and continue from that step.
- Never merge on a guess. A check that is still pending means: wait again.

**Blocking decisions.** When a "Stop" condition is hit:
1. Write `BLOCKED:` with the question and options.
2. Commit what is complete.
3. End the turn. **Do not guess.**

---

## Role

You are a senior .NET test and CI engineer:
- GitHub Actions (jobs, concurrency, artifacts, `workflow_dispatch` inputs, required checks, branch protection through `gh api`);
- Docker Compose and BuildKit on Linux runners;
- xUnit, trx files, and the acceptance harness of this repository (`AcceptanceGate`, `EvidenceRecorder`, `[RequiredAcceptance]`);
- PowerShell 5.1 **and** PowerShell 7 on Linux, and bash.

You work on the `HRManagement` repository on the owner's Windows machine (PowerShell **5.1 only, no `pwsh`** unless the owner installs it, Docker Desktop, culture ar-SA, Windows OpenSSH, `gh` signed in as `omardefdev` with admin rights). The GitHub runner has `pwsh` 7.

**Where things stand:**
- **The `Acceptance` job** exists in `ci.yml` but is gated off by the repository variable `ENABLE_ACCEPTANCE_JOB` (dated obligation `CI-ACCEPTANCE-DISABLED` in `docs/qa/governance-exceptions.json`, owner PLAN-41, review by 2026-12-14). Its steps are stale: the plain compose on port 5000, a `Demo` realm, `ACCEPTANCE_PERSONA_PASSWORD`, the 5433 database port, no `hrm-acceptance` project.
- **The post-merge dedupe guard** (PLAN-37, `scripts/ci/dedupe-push-run.sh`): the push run on `main` skips the heavy jobs when the pull request already tested the exact tree in a successful run.
- **Branch protection:** `docs/scm-governance/branch-protection-main.json` lists `CI`, `CodeQL`, `Gitleaks`, `E2E`, and `Acceptance` as pending. Live protection was impossible while the repository was private on the free plan (exception `SCM-NO-GITHUB-PROTECTION`, local pre-push guard as fallback). PLAN-38 and PLAN-40 found the repository **public**. Check it (P0).
- **The acceptance project** runs its classes one at a time (`AssemblyInfo.cs`: `DisableTestParallelization = true`). Several classes stop or recreate containers (AC-17, `AcMsg40` 40d, `AcOps39` 39f; PLAN-40 readiness note), which breaks the classes that run after them.
- **The demo server** runs `f44e52cf58be`. **This plan makes no server change.**

## Read first (mandatory)

1. `docs/acceptance-plans/PLAN-41-qa-ci-acceptance-honest-baseline.md` — **the plan; it is the specification.** Where this prompt deviates from it, this prompt wins, and the deviation goes into the final report. Its line numbers date from 2026-10-30 and may have moved; find the code by its content.
2. `reports/plan40-final-report.md`: §3 deviations, §9 defects, §11 the readiness note for PLAN-41 (the plain acceptance stack owns its volumes, the summarizer writes UTF-8 without a BOM, run destructive classes alone, the publisher contract).
3. `reports/plan35-final-report.md` §7 (protection: file vs live) and the dedupe notes in `reports/plan37-final-report.md`.
4. `docs/OWNER_DECISIONS_REGISTER.md`: D-QA-2, D-QA-4, D-QA-6, D-QA-19, D-QA-20 and D-QA-21 are accepted.
5. `.github/workflows/ci.yml`, `scripts/ci/dedupe-push-run.sh`, `scripts/ci/steps.json`, `scripts/ci-local.ps1`, `scripts/plan035-prepush.ps1`.
6. `scripts/acceptance-up.ps1`, `scripts/acceptance-local-env.ps1`, `scripts/prepare-keycloak-import.ps1`, `scripts/prepare-ci-compose-env.sh`, `scripts/acceptance-evidence-summarize.ps1`, `scripts/acceptance-file-scan.ps1`, `scripts/ac026-scan-pii.ps1`.
7. `AcceptanceGate.cs`, `EvidenceRecorder.cs`, `AcceptanceHarness.cs`, `AcceptanceEvidenceSummary.cs`, `AcceptanceMessagingHelper.cs`, and every file the plan cites.
8. `docs/scm-governance/branch-protection-main.json`, `scripts/scm/apply-branch-protection.ps1`, `scripts/scm001-branch-protection-validate.ps1`, `docs/qa/governance-exceptions.json`, `docs/qa/dated-obligations.json`.

## Preconditions (check in P0; `BLOCKED:` if any fails)

1. **PLAN-40 is merged with green checks — finish and merge it yourself if it is not** (standing owner instruction). At the time of writing:
   - PR #35 (`release/plan-40`) was open;
   - a CodeQL fix (`3d0b2df8`, antiforgery token on the replay POSTs) was committed locally but not pushed;
   - a paragraph on that token in `docs/OPS_VPS_SINGLE_HOST.md` was not committed.

   Steps:
   1. If the uncommitted `OPS_VPS_SINGLE_HOST.md` paragraph is PLAN-40's antiforgery note, commit it on `release/plan-40`: `docs(msg040): replay POSTs need the antiforgery token`.
   2. Push `release/plan-40` if the remote is behind. Then `gh pr checks 35`, waiting as described in «Waiting for CI».
   3. All green: `gh pr merge 35 --merge`.
   4. A red check: root-cause it (`gh run view <id> --log-failed`), fix it on `release/plan-40` with a small commit naming the step, rerun that step locally, push the release branch only, and wait again. If the fix needs more than a small change, record `BLOCKED:`.
   5. **Never merge with a red or pending check.**
   6. Record the PR link, check results, the merge commit and whether the post-merge dedupe guard skipped, in `reports/plan40-progress.md`, stamped `Merged by agent on owner instruction (UTC): <timestamp>`.
2. **`main` is in sync.** `git switch main; git pull --ff-only`; then `git rev-parse main` = `git rev-parse origin/main`.
3. **The working tree is clean** except for these, which may be present:
   - the owner's local `global.json` edit;
   - the untracked `tmp-vendors.html`;
   - git-ignored `artifacts/`;
   - this prompt file;
   - the uncommitted `reports/plan40-progress.md` and `reports/plan40-pr.md` notes, which ride with this plan's first commit.

   Restore any test-rewritten `testdata/*/out/*.json` with `git checkout -- testdata/*/out`.
4. **No other plan session** is running on this working tree. The PLAN-40 session may still be finishing its merge; wait for it, never run beside it.
5. **`gh auth status`** shows `omardefdev` with the `repo` and `workflow` scopes.

## Decisions — already taken (no owner questions)

Accepted by the owner on 2026-10-31 (wave-1 questionnaire), as the plan's table states:
- **D-QA-2:** required checks are `CI`, `CodeQL`, `Gitleaks`, `E2E`, and `Acceptance` once this plan has made it green.
- **D-QA-4 (partial):** required classes are every current AC-HCM, AC-WF and AC-EST class, AC-QA-34/35, and the merged wave-1 classes (PLAN-36..40). AC-01..37 run **not required**: their result shows and never fails the job, except those excluded explicitly in `ci-scope.json` with an owner. Tagging them required is PLAN-65.
- **D-QA-6:** prod-like profile for every acceptance class from PLAN-37 on, AC-HCM-25 and `JourneyProdLike`; lab profile for AC-HCM-21/22/26..33, AC-WF-23, AC-EST and AC-01..37. The profile is recorded in `metadata.json`.
- **D-QA-19:** the job runs on every PR to `main` and every push to it, with `concurrency` cancelling the older run on the same branch. The report measures the minutes; the owner may later reduce it to a label plus a nightly run.
- **D-QA-20:** `acceptance-evidence/` in the repository is replaced only from the artifact of a CI run (`source: ci`, `ciRunId`). A local run is a draft (`source: local`) and never counts as a pass in the committed summary.
- **D-QA-21:** evidence is `Stale` if its source differs from the code under test, if any service image was built from another source, or if it is older than 30 days (committed summary). P-2 defines «the code under test».

## Prompt-level decisions

| # | Decision |
|---|----------|
| P-1 | **One job script, two PowerShells.** `scripts/ci/acceptance-job.ps1` runs under PowerShell 5.1 on the owner's Windows machine **and** under `pwsh` 7 on the Linux runner. ASCII only; `Join-Path` for paths; no Windows-only cmdlets. `bash scripts/prepare-ci-compose-env.sh` runs only on Linux; on Windows the script uses the local environment path that `acceptance-up.ps1` already takes. Local commands use `powershell -NoProfile -ExecutionPolicy Bypass -File …`; replace every `pwsh` in the plan's Verification block. Build local images one at a time. |
| P-2 | **Freshness by code tree, not by commit.** Committing evidence or a report changes the commit, so «gitSha ≠ HEAD» would make every committed summary stale by construction.<br>• `codeTree` = the hash of `git ls-tree -r <commit>` over every path **except** `acceptance-evidence/`, `reports/` and `docs/` (one helper, used by the recorder, the summarizer and the validator).<br>• `metadata.json` records `sourceSha` (the commit the job checked out), `codeTree`, and `imageRevisions`.<br>• A folder is fresh when its `codeTree` equals the current one, **every** image revision equals its own `sourceSha` (with no `-dirty` suffix), and it is at most 30 days old.<br>• Record this in the plan's «ملاحظات التنفيذ» as a deviation from the literal «gitSha = HEAD». |
| P-3 | **Where the committed evidence comes from.** A pull-request run tests the merge of the release branch into `main`. When the push run after the merge is skipped by the dedupe guard, the tree that landed on `main` is exactly the tree that run tested. That PR run therefore counts as «a CI run on main» for D-QA-20. Its metadata records `ciEvent: pull_request`, `ciRunId` and `codeTree`. The evidence flow inside the single release PR:<br>1. **Run 1** on the code.<br>2. Download its `acceptance-evidence` artifact, replace `acceptance-evidence/` with it, and commit (evidence-only paths, so the code tree is unchanged).<br>3. **Run 2** on that commit.<br>Run 1 and run 2 are the two consecutive green runs the plan asks for. |
| P-4 | **Committed evidence is checked whenever it changes.** A CI step in the `CI` job runs `scripts/acceptance-baseline-validate.ps1 -Committed` **only** when the PR changes `acceptance-evidence/**`. It checks the committed summary:<br>• it was generated in CI;<br>• its run id belongs to this repository;<br>• it is fresh against the PR's code tree;<br>• every AC has an owner, and nothing is counted as proven against the rules.<br>The full validator (without `-Committed`) runs inside the `Acceptance` job on the run's own summary.<br>**Contract for later plans:**<br>• The evidence of record for a plan is its PR run's artifact, linked from the final report.<br>• A local acceptance run is a draft and is never committed.<br>• A plan that refreshes committed evidence copies it from a CI artifact.<br>Write this into `docs/CI_AND_TESTING.md` and the readiness note. |
| P-5 | **Wiring the job.**<br>• Remove the `ENABLE_ACCEPTANCE_JOB` gate and the `acceptance-disabled` job. Close `CI-ACCEPTANCE-DISABLED`, and update every governance test that names it.<br>• `needs: [dedupe]`, with the same skip condition as `CI`, so the job runs **in parallel** with `CI`: the owner values wall-clock time, and `concurrency` cancels superseded runs.<br>• A `timeout-minutes` with room (measure, then set about 1.5 × the first green run).<br>• `scripts/ci/free-runner-disk.sh` before the build.<br>• An artifact that never contains `.acceptance/credentials.json`, `.env` or any generated secret; scan it before upload.<br>• Keep `runs-on: ubuntu-24.04` (PLAN-35: Playwright).<br>• Add `workflow_dispatch` to `ci.yml` with two inputs, `simulate_missing` and `classes`, so the owner can rerun acceptance on `main`. When `simulate_missing` is set, only the lab profile runs, on the listed classes. |
| P-6 | **What runs in CI (`tests/acceptance/ci-scope.json`).**<br>• Every class of `HRManagement.AcceptanceTests` appears exactly once: in `lab[]`, in `prodLike[]` with `{ class, required, group }`, or in `excluded[]` with `{ class, reason, ownerPlan, reviewBy }`.<br>• **Exclude only for an environment CI cannot provide:** SSH to the demo server (`*Vps*` classes), deliberate chaos (AC-17/18/22, owner QA-G «ops-drill»), the backup drill (AC-34), the performance run (AC-35), the human signature (AC-36), and the non-reproducible AC-14 (owner COD-G). Each exclusion is mirrored in `docs/qa/dated-obligations.json`, read by a selector on `excluded[*]`.<br>• **Never exclude or un-require a class because it is red.**<br>• A required class that is red in CI but green locally is an environment difference: fix the environment.<br>• A required class that is red because of a product defect: fix it if the fix is small and inside this plan's area; otherwise **Stop**.<br>• The PLAN-38 least-privilege classes run where the job can start their compose file in the same project. If that needs a third stack variant, add it as a group of the prod-like step, not a new profile. |
| P-7 | **Order inside a profile.** Classes that stop, kill or recreate containers (for example `AcMsg40` 40d and `AcOps39` 39f) run **last and alone**, in their own `dotnet test` invocation (`group: "isolated"`). The job script runs the groups in order, writes one trx per group, and the guard reads them all. |
| P-8 | **Runner capacity.**<br>• The first remote run records peak memory (`docker stats --no-stream` after the stack is up), disk, and each step's duration.<br>• If the standard runner cannot hold the stack, stop the services no class in that group needs (a compose profile), and record which ones.<br>• If that is not enough, record `BLOCKED:` with the options: a larger runner (cost) or splitting the job.<br>• Use the BuildKit GitHub Actions cache only if it fits in one small compose override; otherwise record the build minutes and move on. |
| P-9 | **Branch protection.**<br>• After run 2 is green, the evidence commit also moves `Acceptance` from `pendingRequiredChecks` to `required_status_checks.contexts` in `branch-protection-main.json`, so run 2 validates the file (`scm001-branch-protection-validate.ps1`).<br>• After the merge, check `gh repo view --json visibility`:<br>&nbsp;&nbsp;– **Public, and the protection API answers anything but 403:** apply the file with `scripts/scm/apply-branch-protection.ps1`, verify with `scm001-branch-protection-validate.ps1 -CheckLiveGitHub`, close `SCM-NO-GITHUB-PROTECTION`, and record the undo command (`gh api -X DELETE repos/omardefdev/HRManagement/branches/main/protection`). This carries out the owner's existing decisions D-QA-1 and D-QA-2.<br>&nbsp;&nbsp;– **Private, or 403:** keep the documented fallback and its exception, and record it.<br>• **Never change the repository's visibility.** |
| P-10 | **The negative proofs.**<br>• AC-QA-41a's negative runs **locally** in P4: `ACCEPTANCE_SIMULATE_MISSING` with `-Profile Lab`, exit 1, and the message names the scenario.<br>• It runs **remotely after the merge**: `gh workflow run ci.yml --ref main -f simulate_missing=AcHcm26AssessmentInterviewTests.Ac26_i_intake_close_job_closes_expired_posting -f classes=AcHcm26AssessmentInterviewTests`. The expected result is red, with the scenario named in the job summary. Record the run link.<br>• AC-QA-41c's and AC-QA-41f's negatives use fixture copies and fixture trx files in `testdata/`, never a committed broken class or persona. |
| P-11 | **Merge, by you, only after green checks** (standing owner instruction).<br>1. Push the release branch only after local CI and the pre-push check are green.<br>2. `gh pr create`; wait for run 1 («Waiting for CI»).<br>3. Make the evidence commit (P-3, P-9); push the release branch; wait for run 2.<br>4. Only when every check of run 2 is green: `gh pr merge <n> --merge`.<br>5. A red check: fix it on the release branch, push that branch only, and wait again. A fix to code after run 1 means run 1's evidence is no longer fresh: take the evidence again from the next green run.<br>6. Record whether the post-merge dedupe guard skipped. If it did not skip, record why: the tree differs, so the evidence's code tree must still equal `main`'s, and the validator proves it.<br>Never push to `main` directly, never force-push. |
| P-12a | **Local runs never touch the committed evidence.** Every local acceptance run (P0, P3, P4) writes into `acceptance-evidence/`. Copy what you need to `artifacts/plan41/`, then `git checkout -- acceptance-evidence` and `git clean -fd acceptance-evidence`. The baseline summary of P0 is written to `artifacts/plan41/`, not over the committed one. Before every commit until P7 step 4: `git diff --cached --name-only` lists nothing under `acceptance-evidence/`. |
| P-12 | **No regression, no server change.** These stay green:<br>• `lint-compose.ps1` (both chains), `Ops036ComposeLintTests`;<br>• `ProductionWiringMatrixTests` and `int037-wiring-check.ps1`;<br>• `Ops038EnvParityTests`, `Ops038RabbitPermissionMatrixTests`;<br>• `Ops039RenderRealmTests`, `Ops039RealmManifestParityTests`;<br>• `OutboxPublishOrderingArchitectureTests` and the PLAN-40 publisher contract;<br>• `Scm001` governance tests and the dedupe tests.<br>**The demo server is not touched by this plan.** The `SOURCE_SHA` build argument must not break the server's build (`build-on-host.ps1`): its default stays `unknown`. Making `build-on-host.ps1` pass its own commit is allowed; it takes effect at the next server build by a later plan. Prove that the server's build path still renders with `docker compose … config`. |
| P-13 | **Minutes and cost.**<br>• Record the billable minutes of each run (`gh api repos/omardefdev/HRManagement/actions/runs/<id>/timing`).<br>• Project a month at the recent PR rate (count the PRs of October and November).<br>• If the repository is private and the projection exceeds the free quota, write that as a note for the owner, with the D-QA-19 alternative (a label plus a nightly run). **Do not change D-QA-19 yourself.** |

## Scope boundaries

**Out of scope** (from the plan):
- **PLAN-65:** tagging AC-01..37 `[RequiredAcceptance]`, and strengthening their weak tests.
- **PLAN-59:** fixing AC-12.
- **PLAN-45:** rewriting AC-33.
- **PLAN-42:** fixing AC-11.
- **QA-G (wave 2):** chaos, backup, performance and PDF-signature drills (AC-17/18/22/34/35/36).
- **PLAN-37:** making the prod-like profile mirror production wiring. It is only consumed here.
- **QA-H (wave 2):** real E2E journeys.
- **The demo server** (P-12).

**Never commit:**
- the owner's local `global.json` edit — check `git diff --cached --name-only` before every commit;
- `tmp-vendors.html`, `scripts/__pycache__/*`, `docs/thesis_diagrams/source/.$*`, or anything under `artifacts/`;
- test-rewritten `testdata/*/out/*.json`;
- `.acceptance/credentials.json`, `.env`, or any secret, token or password;
- evidence from a local run (D-QA-20).

**Never:**
- push to `main` or force-push;
- merge with a pending or red check;
- change the repository's visibility;
- disable host-key checking;
- weaken a guard, lower a coverage floor, delete a CI step, exclude a class, or mark a class not required to get green;
- put a password or secret in `ci.yml`.

## Stop (record `BLOCKED:` and end the turn) if

- **A precondition fails** — including a PLAN-40 check that stays red after a small fix.
- **A required class is red because of a product defect** whose fix is not small or belongs to another plan (P-6).
- **The runner cannot hold the stack** even with unneeded services stopped (P-8).
- **The job cannot generate the realm passwords on the runner** without putting a secret in `ci.yml` or the repository.
- **Two consecutive green runs cannot be obtained** because of flakiness you cannot root-cause in the turn: record the failing scenarios, their rate, and the options.

---

## P0 — Timing, preconditions, baseline

1. Write the `Started (UTC)` line (see «Timing»). Check the Preconditions; finishing PLAN-40's merge comes first.
2. `git switch -c feature/plan-41-qa-ci-acceptance-honest-baseline` from the updated `main`.
   - Commit this prompt file.
   - Commit the PLAN-40 notes: `docs(msg040): PR merge note and post-merge run`.
3. **The repository and GitHub state, read-only**, into `reports/plan41-baseline.md`:
   - `gh repo view --json visibility`;
   - `gh api repos/omardefdev/HRManagement/branches/main/protection` (status only);
   - `gh variable list`;
   - the last five `ci.yml` runs on `main` and their billable minutes.
4. **The plan's baseline** («المرحلة 0»):
   - **0.1:** the contract with PLAN-37 is met: `docker-compose.acceptance-prodlike.yml` exists and `acceptance-up.ps1 -ProdLike` starts it.
   - **0.3:** the class inventory, one row per class of `HRManagement.AcceptanceTests`: `[RequiredAcceptance]`, gate pattern, evidence folder, last result, the profile D-QA-6 gives it, what it needs (SSH, chaos, least-privilege stack, GitHub token), and whether it stops or recreates containers. Compare with `reports/plan33-acceptance-inventory.md`. This becomes `ci-scope.json` in P4.
   - **0.4:** the current results on this code, before any change: a lab stack and a prod-like stack started with `acceptance-up.ps1`, then the classes by group, one `dotnet test` per group. Skip the classes that need the demo server or deliberate chaos. Record pass, fail and blocked per class, and the duration. Then restore the committed evidence (P-12a).
   - **0.5:** the current summary (`acceptance-evidence-summarize.ps1`, output under `artifacts/plan41/`) as the «before» picture, with its real counts (the plan's 27/2/3/23 may have moved).

Commit: `chore(qa041): baseline, class inventory and needs, current acceptance results, GitHub state`.

## P1 — The red static tests and the persona passwords (plan §1, «إصلاح الاختبارات الثابتة الحمراء»)

- `AcceptanceV2CatalogTests` and `AcceptanceIdentityPopulationTests` derive their expected counts from `AcceptancePersonaCatalog` (enabled, minus the expected refusals) instead of constants, with corrected names.
- `prepare-keycloak-import.ps1` checks `schemaVersion` and unique ids only; the fixed count check goes. Later plans add personas to `personas.json` and to the catalog together.
- `acceptance-file-scan.ps1` reads the persona's password from `.acceptance/credentials.json`, as `AcceptanceHarness` does.
- `ACCEPTANCE_PERSONA_PASSWORD` disappears from every script, workflow and test (`git grep` outside `docs/` = 0).
- **Tests:** the static filters are green. A fixture copy of `personas.json` with one extra persona makes the parity test red (AC-QA-41c).

Commit: `fix(qa041): persona counts come from the catalog, the file scan reads the generated persona password, no shared persona password left`.

## P2 — An honest evidence model (plan §2, P-2)

- **`EvidenceRecorder.WriteMetadata`** adds:
  - `profile` (from `ACCEPTANCE_PROFILE`, or detected);
  - `imageRevisions` (`docker inspect` of the `hrm-acceptance` containers, label `org.opencontainers.image.revision`);
  - `sourceSha` and `codeTree` (P-2);
  - `source` (`ci` when `GITHUB_RUN_ID` is set, else `local`), `ciRunId`, `ciEvent`.
- **The scenario key** defaults to the test method name, so each folder keeps a `scenarios` map and its worst state. A positive result never erases a negative one (AC-05, AC-QA-41d).
- **`AcceptanceGate`:** `ACCEPTANCE_SIMULATE_MISSING=<Class.Method>` makes the first `RequireData` of that scenario false. It is test code only, and it is documented as such.
- **One summarizer:** `scripts/acceptance-evidence-summarize.ps1` is the only one. Delete `AcceptanceEvidenceSummary.Rebuild`; `AcceptanceEvidenceSummaryTests` call the script through `RepoScriptRunner` on fixture folders.
- **The status rules** in the plan's order (`NotRun`, `Incomplete`, `Stale`, `Fail`/`Blocked`, `Pass`), the summary columns, and separate totals where «passed but does not prove» is not «proven».
- **`tests/acceptance/ac-ownership.json`:** one entry for **every** AC with evidence or a test: AC-01..37, AC-EST, AC-HCM, AC-WF, AC-QA-34/35/41, AC-OPS-36/38/39, AC-INT-37, AC-MSG-40. Use the plan's initial table for AC-01..37; for the rest, the owning plan and `provesClaim` from what each test checks.
- **`scripts/acceptance-baseline-validate.ps1`** with the plan's rules, plus the `-Committed` mode (P-4).
- **Tests:** fixture folders for each state (fresh, stale code tree, image built from another source, older than 30 days, `legacyContractOnly`, incomplete contract, positive plus failed negative), and the validator's failures (AC without an owner, a local summary in `-Committed` mode, a Pass with an incomplete contract) — AC-QA-41b, AC-QA-41d, the negative of AC-QA-41g.

Commit: `feat(qa041): evidence records profile, image sources, code tree and run source, keeps every scenario; one summarizer with honest states; AC ownership and a baseline validator`.

## P3 — Image provenance (plan §1 «مصدر الصورة»)

- `docker/DotnetService.Dockerfile`: `ARG SOURCE_SHA=unknown` and `LABEL org.opencontainers.image.revision=$SOURCE_SHA`.
- `docker-compose.services.yml`: `build.args.SOURCE_SHA: ${SOURCE_SHA:-unknown}` for every service.
- `acceptance-up.ps1`: passes `SOURCE_SHA` (with `-dirty` when a tracked file is changed) and accepts `-Profile Lab|ProdLike` as a thin wrapper over `-ProdLike` (plan step 0.1). There is no second definition of the profiles.
- Prove P-12: the server's build path still renders (`docker compose … config`), and `build-manifest` is regenerated where lock files changed.
- **Tests:** `metadata.json` of a fresh local run has every service's revision equal to `sourceSha`. An image built with an older `SOURCE_SHA` makes the folder `Stale` (AC-QA-41e, positive and negative).

Commit: `feat(qa041): service images carry their source revision, the acceptance stack builds them from the current commit`.

## P4 — The job script, the scope and the guard (plan §1, P-1, P-6, P-7)

1. **`tests/acceptance/ci-scope.json`** from the P0 inventory, with groups. Mirror the exclusions in `docs/qa/dated-obligations.json`.
2. **`scripts/ci/acceptance-job.ps1`:**
   - parameters `-Profile Lab|ProdLike|Both`, `-Scope`, `-SourceSha`, `-Classes`;
   - the plan's steps 1–7, groups in order, one trx per group;
   - the generated gateway caller-proof secret, never written to the evidence;
   - the guard:
     - a required class with any result other than `Passed`, or a `Fail` or `Blocked` folder → exit 1;
     - non-required results go to the job summary only;
     - a class in a trx that `ci-scope.json` does not list → exit 1;
   - a job summary (Markdown) with every class, its result and its duration;
   - the full validator on the run's own summary (P-4).
3. **`AcQa41AcceptanceBaselineTests`** (`[RequiredAcceptance]`, prod-like, `EvidenceRecorder("AC-QA-41", …)`): reads the metadata of the current run's folders (profile, image revisions, source), runs the validator, and records AC-QA-41a/e/g.
4. **Local proof**, recorded with durations:
   - `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/ci/acceptance-job.ps1 -Profile Both` on a clean stack: every required class Pass, exit 0;
   - the AC-QA-41a negative (P-10): exit 1, the scenario named;
   - the AC-QA-41f negative: fixture trx with an unlisted class → exit 1 (a script test through `RepoScriptRunner`).

Commit: `feat(qa041): one acceptance job script for CI and the owner, CI scope with owned exclusions, a guard that fails on blocked or unlisted classes`.

## P5 — `ci.yml` (P-5, P-4)

- The new `Acceptance` job: checkout, setup-dotnet, the free-disk step, `pwsh scripts/ci/acceptance-job.ps1 -Profile Both -SourceSha ${{ github.sha }}`, the artifact (evidence, trx files, `artifacts/acceptance/summary.md`) with its pre-upload secret scan, and teardown.
- Remove the old steps (port 5000, 5433, the persona password, the plain compose).
- `workflow_dispatch` with `simulate_missing` and `classes`.
- The `-Committed` validator step in the `CI` job, conditioned on changes under `acceptance-evidence/**`.
- `CI-ACCEPTANCE-DISABLED` closed; the governance tests and `steps.json` updated where they name the job.
- **Local proof:** `actionlint`, the governance and dedupe tests, `scm001-branch-protection-validate.ps1`.

Commit: `ci(qa041): the Acceptance job runs both profiles on every PR and push to main, the committed evidence is validated when it changes, the disabled-job notice is gone`.

## P6 — Docs, review, local CI

1. **Docs:**
   - `docs/CI_AND_TESTING.md`: the job, the scope file, how the owner runs it locally, the P-4 contract;
   - `docs/acceptance-plans/README.md`: environment and passwords;
   - `docs/GITHUB_CI_GOVERNANCE_RUNBOOK.md`: the required check and the manual run;
   - `docs/acceptance-plans/execution/VERIFICATION-COMPLETION.md` (no `acceptance-live`);
   - the «الحالة» lines for G-QA-06 and G-QA-08;
   - the plan's `Status` and «ملاحظات التنفيذ والانحرافات» (P-2, P-3, P-5).
2. **Independent review** (one fresh subagent). It reads this prompt, the plan and the diff, and hunts for:
   - a path where a blocked or skipped required scenario still passes the job;
   - a class that can enter the trx without being listed;
   - a «Pass» that is stale, local, contractual or incomplete but counts;
   - a secret that can reach the artifact, the log or the repository;
   - freshness that can be faked (a `-dirty` image, a code tree computed differently in two places);
   - a server build changed by `SOURCE_SHA`;
   - a governance test or guard weakened.

   Use a refuter only for high findings. Fix what survives, with tests.
3. **Local CI, then the pre-push check:** `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/ci-local.ps1` on HEAD, then `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/plan035-prepush.ps1`. Both must be green.

Commit: `docs(qa041): acceptance job, scope and evidence contract documented, gap status lines, review fixes`.

## P7 — Remote runs, evidence of record, merge (P-3, P-9, P-11)

1. Merge the feature branch into a new `release/plan-41` from `main` (`--no-ff`). Write `reports/plan41-pr.md` (short).
2. `git push -u origin release/plan-41`, then `gh pr create --base main --head release/plan-41 --title "PLAN-41: acceptance job in CI and an honest acceptance baseline" --body-file reports/plan41-pr.md`.
3. **Run 1.** Wait («Waiting for CI»). Record the P-8 measurements and the minutes.
   - A red check: fix it on the release branch, push that branch only, and wait again (P-11 step 5).
4. **The evidence commit**, after run 1 is fully green:
   1. `gh run download <run-1-id> --name acceptance-evidence --dir artifacts/plan41/run1/`.
   2. Replace `acceptance-evidence/` with the artifact's copy. Check that no secret came with it.
   3. Move `Acceptance` into the required contexts of `branch-protection-main.json` (P-9).
   4. Run the `-Committed` validator locally against the release head: PASS.
   5. Commit on `release/plan-41`: `test(qa041): acceptance evidence of record from CI run <id> (both profiles), Acceptance becomes a required check`. Push the release branch only.
5. **Run 2.** Wait. Every check must be green, including the `-Committed` validator.
6. **Merge:** `gh pr merge <n> --merge`. Then `git switch main; git pull --ff-only`, and verify that `main` = `origin/main`. Record whether the push run's dedupe guard skipped (P-11 step 6).
7. **After the merge:**
   - the AC-QA-41a remote negative (P-10): the dispatched run is red with the scenario named;
   - branch protection (P-9): apply and verify, or record the fallback;
   - the `-Committed` validator on `main`: PASS;
   - if the repository variable `ENABLE_ACCEPTANCE_JOB` exists, delete it (`gh variable delete ENABLE_ACCEPTANCE_JOB`), and record its undo.
8. Record the PR link, each check result of both runs, the merge commit, the post-merge run, the negative run and the protection result in `reports/plan41-progress.md`, stamped `Merged by agent on owner instruction (UTC): <timestamp>`. That note rides with the next plan's first commit; **do not open a second PR for it.**

## P8 — Final report

**`reports/plan41-final-report.md`** (English), with these sections:
1. Summary per phase.
2. Commits.
3. Decisions applied and deviations (P-2, P-3, P-5 at least).
4. The class inventory and the CI scope: required, not required, excluded (with owners).
5. The honest baseline: the summary's totals before (P0) and after (run 1), and every AC whose state changed, with the reason.
6. Unit, script and acceptance results, local and remote.
7. **CI runs:**
   - links;
   - queue and wall-clock times;
   - each `Acceptance` step's duration;
   - peak memory and disk;
   - billable minutes;
   - the monthly projection (P-13).
8. GitHub changes (protection, variables) and their undo.
9. Deferred items.
10. Defects for later plans.
11. **Timing:**
    - total wall-clock;
    - active time against time spent waiting for CI, and how many `WAITING-CI:` stops there were;
    - the output of `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/plan-timing.ps1 -From 25 -To 41`.
12. **A readiness note for PLAN-42:**
    - every new acceptance class goes into `ci-scope.json`;
    - the P-4 evidence contract;
    - the PLAN-40 publisher contract still applies;
    - the job's typical duration.

## Definition of done

- **The job:** `Acceptance` runs both profiles on every PR and push to `main`, and has been green twice in a row on the tree that is now `main`. It is in the protection file's required contexts, and in the live protection when GitHub allows it. `CI-ACCEPTANCE-DISABLED` is closed.
- **The scope:** `ci-scope.json` lists every class; every exclusion has a reason, an owner and a review date, mirrored in the dated obligations.
- **The baseline:** the committed `summary.md` comes from the run 1 artifact:
  - every AC has a state and an owner;
  - AC-34/36 are `NotRun`;
  - no Pass has an incomplete contract;
  - `acceptance-baseline-validate.ps1` (both modes) = PASS.
- **The evidence model:** one summarizer; `EvidenceRecorder` records profile, image sources, code tree and run source, and never erases another scenario.
- **The tests:** AC-QA-41a..g = Pass, positive and negative, with `acceptance-evidence/AC-QA-41/`.
- **No regression:** the P-12 guards are green, and the demo server is untouched.
- **The merge:** done by you, through a PR, after green checks, with the timing and minutes sections.

Then reply with a short summary in Arabic:
- what changed;
- the honest baseline in numbers (proven, passed but not proving, stale, not run, blocked);
- how long one `Acceptance` run takes, and its minutes;
- whether `Acceptance` is now a live required check;
- the PR link;
- the path of the final report.
