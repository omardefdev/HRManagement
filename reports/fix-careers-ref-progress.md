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

## Server changes (with undo)

(none yet)

## Durations

(none yet)

## Next step

F0.2: reproduce the defect on a local lab stack with the gateway.
