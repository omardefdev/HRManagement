1. SQL: an executed promotion with its execution record and a completed custody workflow with a pending assignment, none with a ledger row (the state a lost event left).
2. scripts/msg040-replay-lost-events.ps1 -DryRun lists both; nothing changes.
3. -Apply on this acceptance database: the note reaches HR.Core and the assignment becomes Active; the run is audited.
4. Negative: the second dry run lists zero replayable events.