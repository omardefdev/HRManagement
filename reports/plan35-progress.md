Started (UTC): 2026-10-30T13:38:10.4485293Z

# PLAN-35 progress (checkpoint)

Branch: `feature/plan-35-qa-push-remote-ci` (from `main` = `c2b0d3f`).

## Phase status

| Phase | Status | Finished (UTC) |
|-------|--------|----------------|
| P0 | done | 2026-10-30T14:07:43.5005632Z |
| P1 | done | 2026-10-30T14:07:43.5005632Z |
| P2 | pending | |
| P3 | pending | |
| P4 | pending | |
| P5 | pending | |
| P6 | pending | |
| P7 | pending | |
| P8 | pending | |

## Owner interactions

- Decisions message (D-QA-15, D-QA-1, D-QA-16, P-2, D-QA-2) sent in chat.
  Waiting for owner (UTC): 2026-10-30T13:39:05.0830242Z
- Owner logged `gh` in as `omardefdev` (was `omar2def`, READ only) - reported in chat.
  Owner action done (UTC): 2026-10-30T13:54:07.0169820Z (decisions still open)

## GitHub state (P0, verified as omardefdev)

- `omardefdev/HRManagement`: PUBLIC, viewer ADMIN, default branch `main`, account plan **free**.
- `origin/main` = `9cb3705` (3 commits); local `main` is 160 commits ahead; `origin/main` is an ancestor.
- **Live branch protection on `main` already exists** (the 404 seen as `omar2def` was a permission artefact):
  contexts CI/CodeQL/Gitleaks strict, 2 approving reviews + code-owner + last-push approval, required signatures,
  enforce_admins, required linear history, no force push/deletion. Saved: `artifacts/plan35/github/protection-before.json`.
  Consequence: the planned `git push origin main` fast-forward (unsigned commits, merge commits) is refused by this
  protection -> owner decision needed before P6 (see Open issues).
- No rulesets, no Actions variables, no secrets. Actions enabled (all actions allowed).
- Workflows on GitHub: scheduled ones are `disabled_inactivity` (advisory-to-issue, codeql, e2e-journeys, iac-drift, mutation).
- `@omard` (CODEOWNERS) = user `omarD` id 6695527, not the owner -> CODEOWNERS will name `@omardefdev`.
- No real Python on this Windows machine (Store aliases only); the pre-push script runs the non-prod check in the ci-local image.

## Tool pins

`docs/qa/tool-pins.json`: gitleaks 8.28.0 `sha256:cdbb7c95...`, actionlint 1.7.7 `sha256:887a259a...`.

## Decisions

- gitleaks: the old `.gitleaks.toml` had no rules and no `[extend] useDefault`, so every scan (CI and local) passed
  trivially. Fixed: default rules + two narrow path-AND-match allowlists (qa003 Testcontainers realm secrets, the only
  default-rule findings over the whole history). No allowlist for `-lab-only`/`hrm_ci_`/CI passwords: the default
  rules do not flag them, and an unused allowlist only widens the gate.
- forbidden-files: commit `544ba7a` (PLAN-26 snapshot) touches 4 `testdata/*/out/*.json` reports (counts/dates only);
  allowed by commit+path in the script, listed in the report. global.json per P-3.
- actionlint (with shellcheck) over the current workflows: only the literal `\n` in `e2e-journeys.yml:148` (fixed in P2).

## Files changed

- P0/P1: `reports/plan35-progress.md`, `reports/plan35-baseline.md`, `docs/qa/tool-pins.json`, `.gitleaks.toml`, `scripts/plan035-prepush.ps1`, `scripts/scm/pre-push-guard.ps1`, `scripts/scm/install-hooks.ps1`, `docs/acceptance-plans/execution/PLAN-35-AGENT-PROMPT.md`

## Open issues

- OI-1: live protection blocks the first push of `main` (needs owner decision; asked in chat).
- OI-2: owner decisions D-QA-15/D-QA-1/D-QA-16/P-2/D-QA-2 open (defaults apply at P4).

## Next step

P2: workflows (e2e-journeys done, mutation done, advisory-to-issue + sync script done), then CodeQL gate, duplicates, schedule guards, acceptance disable, lint tests, CI realm roles.

## P0/P1 results (2026-10-30T14:07:43.5005632Z)

- Baseline: `reports/plan35-baseline.md` (ci-local on `c2b0d3f`: 85 passed, 0 failed, 2 not-run; 160 commits ahead).
- `scripts/plan035-prepush.ps1` on `c2b0d3f` (gitleaks, forbidden, signatures, nonprod): gitleaks history 172 commits
  and range 153 commits clean; forbidden PASS (4 allowed out-files of `544ba7a`, global.json = pin); nonprod PASS;
  signatures N=160. actionlint FAIL (the literal `\n` in e2e-journeys.yml) -> fixed in P2.
- AC-QA-35a negatives (temp repos under %TEMP%\plan35-neg): planted random `ghp_` token -> exit 1,
  `deploy.env.sample:1 rule=github-pat`; `global.json` rollForward latestPatch -> exit 1 naming the commit. (A
  placeholder-looking token is skipped by gitleaks' default stopwords; the test must generate a random one.)
- `scripts/scm/pre-push-guard.ps1` self-test: push to main with -ProtectMain -> exit 1; branch push -> gitleaks+forbidden PASS.
- Build `dotnet build HRManagement.slnx -warnaserror`: 0 warnings, 0 errors.
