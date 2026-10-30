# Task: PLAN-35 — First push and remote CI: pre-push secret and file checks over the whole history, honest workflow gates (CodeQL SARIF gate, no duplicates, no broken steps, schedules behind a switch), remote CI green on a PR then on `main`, branch protection applied and verified (one session, with owner actions)

## How to run this task — read before anything else

This is a **single prompt for one session**. Run it as phases **P0–P8** in order.

Some steps are **owner actions (O)**. The owner pushes, changes repository settings and applies protection; **you never push to `main`**. At each owner action:
1. Write `OWNER ACTION:` in the progress file, with the exact commands, and stamp `Waiting for owner (UTC): <timestamp>`.
2. Commit what is complete.
3. End the turn with the commands in a short numbered list.

When the owner replies (for example «تم»), stamp `Owner action done (UTC): <timestamp>`, verify the result yourself (`gh`, `git`), and continue.

**At the end of every phase:**
1. Build: `dotnet build HRManagement.slnx -warnaserror`, 0 errors.
2. Run the phase's tests.
3. Commit, using the phase's commit message.
4. Update **`reports/plan35-progress.md`** — your checkpoint. It contains:
   - phase status;
   - decisions (with reasons);
   - files changed;
   - test and CI results with run links;
   - open issues;
   - the exact next step.

**If you are resuming:** at start, or after any context compaction or restart, read `reports/plan35-progress.md` and `git log --oneline -20` **first**. Continue from the recorded next step. Never redo a committed phase.

**Timing (mandatory, the owner compares plans):**
- The **very first action** of the session is to create `reports/plan35-progress.md`. Its first line is:
  `Started (UTC): <timestamp>`
  where the timestamp comes from `(Get-Date).ToUniversalTime().ToString('o')`.
