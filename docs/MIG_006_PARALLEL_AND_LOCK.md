# MIG-006 — Parallel run and user lock

| Control | Required |
|---|---|
| parallel read | true |
| parallel run | true |
| userLock before cutover | true |

Parallel read/run validates coexistence before Go. User lock prevents conflicting writes at cut moment. Lab stubs only — no live agency lock tooling in this pack.
