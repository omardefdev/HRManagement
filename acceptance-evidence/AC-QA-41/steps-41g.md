1. scripts/acceptance-evidence-summarize.ps1 -Head <sourceSha> on the evidence of this run.
2. scripts/acceptance-baseline-validate.ps1: every AC has an owner, summary.json agrees with the folders, nothing is
   counted as proven against the rules.
Negative: AcceptanceEvidenceSummaryTests, a local summary in -Committed mode, an AC without an owner and a Pass with an
incomplete contract all fail the validator.