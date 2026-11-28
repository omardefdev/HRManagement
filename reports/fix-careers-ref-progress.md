Started (UTC): 2026-11-28T15:26:09Z

# FIX-CAREERS-REF — progress

Prompt: `docs/acceptance-plans/execution/FIX-CAREERS-REF-AGENT-PROMPT.md`. Branch `fix/careers-reference-number` from
`origin/main` (`753c1ef3`), in the main folder `/home/omar/HRManagement`.

## F0 — Preconditions and the proof of the cause

- No other session running (`list_sessions`: every session `isRunning: false`; PLAN-60's PR #61 merged).
- `git fetch origin`: `origin/main` = `753c1ef3` (PLAN-60's merge). Tree clean; `global.json` tracked and unchanged,
  no `tmp-vendors.html`. The merge-note branch `docs/plan-60-merge-note` (`4c96ae42`) left untouched (F-1).
- `ssh -o BatchMode=yes hrm-vps whoami` → `hrm`; host key ED25519 `SHA256:qWIfQls882KM/nQ99yV/WFU2Wk08t6DIb8o+pUMGsto` (matches).
- `gh auth status`: `omardefdev` active, scopes `repo`, `workflow`.
- Keep-awake: `systemd-inhibit --what=idle:sleep --who=fix-careers-ref … sleep 86400`, PID 258948 (stop: `kill 258948`).
- Local disk 22 GB free; 15 GB RAM; 8 CPUs, load 2.3.

- **Lab stack (F0)** from the local images of PLAN-60's tree (`acceptance-up.ps1 -NoBuild -RequireCleanBaseline`; the gateway
  filter is unchanged since PLAN-42's `4cb066f8`, so these images carry the defect): 15:31:18Z → 15:46:23Z (905 s; the load reached
  22 on 8 CPUs during its start, Keycloak's user sync took about 9 min of it). Brought down with its volumes (`acceptance-reset.ps1`,
  7 s); no committed evidence rewritten.
- **Reproduced through the gateway** (`curl` with a cookie jar, as a browser; values never printed; output in
  `artifacts/fix-careers-ref/repro-before.txt`):
  - `POST /careers/apply` on the seeded posting `…0513` → `302 Location: /careers/submitted` with
    `Set-Cookie: .AspNetCore.Mvc.CookieTempDataProvider=…; path=/careers; samesite=lax; httponly` — the cookie is written, reaches the
    browser, and its path shows HR.Core's path base on the careers route is `/careers`;
  - `GET /careers/submitted`: the browser sends `.AspNetCore.Antiforgery.HRM-HR-Core-Web` and `.AspNetCore.Mvc.CookieTempDataProvider`;
    the page's badge holds **0 characters**;
  - what HR.Core receives: HR.Core answers 403 to a direct call (it requires the gateway's proof), so the gateway's own filter, as
    built from `main` (`HRManagement.ServiceDefaults.dll` of the PLAN-60 build), was run on those two names: careers route →
    `.AspNetCore.Antiforgery.HRM-HR-Core-Web=<a>` only; a staff route → both. **The cause is the one the prompt names.**
  - the duplicate apply → `302 /careers/{posting}` with a new flash cookie; the page shows no refusal. An unknown posting →
    `302 /careers/index`; no refusal shown.
- **The demo, read-only** (`artifacts/fix-careers-ref/demo-before.txt`): a token from `/careers/track`, an apply to an unknown posting
  id (refused before any write) → `302 /careers/index` with the flash cookie (`path=/careers`); the landing page shows no
  «الإعلان غير متاح للتقديم»; a direct visit to `/careers/submitted` shows the empty badge.
- One more effect found while reading: `Views/PublicCareers/Competition.cshtml` prints `TempData["Error"]` itself and its layout
  `_PublicLayout` prints it too, so once the flash crosses, a competition refusal would show twice. F1 removes the view's copy.
- Finished F0 (UTC): 2026-11-28T15:49:30Z

## Server changes (with undo)

(none yet)

## Durations

- Lab stack start (F0, no build): 905 s.

## Next step

F1: the fix (written in the tree, not yet committed), then the builds and the unit, integration and guard tests.
