Started (UTC): 2026-11-01T10:24:39.9524811Z

# PLAN-38 progress

## P0 — in progress

Preconditions (checked 2026-11-01T10:25Z): PR #32 merged (`d1642d41`), post-merge `ci` 41296399636 success (dedupe
skipped the heavy jobs); `main` = `origin/main` + `ecca3657`; tree clean except `global.json` (owner), `tmp-vendors.html`,
this prompt; no other plan session; `ssh hrm-vps whoami` = `hrm`, ED25519 key `SHA256:qWIfQls882KM/nQ99yV/WFU2Wk08t6DIb8o+pUMGsto`.

- Branch `feature/plan-38-ops-first-boot-data-broker-secrets`; prompt committed (`1d5b0059`).
- Baseline: build 0/0 (21 s); unit filters Sec004|Ten002|DB001 22/22; seed inventory and broker inventory (two read-only
  subagents) in `reports/plan38-baseline.md`; live topology on `hrm-acceptance` after the AC-HCM-33 journey (Pass, 20 s):
  200 exchanges, 122 queues, 0 unclassified → `reports/plan38-rabbit-topology.json`.
- Local: `docker builder prune -f` freed 18 GB (P-3); `hrm-acceptance` stack started (it was stopped in PLAN-37).
- Server before-picture (read-only): in the baseline.

Finished (UTC): 2026-11-01T10:45:00Z

## Decisions (notes)

- Gateway has no bus: `mq_gateway` gets no broker user (the production file's `RabbitMQ__Username: mq_gateway` is
  unused and is removed rather than given a password).
- Broker `configure` per service: `^(<prefix>-.*|HRManagement\.MessageContracts\.(Events|Commands):.*|MassTransit:Fault.*|.*_bus_.*)$`
  (+ `HR\.Contracts\.Infrastructure\.Sagas:.*` for HR.Contracts), to be proven on Testcontainers with the new image (P2).
