# Task: PLAN-66 — A faster CI: parallel acceptance stages, no rerun for an unchanged code tree, governance checks on demand

The goal: a merge waits about **50 minutes** for its checks instead of about **two hours**, a commit that does not change the code tree waits **under 20 minutes**, and a failure is retried in **minutes**. Coverage stays the same: every acceptance class still runs, with one deliberate exception (S3), which the owner approved.

The owner asked for this on 2026-11-06, after asking whether the tests are excessive. The answer they accepted:
- The tests are not excessive. The pipeline around them is slow.
- About half of the `Acceptance` job is four stacks started one after another on one runner.
- About 18 minutes go to governance checks of the CI itself.
- Every failure, and every evidence commit, pays the whole run again. PLAN-41 needed 8 runs (about 10 h of CI); PLAN-42 needed 3 (about 5 h).

It runs in one session with no owner action: the owner asked the agent to merge, so you merge after green checks.

## How to run this task — read before anything else

This is a **single prompt for one session**. Run it as phases **P0–P7** in order. **There are no owner actions in this plan**: every decision is taken (see «Decisions»).

**Standing owner instruction on merges** (PLAN-38 to PLAN-43): **you** push the release branch, open the PR, wait for the checks, and merge — but **only when every check is green** (P-9).

If something truly needs the owner (a Stop condition), write `BLOCKED:` and end the turn.

**At the end of every phase:**
1. Build: `dotnet build HRManagement.slnx -warnaserror`, 0 errors.
2. Run the phase's tests.
3. Commit, using the phase's commit message.
4. Update **`reports/plan66-progress.md`** — your checkpoint. It contains:
   - phase status;
   - decisions (with reasons);
   - files changed;
   - test results;
   - every CI run you start: its id, URL, what it measured, and its per-job durations;
   - lock acquisitions and releases;
   - open issues;
   - the exact next step.

**If you are resuming:** at start, or after any context compaction or restart, read `reports/plan66-progress.md` and `git log --oneline -20` **first**. Continue from the recorded next step. Never redo a committed phase.

**Timing (mandatory, the owner compares plans):**
- The **very first action** of the session is to create `reports/plan66-progress.md`. Its first line is:
  `Started (UTC): <timestamp>`
  where the timestamp comes from `(Get-Date).ToUniversalTime().ToString('o')`.
- Every phase records `Finished (UTC):`.
- Every resume records `Resumed (UTC):` with the reason.
- Record the duration of every local run and every GitHub Actions run, per job.
- Record the time spent waiting for CI (`WAITING-CI:`) and for locks.
- Never edit an earlier stamp.

**Economy mode** (the owner runs this session at `xhigh`):
- Use **at most two subagents at a time**. Good uses: the P0 inventories, read-only.
- Run **one independent review** at the end. Use a refuter only for findings rated high.
- Write long outputs (logs, run JSON, trx files) under `artifacts/plan66/`. Read only the failing parts.

**Waiting for CI** (PLAN-41 rule):
- Wait with whatever mechanism this session allows: a background `gh run watch <id> --exit-status` if permitted, or the app's run monitor.
- If the session cannot wait, end the turn with a line that starts with **`WAITING-CI:`** and names the PR, the run id, its URL and the exact next step.
- On the owner's next message (even «اكمل»), check **once** (`gh pr checks <n>` or `gh run view <id>`) and continue from that step.
- Never merge on a guess.

**Blocking decisions.** When a "Stop" condition is hit:
1. Write `BLOCKED:` with the question and options.
2. Commit what is complete.
3. End the turn. **Do not guess.**

---

## Role

You are a senior CI engineer who also writes .NET:
- GitHub Actions: job graphs, matrices, artifacts, concurrency, re-runs, required checks, the REST API through `gh`;
- Docker and Compose on runners: BuildKit, `docker save`/`docker load`, disk limits;
- PowerShell 5.1 and 7 (the job script runs under both), bash;
- xUnit tests that pin a pipeline's structure.

