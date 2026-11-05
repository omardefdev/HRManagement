# PLAN-42: the S2S trust boundary

Remote policy checks now fail closed, and only the owning service can reach the internal enforce and code endpoints. Details, deviations and open items are in [reports/plan42-final-report.md](plan42-final-report.md).

## What changed

- **Enforcement:** a denial or an outage in Enforce stays a refusal, and Shadow never changes the caller's result.
- **Internal endpoints:** the server derives mode, actor and tenant. The user `enforce` route is gone, and every `api/internal/*` action is service-only, with a guard.
- **Gateway proof v2:** an ECDSA P-256 signature only the gateway can make, tied to the token and usable once. Services stop signing.
- **Secrets:** one per Deployment, and a placeholder stops the boot.
- **Scopes:** `dsar.subject.erase` is separate from read. Official numbers are issued by the owner, in the request's tenant.
- **Legal hold:** the gate is authenticated and fails closed, at creation and publication.
- **Parallel plans:** `scripts/parallel/hrm-lock.ps1` and `docs/PARALLEL_PLANS.md`. Two acceptance stacks do not fit on the owner's machine, so plans take turns.

## Verified

- Local CI on 0908829e: 91 steps, 0 failed. Pre-push green.
- Prod-like: AC-S2S-42a..i 9 of 9, and the PLAN-40 and PLAN-41 groups. Lab: AC-10, AC-11 and AC-13 passed; AC-09 is Blocked by design.
- Demo server on `a7ad95e0a4fa`: checks 1 to 8 green, including a direct call with a v1 proof refused with 403.

## For the reviewer

- `Ac40f` got one connection reset on the merged code and passed on a clean rerun. The cause is not known.
- M3 stays open: `WorkflowInstancesController` accepts any `hr-core.workflow` token. It predates this plan; PLAN-43 and PLAN-51 close it.
- No evidence is committed. This PR run's artifact is the evidence of record.
- The commits after the deployed tag change no server behaviour.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
