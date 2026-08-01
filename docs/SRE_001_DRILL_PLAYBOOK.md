# SRE-001 — Drill playbook

## Drill A — Unannounced P1 page

1. Fire a synthetic P1 alert to the current Tier-0.
2. Measure time-to-ack and time-to-IC.
3. Pass if ack ≤ 5m and IC ≤ 10m.

## Drill B — Roster / access audit

1. Call/SMS every person on the current week roster.
2. Verify VPN + Grafana login for each Tier-0/1.
3. Fail if any slot unreachable or without access.

## Drill C — Handover exercise

1. Simulate shift change with an open P2.
2. Incoming shift restates status and next action in ≤ 15 minutes.

## Drill D — Service-down using pilot runbook

1. Pick a runbook under `docs/runbooks/`.
2. Shadow on-call executes without author present.
3. Record gaps → SRE-002 ORR backlog.

## Log

Record results in [SRE_001_VERIFICATION.md](./SRE_001_VERIFICATION.md).
