# First push: PLAN-25..35

This PR puts about 166 local commits on GitHub for the first time (local `main` was 160 commits ahead of `origin/main` =
`9cb3705`, plus the PLAN-35 merge). It is the CI gate before `main` is fast-forwarded (PLAN-35 §3). Nothing is squashed
or rewritten: `origin/main` is an ancestor of this branch.

## What is in it

| Plan | Scope |
|------|-------|
| snapshot `544ba7a` | Working tree before PLAN-26, including the SDK pin 10.0.302 → 10.0.400 (`global.json`, `rollForward: disable`) |
| PLAN-25 | Recruitment approvals bound to production; server-side permission enforcement (HCM-C) |
| PLAN-26 | Evaluation criteria, interview cycle, vacancy-request integrity (HCM-D) |
| PLAN-27 | Offer-letter integrity, merit principle, reserve list (HCM-E) |
| PLAN-28 | Selection-committee governance and appeals (HCM-F) |
| PLAN-29 | Appointment closure; contract, payroll and official-document integration (HCM-G) |
| PLAN-30 | Candidate experience, unified channel, notifications (HCM-H) |
| PLAN-31 | Multi-vacancy competitions and tests (HCM-I) |
| PLAN-32 | Recruitment reports and KPIs (HCM-J) |
| PLAN-33 | Live verification of the recruitment flow; documentation fixes (HCM-K) |
| PLAN-34 | Pushable baseline: local CI identical to remote (`scripts/ci/steps.json`), dated obligations, zero red tests (QA-A) |
| PLAN-35 | This push: pre-push checks, honest workflow gates, CI realm roles, branch protection as code (QA-B) |

Reports: `reports/plan25-final-report.md`, `reports/plan30-…34-final-report.md`; PLAN-35: `reports/plan35-progress.md`.

## Checked before the push (`scripts/plan035-prepush.ps1`)

- gitleaks 8.28.0 (pinned by digest) over the **whole history** and over `origin/main..HEAD`: no leaks. The old
  `.gitleaks.toml` defined no rules, so every earlier scan passed trivially. The default rules are now active, with two
  path-AND-match allow-lists for the throwaway qa003 Testcontainers realm secrets.
- actionlint 1.7.7 (with shellcheck) on every workflow: no findings.
- ci-local (Linux container, clean clone of the pushed HEAD): see `reports/plan35-progress.md` (P4).
- Forbidden files in the range: none. The four regenerated `testdata/*/out` reports of `544ba7a` are allowed by
  commit+path. `global.json` equals the pin commit.
- GOV-001-D non-prod data policy: OK. Commits are unsigned (D-QA-16: signing not required, dated exception).

## What CI runs on this PR (`.github/workflows/ci.yml`)

| Job (check name) | Expected |
|------------------|----------|
| `CI` | the 88 steps of `scripts/ci/steps.json` (new: `Workflow lint (actionlint)`). Locally green; Testcontainers and the coverage gate run here natively |
| `E2E` | base stack via `scripts/ci/compose-up-retry.sh` (file-storage volume ownership, one cold-start retry), Playwright suite. Locally 53/57 before the SEC-001 fix; the 3 others were transient local navigation stalls (re-ran green) |
| `CodeQL` | analysis + `scripts/ci/codeql-sarif-gate.ps1` (new error / security-severity ≥ 7.0 outside `docs/qa/codeql-baseline.json` fails). The baseline is empty, so the first run shows whether entries are needed |
| `Gitleaks` | pinned gitleaks over the whole history (`scripts/ci/gitleaks.sh`) |
| `Acceptance disabled notice` | prints «Acceptance: معطّلة حتى PLAN-41»; the `Acceptance` job is skipped (`ENABLE_ACCEPTANCE_JOB` unset, obligation `CI-ACCEPTANCE-DISABLED`) |

Not run by this PR: `deploy.yml` (PLAN-36), and the schedules (`ENABLE_SCHEDULED` unset).

## Fixed on the way (PLAN-35)

- CI realm: password-grant tokens now carry realm roles. The realm's own `clientScopes` list had replaced Keycloak's
  built-in roles/profile/email scopes. AC-QA-34g: Blocked → Pass.
- SEC-001: the 14 `/health/upstreams/*` gateway routes were anonymous (snapshot `544ba7a`); they require the dashboard
  policy again.
- Workflows: e2e-journeys literal `\n`; duplicate `gitleaks.yml` and `gateway-ci.yml` removed; CodeQL per-PR only in
  `ci.yml`; mutation SDK/tool path; Dependabot sync fails on a refused token; `--locked-mode` everywhere.

## Expected quarantines

None at the time of writing (`tests/quarantine.json` and `tests/e2e/quarantine.json` are empty). Any quarantine added
during triage is listed in `reports/plan35-final-report.md`, with an owner and a review date.

## After this PR is green

1. The owner fast-forwards `main` (`git merge --ff-only release/plan-35-first-push; git push origin main`), after
   removing the pre-PLAN-35 protection, which requires signed commits, 2 reviews and linear history (owner decision OI-1).
2. The protection from `docs/scm-governance/branch-protection-main.json` is applied, or the local guard is installed
   if the plan cannot protect a private repository. Then `ENABLE_SCHEDULED=true`, and a manual `e2e-journeys` run.