- Every phase records `Finished (UTC):`.
- Every resume records `Resumed (UTC):` with the reason.
- Every owner action records the `Waiting for owner` / `Owner action done` pair.
- Every remote CI run records its start and end and its billable minutes (`gh run view <id> --json`, or the run's timing page).
- Never edit an earlier stamp.

**Economy mode** (the owner runs this session **without ultracode**, at `xhigh`):
- Use **at most two subagents at a time**: for broad read-only searches, and for root-causing separate CI failures.
- Run **one independent review** at the end. Use a refuter only for findings rated high.
- Write long outputs (CI logs, test runs, gitleaks reports) to files under `artifacts/plan35/`. Read only the failing parts: `gh run view <id> --log-failed` first, never the full log.
- Do not re-read large documents you have already read in this session; take notes in the progress file instead.

**Blocking decisions.** When a "Stop" condition is hit:
1. Write `BLOCKED:` with the question and options.
2. Commit what is complete.
3. End the turn. **Do not guess.**

---

## Role

You are a senior DevOps / CI engineer: GitHub Actions, the `gh` CLI, gitleaks, CodeQL and SARIF, branch protection, PowerShell (5.1 and 7) and bash. You work on the `HRManagement` repository on the owner's Windows machine (PowerShell **5.1 only, no `pwsh`**, Docker Desktop, culture ar-SA).

PLAN-34 made `main` locally green: 87 CI steps from `scripts/ci/steps.json`, identical on Linux and Windows. This plan puts ~160 local commits on GitHub for the first time and makes the remote gates real.

## Read first (mandatory)

1. `docs/acceptance-plans/PLAN-35-qa-push-remote-ci.md` — **the plan; it is the specification.**
2. `reports/plan34-final-report.md`:
   - **§8:** the defects recorded for later. Several affect remote CI: the CI realm issues tokens without realm roles, so E2E/acceptance get 403; `auth-directory-web` crashes on a cold start; ci-local has no `python` alias.
   - **§9:** the readiness checklist.
   - **§10:** timing.
3. `docs/PRG_001_QUALITY_GAP_STUDY.md`: G-QA-01, 07, 11, 15, and §5 decisions D-QA-1, 2, 15–18.
4. `docs/CI_AND_TESTING.md`, `docs/GITHUB_CI_GOVERNANCE_RUNBOOK.md`, `docs/SCM_001_BRANCH_PROTECTION.md`.
5. All of `.github/workflows/*.yml`, `scripts/ci/steps.json`, `scripts/ci/run-steps.sh`, `.gitleaks.toml`, `.github/CODEOWNERS`, `docs/scm-governance/branch-protection-main.json`, `scripts/scm001-branch-protection-validate.ps1`, `docs/qa/dated-obligations.json`.

## Decisions for this implementation (ask the owner in P0, then record)

Ask these **together in P0**, in one short Arabic message. Then continue with the work that does not depend on the answers (P1–P3), so the session does not stall the way PLAN-34 did (7 h 54 min waiting for decisions).

| # | Question to the owner | Default if no answer by P4 |
|---|-----------------------|----------------------------|
| D-QA-15 | Make the GitHub repository **private** before the push? | **Yes, private.** See the trade-offs below. |
| D-QA-1 | Protect `main`: PR required, the required checks green and `strict`, 0 reviews (temporary, dated), `enforce_admins`, no force-push or deletion? | Yes. If the repo is private on a free plan where protection is unavailable: a local `pre-push` guard plus a dated obligation. |
| D-QA-16 | Require signed commits? | No, until the owner sets up a signing key; record it as a dated exception. Never rewrite history for signatures. |
| P-2 | May the agent push **only to the `release/plan-35-first-push` branch** and to the negative-check branch, to speed up CI triage? It never pushes to `main`. | No: the owner pushes every fix. |
| D-QA-2 | Required checks: `CI`, `CodeQL`, `Gitleaks`, and `E2E` after two consecutive green runs. | As stated. |

**Private-repository trade-offs** (state them to the owner in the P0 message; each is «يحتاج تحقق» for their account plan):
- branch protection on private repos may need a paid plan;
- uploading code-scanning results needs GitHub Code Security, so the local SARIF gate is used instead;
- Actions minutes are limited on private repos.

**Prompt-level decisions:**

| # | Decision |
|---|----------|
| P-1 | **E2E must be green, and PLAN-34 found that CI realm tokens carry no realm roles** (`prepare-ci-compose-env.sh`; evidence `acceptance-evidence/AC-QA-34/db-checks-34g.json`). Fix that here **if the fix is in the CI realm or compose preparation** (for example a `roles` client scope, and accepting the `profile`/`email` scopes). Do the same for `auth-directory-web`'s cold-start crash in CI: a retry or restart policy in the CI compose path only. A fix that needs product code goes through triage rule 2 (quarantine with a dated obligation) and is reported. |
| P-3 | **The forbidden-files check must allow the deliberate `global.json` pin commit `544ba7a`** (2026-10-21, SDK 10.0.302 → 10.0.400, `rollForward: disable`). The rule is: `HEAD:global.json` equals that pinned content, and no commit in the range sets `rollForward` to anything but `disable`. The owner's local `latestPatch` edit stays local. |
| P-4 | **Scripts the owner runs must work in Windows PowerShell 5.1** (ASCII only, as in PLAN-34 S-3). CI runs `pwsh` 7. Every script command in reports and owner instructions uses `powershell -File …`. |
| P-5 | **Merging after protection:** once `main` is protected (P7), the final report reaches `main` only through a PR with green checks. That PR is also the positive half of AC-QA-35d. |

## Scope boundaries

- **Out of scope** (from the plan):
  - fixing tests locally → PLAN-34 (done);
  - running and requiring the acceptance job → PLAN-41 (it stays disabled here behind `ENABLE_ACCEPTANCE_JOB`, with a dated obligation);
  - turning shallow journeys into real ones → QA-H;
  - `deploy.yml` and its secrets → PLAN-36;
  - a real second reviewer → recorded as a dated obligation.
- **Never commit:**
  - the owner's local `global.json` edit — check `git diff --cached --name-only` before every commit;
  - `tmp-vendors.html`, `scripts/__pycache__/*`, `docs/thesis_diagrams/source/.$*`, `artifacts/`;
  - test-rewritten `testdata/*/out/*.json` — use `git checkout -- testdata/*/out`, never a blanket `git checkout -- testdata`;
  - secrets, tokens, the unredacted gitleaks output.
- **Never:**
  - push to `main` yourself;
  - force-push;
  - rewrite history without the owner's explicit decision;
  - lower a coverage floor, delete a CI step, or silence a gate to get green.

## Repository facts (verified 2026-10-30 16:15 — re-verify in P0)

- `main` = `c2b0d3f` (PLAN-34 merged at `76169dc`, plus two report commits). `origin/main` is 160 commits behind.
- The GitHub repository `omardefdev/HRManagement` is **public** and holds 3 commits.
- The working tree has only the local `global.json` edit and the untracked `tmp-vendors.html`.
- **PLAN-34 outcome:**
  - Linux ci-local 85 passed, 0 failed, 2 not-run (Testcontainers and the coverage gate, through the Docker Desktop proxy). Both pass natively on Windows and are expected to run normally on `ubuntu-latest`.
  - AC-QA-34g is Blocked: the CI realm issues no roles.
- **Expected on the first remote run** (PLAN-34 §9):
  - `build-and-test` green;
  - `e2e` and `acceptance` red (realm roles; the acceptance job is PLAN-41's);
  - `codeql` and `gitleaks` jobs plus separate `codeql.yml`/`gitleaks.yml` duplicates;
  - the schedules (`e2e-journeys`, `mutation`, `advisory-to-issue`, CodeQL weekly, `iac-drift`) activate on push.
- **Dated obligations:** the nearest is 2026-12-14 (ObjectStorage CI exclusion), so no time bomb before this plan ends.

## Stop (record `BLOCKED:` and end the turn) if

- **gitleaks finds a real secret.** For unpushed commits, the plan's default (D-QA-17) is a `git filter-repo` rewrite, which changes every later SHA (including those cited in reports). Present the findings, redacted, with the options: rewrite plus rotation, or rotation alone if the value is lab-only and allow-listed with a reason. Wait for the owner.
- **A remote failure is a product defect that blocks a required check** and cannot be quarantined under the triage rules without hiding a real regression.
- **The owner's account cannot apply branch protection** and does not accept the local-guard fallback.
- **Actions minutes are clearly insufficient** for the triage loop on a private repository. Report the measured minutes per run and ask.

---

## P0 — Timing, baseline, GitHub state, owner questions

1. `Started (UTC)` line (see «Timing»). Then:
   - `git status`;
   - `git switch -c feature/plan-35-qa-push-remote-ci` from `main`.
2. **Baseline:**
   - Run the local CI on HEAD: `powershell -File scripts/ci-local.ps1 -SkipTestcontainers`; the Testcontainers step and the coverage gate run natively.
   - `git fetch origin`; `git rev-list --count origin/main..main`.
   - Record both in `reports/plan35-baseline.md`.
3. **GitHub state:**
   - Run `gh --version` and `gh auth status`. If `gh` is missing or not authenticated → **owner action**: `winget install GitHub.cli` and `gh auth login`. Continue with the offline phases meanwhile.
   - Then record:
     - `gh repo view --json visibility,owner,defaultBranchRef,isPrivate`;
     - the protection state (`gh api repos/{owner}/{repo}/branches/main/protection`; 404 means none);
     - the account plan, if visible;
     - whether `@omard` (`CODEOWNERS`) exists: `gh api users/omard`.
4. **Tool pins:** pull the gitleaks 8.x and actionlint 1.7.x images and record their digests in `docs/qa/tool-pins.json`.
5. **Send the owner the decisions message** («Decisions» table, in Arabic, short). Stamp the wait, but **do not end the turn**. Continue to P1 and collect the answers when the owner writes.

Commit: `chore(qa035): baseline, GitHub state, tool pins`.

## P1 — Pre-push check over the whole history (plan §1)

- **`scripts/plan035-prepush.ps1`** (PS 5.1 compatible). It writes `artifacts/plan35/prepush/report.md`, exits non-zero on any failure, and **never pushes**. Its checks:
  1. gitleaks over the **whole history** (`detect --source . --redact`) **and** over the range `origin/main..HEAD`. Reports are redacted, under `artifacts/plan35/prepush/`.
  2. actionlint on `.github/workflows/*.yml`.
  3. The ci-local summary exists, its `gitSha` equals HEAD, and it is green.
  4. Forbidden files in the range: `.env`, `.acceptance/`, `artifacts/`, `testdata/*/out/*.json`, files over 50 MB. `global.json` is handled per P-3.
  5. `python scripts/check-non-prod-data-policy.py` (use `python3` where `python` is missing).
  6. Signature status is printed; it fails only if D-QA-16 requires signing.
  7. It prints the owner's exact push commands.
- **`.gitleaks.toml`:** a narrow `[[allowlists]]` for explicitly marked lab values (`-lab-only`, `hrm_ci_`, the CI test passwords). Each entry matches path **and** pattern, with a reason comment.
- **`scripts/scm/pre-push-guard.ps1` and `scripts/scm/install-hooks.ps1`:** an optional local hook running the fast checks.
- **Run it now.**
  - Real secret → **Stop**.
  - Lab-only value → allow-list it with a reason, and list it in the report.
- **Tests:**
  - AC-QA-35a positive;
  - the negative: a temp repo under `$env:TEMP` with a planted secret, and another with a `global.json` `rollForward` change → exit 1 naming the file;
  - `Scm001BranchProtectionExceptionTests` is written in P3.

Commit: `ci(qa035): pre-push check over the whole history (gitleaks, actionlint, forbidden files, ci-local summary), narrow lab allow-list, optional pre-push hook`.

## P2 — Honest workflows (plan §2) and the CI realm (P-1)

Apply the plan's §2 table:
- **`e2e-journeys.yml`:** replace the literal `\n` with a real multi-line `run: |`.
- **CodeQL gate:**
  - `upload: ${{ vars.CODE_SCANNING_UPLOAD == 'true' }}` plus `output: sarif-results`;
  - `scripts/ci/codeql-sarif-gate.py` with the baseline `docs/qa/codeql-baseline.json`, whose entries are registered in `docs/qa/dated-obligations.json`;
  - `CodeqlSarifGateTests`.
- **Remove duplicates:** delete `gitleaks.yml` and `gateway-ci.yml`. `codeql.yml` keeps only `schedule` and `workflow_dispatch`. Update `docs/CI_AND_TESTING.md`.
- **`mutation.yml`:** `global-json-file`, the Stryker tool path, `--locked-mode`.
- **`sync-dependabot-alerts-to-issues.sh`:** fail explicitly on any non-200 response. `DEPENDABOT_ALERTS_TOKEN` is optional; without it the job is `workflow_dispatch` only, plus a dated obligation.
- **Schedule guard:** `vars.ENABLE_SCHEDULED == 'true'` on every scheduled job except `dated-obligations.yml`.
- **Acceptance job:** renamed `Acceptance`, behind `vars.ENABLE_ACCEPTANCE_JOB == 'true'`, with a summary line «معطّلة حتى PLAN-41» and a dated obligation (+45 days, owner PLAN-41).
- **`WorkflowYamlLintTests`:** no literal `\n` inside `run`; every `dotnet restore` uses `--locked-mode`. Add the actionlint step to `scripts/ci/steps.json`, keeping `CiStepsParityTests` green.
- **CI realm roles (P-1):**
  - make the CI realm issue realm roles in password-grant tokens;
  - extend AC-QA-34g's evidence check so it now passes, recording the before and after;
  - the `auth-directory-web` cold start gets a retry or restart policy in the CI compose path only.

Run locally:
- the affected unit tests;
- actionlint;
- the base stack + an upload as a persona (the AC-QA-34g positive);
- the E2E job's commands as far as they run locally.

Commit: `ci(qa035): workflow gates made real (CodeQL SARIF gate, no duplicates, schedules behind a switch, acceptance disabled until PLAN-41), CI realm tokens carry roles, workflow lint tests`.

## P3 — Branch protection as code (plan §4)

- **`docs/scm-governance/branch-protection-main.json`:** values per D-QA-1/2/16, with `exceptionRefs` for each relaxation, registered in dated obligations.
- **`scm001-branch-protection-validate.ps1`:**
  - accepts a relaxation only with an open, unexpired obligation;
  - `-CheckLiveGitHub` compares the live protection with the file;
  - add `Scm001BranchProtectionExceptionTests`.
- **CODEOWNERS:** use the real owner login from P0.
- **`scripts/scm/apply-branch-protection.ps1`:** for the owner (PS 5.1).
- **Docs:** `GITHUB_CI_GOVERNANCE_RUNBOOK.md` (visibility, actual required checks, the secrets inventory, the disabled job), `SCM_001_BRANCH_PROTECTION.md`.

Commit: `chore(qa035): branch protection as code with dated exceptions, live check, CODEOWNERS owner, governance docs`.

## P4 — Ready to push (owner action)

1. Apply the owner's answers, or the defaults, and record them.
2. Run `powershell -File scripts/plan035-prepush.ps1`; it must be green.
3. Write `reports/plan35-first-push.md`, the PR description. It summarises PLAN-25..35, what CI will run, and what is expected to be quarantined.
4. Merge this branch into local `main` (`--no-ff`). Then create `release/plan-35-first-push` from `main`.
5. **OWNER ACTION** (end the turn with these commands):
   1. If private was chosen: `gh repo edit --visibility private --accept-visibility-change-consequences`
   2. `git push -u origin release/plan-35-first-push`
   3. `gh pr create --base main --head release/plan-35-first-push --title "First push: PLAN-25..35" --body-file reports/plan35-first-push.md`

Commit before ending the turn: `docs(qa035): first-push PR description, pre-push report`.

## P5 — Remote CI triage on the PR (plan §3.4–3.5)

Loop until `CI`, `CodeQL`, `Gitleaks` and `E2E` are green on the PR:
1. `gh run list --branch release/plan-35-first-push --limit 10`, then `gh pr checks <pr>`.
2. For each failure:
   - `gh run view <id> --log-failed > artifacts/plan35/runs/<id>.log`;
   - `gh run download <id> -D artifacts/plan35/runs/<id>`;
   - root-cause it (at most two subagents in parallel for separate failures).
3. Fix on `release/plan-35-first-push`, one small commit per failure, with a message naming the step. Rerun that step locally (`run-steps.sh --step` or ci-local) before pushing.
4. Pushes go per P-2: the owner pushes, or the agent pushes to the release branch only if allowed.
5. Triage rules 1–4 of the plan:
   - an environment failure → fix the workflow or script;
   - a real test failure → fix it if it is in the test or host; quarantine a product defect with a dated obligation and report it;
   - a shallow E2E failing on a product issue → `tests/e2e/quarantine.json`;
   - never lower floors or delete steps.
6. Record every run: link, result, duration, billable minutes.
7. `E2E` must be green **twice in a row** before it becomes required (D-QA-2).

## P6 — `main` on GitHub (owner action, then verify)

1. **OWNER ACTION:** `git switch main; git merge --ff-only release/plan-35-first-push; git push origin main`.
2. **Verify:**
   - the `push` run on `main` at the same SHA is green for every enabled job;
   - `git rev-parse main` = `git rev-parse origin/main`.
3. Record the links.

## P7 — Protection, schedules, journeys, negative check (owner actions, then verify)

1. **OWNER ACTION:**
   - `powershell -File scripts/scm/apply-branch-protection.ps1`, or install the local guard if protection is unavailable;
   - `gh variable set ENABLE_SCHEDULED --body true`;
   - `gh workflow run e2e-journeys.yml --ref main`;
   - `gh workflow run dated-obligations.yml --ref main`.
2. **Verify:**
   - `powershell -File scripts/scm001-branch-protection-validate.ps1 -CheckLiveGitHub` passes;
   - `e2e-journeys` `recruitment-journey` prints `Passed: 1, skipped: 0` twice (lab and prod-like). A failure here is triaged like P5;
   - `dated-obligations.yml` is green.
3. **Negative check (AC-QA-35d):**
   - a branch `plan35-negative-check` with a deliberately red test, pushed per P-2, plus a PR;
   - `gh pr view --json mergeStateStatus` = `BLOCKED`;
   - close the PR and delete the branch;
   - a direct push to `main` is refused (only if protection is applied; with the local guard, show the guard refusing).

## P8 — Acceptance, docs, review, final report (through a PR)

1. **`AcQa35RemoteCiTests`:** `[RequiredAcceptance]`, `AcceptanceGate`, `EvidenceRecorder("AC-QA-35", …)`. It reads run numbers and protection through `gh api`; without an authenticated `gh` it is `Blocked`.
   - Run AC-QA-35a..g with `RUN_ACCEPTANCE=true` and `ACCEPTANCE_REQUIRE_ALL=true`.
   - Commit the folder `acceptance-evidence/AC-QA-35/` and the regenerated `summary.md`.
2. **Docs:**
   - in `docs/PRG_001_QUALITY_GAP_STUDY.md`, update the «الحالة» lines for G-QA-01/07/11/15, and record in the header the SHA on which remote CI went green;
   - set the plan's `Status`, and add «ملاحظات التنفيذ والانحرافات»;
   - update `docs/CI_AND_TESTING.md`.
3. **Independent review** (one fresh subagent): it reads this prompt, the plan and the diff, and hunts for:
   - a gate that cannot fail;
   - a quarantined real regression;
   - an allow-list wider than needed;
   - a required-check name mismatch between the workflows, the protection file and the live settings;
   - a secret in reports.

   Use a refuter only for high findings. Fix what survives, with tests.
4. **`reports/plan35-final-report.md`** (English), with these sections:
   1. Summary per phase.
   2. Commits.
   3. Owner decisions and when they came.
   4. gitleaks results (redacted) and their handling.
   5. Every remote run: link, result, duration, minutes.
   6. Quarantines with owners and dates.
   7. Protection: file vs live.
   8. Workflows changed or removed.
   9. Decisions and deviations.
   10. Defects for later plans.
   11. **Timing:** total wall-clock; **active time vs owner-wait time** from the stamps; time waiting on remote CI; total Actions minutes; and the output of `powershell -File scripts/plan-timing.ps1 -From 25 -To 35`.
   12. A readiness note for PLAN-36.
5. **Through a PR (P-5):**
   - create the branch `docs/plan-35-final-report` from `main`;
   - commit: `docs(qa035): remote CI green, protection applied, gap status lines, final report`;
   - **OWNER ACTION:** push it, open the PR, merge after the green checks (`gh pr merge --merge`);
   - verify `main` = `origin/main` and the post-merge run is green.

## Definition of done

- D-QA-15 is recorded before the push. Whole-history gitleaks is clean, or every finding is handled per the owner's decision.
- Remote `main` = local `main`. The last `push` run on `main` is green for `CI`, `CodeQL`, `Gitleaks` and `E2E`, with links.
- No duplicate or broken workflow. Schedules are enabled only after green. One manual `e2e-journeys` run passed both profiles.
- The CodeQL gate fails on new high-severity findings (`CodeqlSarifGateTests`).
- Live protection matches the file, or the documented local guard is installed. CODEOWNERS names a real account.
- AC-QA-35a..g = Pass, with evidence.
- The final report is merged through a protected PR, with the timing section that separates active time from owner waits.

Then reply with a short summary, the PR and run links, and the path of the final report.
