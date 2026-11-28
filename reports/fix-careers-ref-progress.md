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

## F1 — The fix and its tests

- **The gateway** (`GatewayUpstreamCookieForwarding.BuildUpstreamCookieHeader`): the flash cookie's exact name now passes on every
  route, the careers route included; `careers_session` still on the careers route only; every other cookie dropped. The transform is
  unchanged (it still removes `Authorization` on the careers route); its comment says why the flash cookie may cross.
- **The confirmation page** (`PublicCareersController.Submitted`, `Views/PublicCareers/Submitted.cshtml`): the badge holds the
  number read once from the flash; without one, no badge but «لا يمكن عرض الرقم المرجعي مرة أخرى», the number is shown once, the
  agency's recruitment office can help, and — only when `Notifications:Candidate:Provider` is not `None` (read through
  `[FromServices] IOptions<CandidateNotificationOptions>`, so the controller's constructor and its tests are untouched) — a consenting
  candidate also receives the number by message. Both apply paths redirect to the same page with no route value.
- **The competition page:** its own `TempData["Error"]` alert removed (the layout shows the refusal once).
- **Tests:** `GatewayUpstreamCookieForwardingTests` (the pinned careers drop test changed: the flash's exact name left its list, its
  near-names `x.AspNetCore.Mvc.CookieTempDataProvider`, `.AspNetCore.Mvc.CookieTempData`, `…ProviderC1`, lower case joined it; new:
  the careers route keeps the three names, the staff routes forward what they did, the transform carries the flash to HR.Core with
  no `Authorization` from a signed-in staff browser or a client Bearer); `FixCareersRefConfirmationPageTests` (the posting apply
  hands the number through the flash only, never the URL; the page's flag for each provider value); `Hcm031IntakeTests` (the
  competition apply likewise); `Ux067PortalRoutingTests` (HR.Core in process: the page renders the number from a flash cookie the
  gateway filter forwards and ends it, `path=/careers`; without it, no badge and the message; a refusal in the flash shown once).
- Debug build `-warnaserror` 484 s, 0 warnings; Release build `-warnaserror` 484 s, 0 warnings.
- Gateway, careers and competition unit tests 884/884; the portal's in-process tests 12/12; the architecture tests 444/444
  (`InterfaceTextGuardTests` among them); the whole unit suite **7,493/7,493** (653 s; its rewritten `testdata/*/out` restored);
  `dotnet format --verify-no-changes` on the changed files: clean (310 s). Gitleaks (pinned image, `--no-git`) on the changed files:
  no leaks.
- Finished F1 (UTC): 2026-11-28T16:19:00Z

## F2 — Acceptance on the local stacks

- **Images** (`artifacts/fix-careers-ref/tools/build-images-sequential.ps1`, one at a time, local base images): `hr-gateway` from
  `f5cd7209` in 65 s; `hr-core-web` failed once at Docker Hub (`docker/dockerfile:1.4` metadata, TLS handshake timeout — transient),
  then 170 s. Only these two images were rebuilt: the change is in the gateway's filter (`HRManagement.ServiceDefaults`, which every
  image carries but only the gateway's transform runs) and in HR.Core's controller and views; the other 15 images are PLAN-60's
  (`92f0c997`/`46314a05`), as PLAN-60 itself ran. Labels `f5cd7209…-dirty` (the acceptance tests were not yet committed; no image
  embeds them). The old images stay as the `hrm-leastpriv-*` tags.
- **The independent review** (one fresh subagent, the diff against the prompt, read only): nothing blocking. No other cookie or
  credential crosses on the careers route; the staff routes unchanged; the number in no URL, log or evidence name; no test proves the
  page by SQL. Taken: the ADR S2S-042 line that still said the flash stays dropped on the careers route; the stale comment of
  `CandidatePortalController`; a 429 retry in AC-31's form posts (the portal's 30-per-minute window); the closed-competition refusal
  checked on its own page; no badge value in an assertion message (`Assert.True(… is null, msg)` instead of `Assert.Null`); the
  server check counts the visitor's own rows (by its test national id's hash) instead of every tenant's; a tautological assert
  removed; the no-number heading made neutral («لا يوجد رقم مرجعي لعرضه», not «… مرة أخرى» to a visitor who never applied).
  Left: the server check reads the provider from the container's environment only (the demo sets it there or not at all).
- **Lab stack** (`acceptance-up.ps1 -NoBuild -RequireCleanBaseline`, 16:26:25Z → 16:41:59Z, 934 s). **Deviation:** started at a
  one-minute load of 13 on 8 CPUs (the image builds had just ended; nothing heavy beside it); it came up at the first attempt.
  - The reproduction again (`repro-after.txt`, pages decoded): the badge shows a 15-character number; the duplicate's landing page
    and the unknown posting's each show their refusal once. (The F0 script's own greps had missed Arabic text, which Razor
    HTML-encodes; the F0 pages, decoded, show an empty badge and no alert at all — the F0 record stands.)
  - `AcHcm30CandidateExperienceTests` **9/9**, `AcHcm31CompetitionTests` **2/2** (31a with its public apply, duplicate and closed
    refusals; 31b–g 3 min), `AcHcm22RecruitmentWorkflowsUiTests` **5/5** (16:42:43Z → about 16:50Z, one run); the lab journey
    `RecruitmentToHireJourneyE2ETests` **1/1** (12 s). Evidence under `artifacts/fix-careers-ref/runs/`; no committed evidence rewritten.
  - Brought down with its volumes (16:51:10Z).
- `hr-core-web` rebuilt from the tree with the review's heading and comment (191 s); the lab image it replaced removed by id. The unit
  project rebuilt (344 s): the gateway and confirmation-page unit tests 41/41; the portal's in-process tests 7/7.
- **Prod-like stack** (`acceptance-up.ps1 -Profile ProdLike -NoBuild -RequireCleanBaseline`, started 17:04:34Z after waiting 50 s for
  the load to fall under 8, ready 17:19:37Z, 903 s; `hr-core-web` with `NationalScope__IsLabScale=false`):
  `RecruitmentToHireJourneyProdLikeE2ETests` **1/1** (35 s: the number read from the confirmation page, checked against the citizen
  row, then used for the portal sign-in); `AcUx67ReadableInterfaceTests.Ac67d_…` (the portal check) **1/1** (10 s). The reproduction
  on it (`repro-prodlike.txt`): a 15-character number in the badge; the duplicate and unknown-posting refusals shown once each; a
  direct visit shows no badge, says so, and (FileDrop on this stack) promises the message. Brought down with its volumes (17:21:35Z).
- Committed: `810b9e38` (the review's source and document points), `21ffa0fe` (AC-HCM-30, AC-HCM-31, both journeys and
  `CareersConfirmationPage`). Gitleaks on the changed files before them: no leaks.
- Finished F2 (UTC): 2026-11-28T17:24:30Z

## Server changes (with undo)

(none yet)

## Durations

- Lab stack start (F0, no build): 905 s.
- F1: Debug build 484 s; Release build 484 s; unit suite 653 s; format check 310 s.
- F2: images 65 s + 170 s (+ a failed first try at Docker Hub) + 191 s (the review's view change); lab stack 934 s; AC-HCM-30/31/22
  about 8 min; the lab journey 12 s; the review ran 15 min beside the stack; prod-like stack 903 s (+50 s waiting for the load);
  the prod-like journey 35 s; AC-UX-67d 10 s.

## Next step

F3: the Testcontainers step with CI's filter; the local CI steps (with the Release build) in a fresh clone of HEAD; then
`plan035-prepush.ps1`, the push and the PR.
