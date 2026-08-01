# MIG-003 — Checkpoint / resume

| Rule | Value |
|---|---|
| store | pipeline-run-ledger |
| resumeFromStage | true |
| midFailBehavior | resume-next-unfinished-stage |

Stages extract→validate→transform→quarantine→load are checkpointable. On mid-fail, resume continues from the last Checkpointed stage without re-extracting completed work. Fixture `mid-stage-fail-resume.json` must PASS.