You work on the `HRManagement` repository on the owner's Windows machine (PowerShell **5.1 only, no `pwsh`** unless the owner installs it, Docker Desktop, culture ar-SA, `gh` signed in as `omardefdev`). This plan does **not** touch the demo server or product code.

## Where things stand (2026-11-06)

- **Merged:** PLAN-41 (PR 36) and PLAN-42 (PR 37, `7480843e` on `main`).
- **PLAN-43** is in its PR (#38, `release/plan-43`) and waits for its checks in the **primary clone** `D:\my projects\HRManagement`. That clone belongs to its session: never touch it, its branches or its PR.
- **You normally work in the second clone** `D:\my projects\HRManagement-b` (P-1, precondition 1). The protocol is `docs/PARALLEL_PLANS.md`, with the locks in `scripts/parallel/hrm-lock.ps1`.
- **The repository is public.** Standard GitHub-hosted runners cost nothing there, so extra parallel jobs cost no money. Verify this in P0 (`gh repo view --json visibility`).

**The pipeline today** (`.github/workflows/ci.yml`):
- Jobs: `Dedupe post-merge run`, `CI` (build-and-test), `E2E` (needs CI), `Acceptance`, `CodeQL`, `Gitleaks`.
- `Acceptance` is one job on `ubuntu-24.04`, timeout 120 min. It runs `scripts/ci/acceptance-job.ps1 -Profile Both`, which walks `order[]` in `tests/acceptance/ci-scope.json` one stage after another:
  1. `lab`: `main`, `journey`, `isolated`;
  2. `prodLike`: `journey`, `main`, `isolated`, `final`, then `s2s42` (an overlay that recreates two services);
  3. `leastpriv`;
  4. `meta` (`ciOnly`, on the `base` stack; it reads the results of the CI job of the same commit).
- It uploads one artifact, `acceptance-evidence`, from `artifacts/acceptance/run/`: the evidence of record.

**Measured** — use these as your "before", and add your own measurements in P0:

| Item | Value |
|------|-------|
| Green run, whole workflow | 1h38m (run 41962709297), 1h37m48s (PLAN-42's green run) |
| `CI` / `E2E` / `CodeQL` | about 33 / 15 / 16 minutes |
| `Acceptance` | about 1h40m |
| PLAN-41 run 2, stack starts | lab 1189 s, prod-like 498 s, least-privilege 702 s, base 694 s: about 51 minutes in all |
| PLAN-41 run 2, tests | lab-main 845 s, meta 1058 s |
| Run after an evidence-only commit | 1h30m (PLAN-41's last run) |
| PLAN-43's local gates | `ci-local.ps1` 1346 s, pre-push 782 s |

## Read first (mandatory)

1. `reports/plan41-final-report.md`: the job, the scope file, the evidence contract, the code tree, the dedupe guard, the run history.
2. The PLAN-42 and PLAN-43 final reports: how the job grew (`composeFiles`, `recreate`, the `s2s42` stage), and their run histories. PLAN-43's report is on `origin/release/plan-43` until that PR merges.
3. `.github/workflows/ci.yml`, `scripts/ci/*` (above all `acceptance-job.ps1`, `acceptance-guard.ps1`, `acceptance-artifact-scan.ps1`, `dedupe-push-run.sh`, `free-runner-disk.sh`, `compose-up-retry.sh`), `scripts/acceptance-evidence-lib.ps1` (`Get-AcceptanceCodeTree`), `scripts/acceptance-baseline-validate.ps1`.
4. `tests/acceptance/ci-scope.json`, `tests/acceptance/ac-ownership.json`, `AcceptanceCiScopeTests`, `AcQa34PushableBaselineTests`, `AcQa35RemoteCiTests`, `AcQa41AcceptanceBaselineTests`.
5. `docs/CI_AND_TESTING.md`, `docs/GITHUB_CI_GOVERNANCE_RUNBOOK.md`, `docs/PARALLEL_PLANS.md`, `docs/OWNER_DECISIONS_REGISTER.md`.
6. The branch protection file and its fixture (`testdata/qa/branch-protection/`).

## Preconditions (check in P0; `BLOCKED:` if any fails)

1. **No other session works in this clone.**
   - While PLAN-43's PR #38 is open, its session owns the primary clone `D:\my projects\HRManagement`, so you must be in the second clone `D:\my projects\HRManagement-b`.
   - Once PLAN-43 is merged and no other plan has started there, the primary clone is fine too.
   - If you are in the primary clone while PR #38 is open: `BLOCKED:` with the commands for the owner:
     ```
     git clone "D:\my projects\HRManagement" "D:\my projects\HRManagement-b"
     Copy-Item "D:\my projects\HRManagement\docs\acceptance-plans\execution\PLAN-66-AGENT-PROMPT.md" "D:\my projects\HRManagement-b\docs\acceptance-plans\execution\"
     ```

   A second clone is set up as `docs/PARALLEL_PLANS.md` says; fix whatever is missing:
   - `git remote set-url origin https://github.com/omardefdev/HRManagement.git`;
   - the hooks: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/scm/install-hooks.ps1`;
   - the owner's local `global.json` edit, copied from the primary clone and **never committed**;
   - any git-ignored local file a script needs: copy it from the primary clone without printing it, and never commit it.
2. **`git fetch origin`**, and `origin/main` contains PLAN-42's merge (`7480843e` or later).
3. **The working tree is clean**, except for these, which may be present:
   - the `global.json` edit;
   - git-ignored `artifacts/`;
   - this prompt file.
4. **`gh auth status`** shows `omardefdev` with the `repo` and `workflow` scopes.
5. **The repository is public** (see «Where things stand»).

## Decisions — already taken (no owner questions)

Accepted by the owner on 2026-11-06, in chat. Record them in `docs/OWNER_DECISIONS_REGISTER.md` with that date and source.

- **D-QA-66-1 — Governance checks of the CI run on demand.** The `meta` stage (`AcQa34PushableBaselineTests`, `AcQa35RemoteCiTests`) runs on a pull request only when the PR changes a file those checks read (S3). It also runs on `main` every night and on every manual run. Otherwise the evidence records those classes as «not run by rule», with a link to the last nightly run.
- **D-QA-66-2 — No second run for an unchanged code tree.** When a commit leaves the code tree unchanged (`Get-AcceptanceCodeTree`, the PLAN-41 definition), the heavy jobs reuse a green full run of the same pull request for that same tree instead of running again (S2). Typical cases: an evidence commit, a report, a progress note.
- **Coverage does not shrink.** Every other class runs on every pull request, exactly once, under the same guard. No class is excluded, made optional or quarantined to gain time.

## Specification

**S1 — The `Acceptance` job runs its stages in parallel.**
- **A build job** builds every service image once — the same images the lab stack builds today, labelled with the run's `SOURCE_SHA`. It pulls every third-party image the stacks use, once, with retries.
- **The images travel to the stage jobs.** Default: one compressed `docker save` artifact, `retention-days: 1`. GHCR is the alternative only if P0 proves that its packages cost nothing here and can be public safely.
- **Stage jobs** each load the images, check that every service image carries this run's `SOURCE_SHA`, and run `acceptance-job.ps1` with a new `-Stage` selector and `-NoBuild`. They pull from no registry. Each uploads its results.
  - Group the `order[]` entries into stage jobs so that the **longest** job is as short as possible. Measure; do not guess.
  - Stages that recreate or overlay containers (`s2s42`, `leastpriv`, the isolated groups) keep the isolation they have today.
  - Stage jobs that run Playwright stay on `ubuntu-24.04`.
- **The aggregate job keeps the name `Acceptance`,** so the required check and branch protection do not change.
  - It needs every stage job. It runs `if: always()` and fails if any of them did not succeed.
  - It assembles the evidence from the stage results into **the same layout** a sequential run writes (`artifacts/acceptance/run/`, the same summary and metadata, the same code tree).
  - It runs the guard and the artifact scan, and uploads `acceptance-evidence` exactly as today.
  - `AcQa41AcceptanceBaselineTests` (`final`) runs where it can see what it checks: in the aggregate if it needs the whole run, otherwise in its stage. Read the class and decide.
- **One script for CI and the owner** (PLAN-41 principle). `acceptance-job.ps1 -Profile Both` without `-Stage` still runs everything in order on one machine. `-Stage` takes one or more `part:group` entries of `order[]`.
- **What keeps working:**
  - `workflow_dispatch` inputs `classes` (a narrowed run) and `simulate_missing` (the guard's negative proof);
  - the dedupe guard;
  - cancel-in-progress for a new push;
  - free runner disk where a job needs it.

**S2 — Reuse for an unchanged code tree** (D-QA-66-2).
- **Every run records its identity:** a small `run-identity` artifact with
  - the code tree;
  - the head SHA;
  - the scope (`full`, or `narrowed` for a dispatch with `classes` or `simulate_missing`);
  - and, for a reusing run, `reusedFrom`, pointing at the original full run.
- **A `reuse-check` job** runs after `Dedupe`. It looks for the newest successful run of this workflow for the **same pull request**, whose **head repository is this repository**, whose scope is `full` and whose code tree **equals** this commit's.
- **When it finds one:**
  - `CI`, `E2E`, the build job, the stage jobs and `Acceptance` succeed without doing the work;
  - each writes the reused run's id and URL to the job summary;
  - `Acceptance` copies the reused run's `acceptance-evidence` artifact into this run, with a `reuse.json` pointer, so «the PR run's artifact» exists for the evidence of record.
- **`CodeQL` and `Gitleaks` always run.**
- **Never reuse for:**
  - a fork;
  - a push to `main`;
  - a manual or scheduled run;
  - a narrowed run;
  - a failed, cancelled or partly skipped run;
  - a different code tree.

  When in doubt, run.
- **The decision is a script** (`scripts/ci/acceptance-reuse.ps1` or similar) with unit tests for every case above, the negative ones included.

**S3 — Governance checks on demand** (D-QA-66-1).
- `ci-scope.json` gains a rule for the `meta` stage: it runs when the PR changes a listed trigger path, on a schedule, or on a manual run.
- **The trigger paths are what the meta classes read.** Inventory them in P0 by reading the classes. At least:
  - `.github/**`, `scripts/ci/**`, `scripts/ci-local.ps1`, `scripts/plan035-prepush.ps1`;
  - `tests/acceptance/**`, `testdata/qa/**`, `docs/qa/**`, `.gitleaks.toml`;
  - the meta classes' own files.
- **The guard** treats the meta classes as required only when the rule fires. Otherwise the evidence marks them «not run by rule» with the reason and the last nightly run.
- **The baseline validator** and `AcceptanceCiScopeTests` accept «not run by rule» only for the classes the rule names.
- **A nightly schedule on `main`** runs the whole workflow with the rule forced on. A red nightly shows in Actions; if the repository already turns failures into issues (as `advisory-to-issue.yml` does), use the same path.

**S4 — The rest of the critical path.**
- `E2E` waits for `CI` today (33 + 15 minutes). If `E2E` needs nothing from `CI` but its success, run it in parallel.
- If `CI` is then the longest path, split it into parallel jobs (for example unit and integration). Keep the same required check names: an aggregate named `CI` if needed.
- Measure before and after. Change only what moves the critical path.

**S5 — Local policy and the plan document.**
- `docs/CI_AND_TESTING.md` and `docs/PARALLEL_PLANS.md` say:
  - before a push, run locally what the gates require today (`ci-local.ps1`, the pre-push check) and the acceptance classes the plan touches (`-Classes`);
  - the full acceptance suite runs once, in CI;
  - local acceptance runs, and so the hold on the `acceptance-stack` lock, get shorter.
- Write `docs/acceptance-plans/PLAN-66-qa-ci-fast-pipeline.md` in the format of the other plan documents (Arabic, like them):
  - the problem and the measurements;
  - S1–S5;
  - the ACs (below);
  - «الحالة»;
  - «ملاحظات التنفيذ والانحرافات».

**Acceptance criteria:**

| AC | What it proves | How |
|----|----------------|-----|
| AC-QA-66a | Every class in `ci-scope.json` runs in exactly one stage job on a normal PR, and the union is the sequential job's set (meta by rule) | Unit tests over `ci.yml` and `ci-scope.json`, and the stage results of this PR's run |
| AC-QA-66b | `Acceptance` fails when a stage job fails, is cancelled or skipped, or a required class is missing | Unit tests, and a `simulate_missing` dispatch after the merge that ends red |
| AC-QA-66c | Reuse happens only for the same PR, repository and code tree and a green full run; never otherwise | Unit tests for every case, the evidence-commit run of this PR (reused), and a code commit (not reused) |
| AC-QA-66d | Meta runs when a trigger path changes, nightly and manually; otherwise «not run by rule», accepted only for those classes | Unit tests, this PR (it changes `.github/`, so meta runs), one manual run of the nightly |
| AC-QA-66e | Full run ≤ 60 min (expected 45–55); evidence-only run ≤ 20 min; one stage rerun ≤ 25 min | Measured on this PR's runs (`gh run view --json jobs`), recorded in the final report |
| AC-QA-66f | `acceptance-job.ps1` without `-Stage` still runs everything in order; with `-Stage` it runs only the named stages | Unit tests, and one local `-Stage` run on the lab stack |

Put the unit tests in `src/Tests/HRManagement.UnitTests/Qa066*Tests.cs`. Add `AC-QA-66` to `ac-ownership.json` only if PLAN-41's validator needs an entry for an AC with no acceptance class. Read the validator and decide.

## Prompt-level decisions

| # | Decision |
|---|----------|
| P-1 | **Where.**<br>• A clone no other session uses (precondition 1), on branch `feature/plan-66-qa-ci-fast-pipeline` from `origin/main`.<br>• While PR #38 is open, the primary clone is PLAN-43's.<br>• If PLAN-43 merges while you work: `git fetch origin`, merge `origin/main` into your branch, and keep both sides in the shared list files, as the protocol says. |
| P-2 | **The contract does not change.**<br>• The required check names: `Acceptance`, `CI`, `E2E`, `CodeQL`, `Gitleaks`, and whatever else branch protection lists.<br>• The evidence artifact's name and layout, the guard's meaning, and the evidence-of-record flow.<br>• PowerShell 5.1 compatibility: new `.ps1` files ASCII-only and run with `powershell -NoProfile -ExecutionPolicy Bypass -File …`.<br>• If a required check name or branch protection would have to change: **Stop**. |
| P-3 | **No cost.**<br>• Standard GitHub-hosted runners only.<br>• Image artifacts kept 1 day.<br>• Record the runner minutes per run before and after: they rise with parallel jobs and stay free on a public repository. |
| P-4 | **Security of the images and of reuse.**<br>• The image artifact holds only images built from this repository and public third-party images. Check the Dockerfiles' `COPY` lines and `.dockerignore`: no `.env`, credential or generated secret enters an image. Run the artifact scan on everything else that is uploaded.<br>• Reuse reads runs through the default `GITHUB_TOKEN` with `actions: read` and trusts only runs of this repository.<br>• Give each job the least `permissions` it needs. |
| P-5 | **Reliability.**<br>• Third-party images are pulled once, in the build job, with retries. Stage jobs pull from no registry.<br>• Keep `compose-up-retry.sh`.<br>• Size each job's `timeout-minutes` at about 1.5 times its measured duration. |
| P-6 | **Local Docker and the shared machine.**<br>• Take `acceptance-stack` with `hrm-lock.ps1` for any local stack run, and release it after `down -v`.<br>• A Docker-heavy run (`ci-local.ps1`, Testcontainers) starts only when `docker ps` shows nothing of another session.<br>• **Never** prune Docker while another session may use it.<br>• PLAN-43 is only waiting for CI now; after it merges, PLAN-44 or PLAN-45 may start in the primary clone. |
| P-7 | **Measure every claim.** Before and after, from the Actions API (`gh run view <id> --json jobs`), never from memory:<br>• per-job durations;<br>• the stack and test times inside the stage logs;<br>• for this PR: a full run, an evidence-only run, and a one-stage rerun.<br>Measure the rerun with `gh run rerun <id> --job <stage-job-id>` on a completed run; no failure needs to be made. |
| P-8 | **Two rounds of tuning at most.** If a target of AC-QA-66e is missed by more than 20% after two rounds of regrouping or splitting, stop tuning. Ship what holds, and record the numbers and the next lever in the final report. |
| P-9 | **Merge, by you, only after green checks.**<br>1. Push the release branch once, in P6, after local CI and the pre-push check are green.<br>2. `gh pr create`; wait («Waiting for CI»).<br>3. Make the evidence commit from the green run's artifact, as PLAN-41 does. Its run is the live proof of reuse.<br>4. Only when every check is green: `gh pr merge <n> --merge`.<br>5. A red check: fix it on the release branch, push that branch only, and wait again. Use `gh run rerun <id> --failed` for a failure that is plainly the environment, and record it.<br>6. Never push to `main` directly, never force-push. |
| P-10 | **No regression.** These stay green:<br>• `AcceptanceCiScopeTests`, the guard, the baseline validator, `Scm001` and the branch protection fixture tests;<br>• `scripts/ci/actionlint.sh`, `ProductionWiringMatrixTests`;<br>• the post-merge dedupe guard (it still skips the push run on `main`). |

## Scope boundaries

**Out of scope:**
- the tests themselves: no class is changed, removed, excluded, made optional or quarantined;
- `ci-local.ps1` and the pre-push gate (record their cost as a follow-up);
- CodeQL's configuration;
- the demo server;
- product code;
- the other workflows, unless the nightly schedule needs one.

**Never commit:**
- the owner's `global.json` edit (check `git diff --cached --name-only` before every commit);
- anything under `artifacts/`, or `tmp-vendors.html`;
- local evidence;
- any secret or token.

**Never:**
- push to `main` or force-push;
- merge with a pending or red check;
- weaken the guard, lower a coverage floor, or exclude a class to get green or fast;
- touch a clone another session works in, PLAN-43's branch or PR, or another session's stack or lock;
- disable host-key checking.

## Stop (record `BLOCKED:` and end the turn) if

- **A precondition fails.**
- **A required check name or branch protection** would have to change (P-2).
- **The parallel layout cannot run every class exactly once** under the same guard.
- **Reuse cannot be made safe** against the S2 «never» list. Then ship S1, S3 and S4 without S2, and record why.
- **Keeping the targets** would need a class excluded, made optional or run less often than S3 allows.

---

## P0 — Timing, preconditions, baseline, inventories

1. Write the `Started (UTC)` line (see «Timing»). Check the Preconditions.
2. `git switch -c feature/plan-66-qa-ci-fast-pipeline origin/main`. Commit this prompt file.
3. **The baseline, into `reports/plan66-baseline.md`:**
   - per-job durations of the last five green and the last five red runs of `ci.yml` on pull requests (`gh run list`, `gh run view --json jobs`), and the causes of the red ones;
   - the stack and test times per stage from their logs;
   - the runner minutes per run;
   - the repository's visibility.
4. **Inventories:**
   - every image the stacks and the tests use (ours and third-party) and its size;
   - what `Get-AcceptanceCodeTree` includes and excludes;
   - what each meta class reads (the S3 trigger paths);
   - whether `E2E` needs anything from `CI` but its success;
   - what `AcQa41AcceptanceBaselineTests` checks, and where it can run;
   - how the dedupe guard and `concurrency` work today.
5. **The plan document** (S5), and D-QA-66-1 and D-QA-66-2 in the register.

Commit: `chore(qa066): baseline of CI durations and failures, inventories of images, code tree, meta inputs and job dependencies; plan document and owner decisions`.

## P1 — The job script runs one stage, and the stage results merge into one evidence (S1, AC-QA-66f)

- `acceptance-job.ps1 -Stage <part:group>[,…]`: runs only those `order[]` entries, with their stacks, overlays and teardown; writes its results under a per-stage folder.
- **A merge script** (`scripts/ci/acceptance-merge.ps1` or similar) assembles the stage results into the layout a sequential run writes. The guard and the baseline validator then run on it unchanged.
- **The image scripts:**
  - build and pull once;
  - save and compress into one archive;
  - load, then check that every service image carries the expected `SOURCE_SHA`.
- **Unit tests:**
  - `-Stage` parsing and selection;
  - an unknown stage fails;
  - the merge of recorded stage outputs equals the sequential layout in structure;
  - the image label check refuses a stale image.
- **Local proof (P-6):** under the lock, run `-Stage lab:main` alone, then compare its results folder with the same stage from a sequential run's layout. Release the lock.

Commit: `feat(qa066): the acceptance job runs chosen stages and their results merge into the one evidence layout; images are built once, archived and checked by revision`.

## P2 — The parallel workflow (S1, AC-QA-66a, AC-QA-66b)

- In `ci.yml`: the build job, the stage jobs (grouped as P0's measurements suggest), and the aggregate `Acceptance`. Set the needs, `if: always()` with explicit result checks, permissions, timeouts, concurrency, and free disk.
- The dispatch inputs (`classes`, `simulate_missing`) reach the stage jobs and the guard as before.
- **Unit tests** that parse `ci.yml` and `ci-scope.json`:
  - every `order[]` entry belongs to exactly one stage job;
  - `Acceptance` needs every stage job and fails on any non-success;
  - the required check names exist;
  - stage jobs that run Playwright use `ubuntu-24.04`.
- `scripts/ci/actionlint.sh` passes.

Commit: `feat(qa066): acceptance stages run as parallel jobs on one set of images; the Acceptance check aggregates them, the evidence and the guard unchanged`.

## P3 — Reuse for an unchanged code tree (S2, AC-QA-66c)

- Add the `run-identity` artifact, the `reuse-check` job, and the short-circuit in `CI`, `E2E`, the build job, the stage jobs and `Acceptance`. When reusing, copy the reused evidence artifact and add a `reuse.json` pointer.
- **Unit tests** for the decision script, for every S2 case:
  - same tree → reuse;
  - a different tree, a fork, a narrowed run, a failed, cancelled or partly skipped run, a push or a schedule → no reuse;
  - a chain of reuses → points at the original run.

Commit: `feat(qa066): a commit that leaves the code tree unchanged reuses the PR's green full run for CI, E2E and Acceptance, with the evidence copied and the reused run named; CodeQL and Gitleaks always run`.

## P4 — Governance checks on demand, nightly on main (S3, AC-QA-66d)

- Add the meta rule and its trigger paths to `ci-scope.json`; change the guard, the baseline validator and `AcceptanceCiScopeTests` to match; add the nightly schedule; add the evidence wording «not run by rule» with the link to the last nightly.
- **Unit tests:**
  - a PR touching a trigger path → meta required;
  - a PR touching none → meta «not run by rule» and accepted;
  - a non-meta class marked «not run by rule» → refused;
  - schedule and dispatch → meta required.

Commit: `feat(qa066): governance checks of the CI run when their inputs change, nightly on main and on manual runs; otherwise recorded as not run by rule`.

## P5 — The rest of the critical path, docs, review, local gates (S4, S5)

1. **S4:** decouple or split, only where the measurements show the critical path moves. Add unit tests for any new job graph.
2. **Docs:**
   - `docs/CI_AND_TESTING.md`: the new layout, reuse, meta on demand, the local policy;
   - `docs/GITHUB_CI_GOVERNANCE_RUNBOOK.md`: rerunning one stage, reading a reused run, the nightly;
   - `docs/PARALLEL_PLANS.md`: shorter local acceptance runs;
   - the plan document's «الحالة».
3. **Independent review** (one fresh subagent). It reads this prompt and the diff, and hunts for:
   - a class that runs twice or never;
   - an `Acceptance` that can be green with a failed, skipped or cancelled stage;
   - a reuse across different code trees, forks or narrowed runs;
   - a meta class silently dropped;
   - an image from a registry in a stage job, or a stale image;
   - a secret reaching an artifact;
   - a required check name that changed.

   Use a refuter only for high findings. Fix what survives, with tests.
4. **Local CI, then the pre-push check:**
   - `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/ci-local.ps1`
   - then `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/plan035-prepush.ps1`

   Both must be green (P-6 for Docker).

Commit: `docs(qa066): CI layout, reuse, governance on demand and the local policy documented; review fixes`.

## P6 — The PR, the measured runs, the merge (P-7, P-9)

1. If `origin/main` moved, merge it (P-1). Merge the feature branch into a new `release/plan-66` from `main` (`--no-ff`). Write `reports/plan66-pr.md`.
2. Push and open the PR:
   - `git push -u origin release/plan-66`
   - `gh pr create --base main --head release/plan-66 --title "PLAN-66: a faster CI (parallel acceptance stages, reuse for an unchanged code tree, governance checks on demand)" --body-file reports/plan66-pr.md`
3. Wait for the checks. This PR changes `.github/`, so meta runs: that is AC-QA-66d's positive case. Fix, and wait again, until green. Record each run's per-job durations: the full run of AC-QA-66e.
4. **The one-stage rerun:** `gh run rerun <green run id> --job <a stage job id>`. Record its duration.
5. **The evidence commit:** commit the evidence from the green run's artifact, as PLAN-41 does, and push. Its run must **reuse** (AC-QA-66c) and finish within the target (AC-QA-66e). Record both.
6. Only when every check is green: `gh pr merge <n> --merge`. Then `git switch main; git pull --ff-only`, and verify that `main` = `origin/main`.
7. **After the merge:**
   - confirm the dedupe guard skipped the push run;
   - dispatch `simulate_missing`: `Acceptance` must end **red** (AC-QA-66b);
   - dispatch the nightly once: meta runs and passes (AC-QA-66d);
   - record the three runs.

Record the PR link, every run, the merge commit and the stamp `Merged by agent on owner instruction (UTC): <timestamp>` in `reports/plan66-progress.md`. That note rides with the next plan's first commit; **do not open a second PR for it.**

## P7 — Final report

**`reports/plan66-final-report.md`** (English):
1. Summary per phase.
2. Commits.
3. Decisions applied and deviations.
4. **Before and after**, per job and for the whole run, from the Actions API:
   - a normal PR run;
   - an evidence-only run;
   - a one-stage rerun;
   - runner minutes.
5. The stage grouping and why.
6. The reuse rules and their tests.
7. The meta rule, its trigger paths, and the nightly.
8. The proofs: AC-QA-66a..f, each with its test or run link.
9. What is left, and the next lever (for example, the cost of `ci-local.ps1` and the pre-push check, 35 minutes in PLAN-43).
10. **Timing:**
    - total wall-clock;
    - active time against waiting;
    - the output of `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/plan-timing.ps1 -From 25 -To 66`.
11. **A note for the next plans:** what a plan does differently now:
    - local acceptance only on its classes;
    - `-Stage`;
    - how to read a reused run;
    - when meta runs.

Commit it on `main` only through the next plan's first commit (the PLAN-41 rule). Keep it in the working tree.

## Definition of done

- **The layout:** `Acceptance` runs as parallel stage jobs on one set of images, under the same required check name. Every class runs exactly once, and meta runs by rule. The evidence and the guard are unchanged in meaning.
- **Reuse:** a commit that leaves the code tree unchanged reuses the PR's green full run, and only then.
- **Meta:** governance checks run when their inputs change, nightly on `main`, and on manual runs.
- **The targets of AC-QA-66e are met,** or P-8 is applied and recorded.
- **The merge:** done by you, through a PR, after green checks; the post-merge proofs are recorded.

Then reply with a short summary in Arabic:
- the durations before and after (a normal run, an evidence-only run, a one-stage rerun);
- what changed in how plans are tested;
- the PR link;
- the path of the final report.
