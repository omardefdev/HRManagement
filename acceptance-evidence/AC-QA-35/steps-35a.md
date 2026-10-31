1. powershell -File scripts/plan035-prepush.ps1 -Only gitleaks,actionlint,forbidden,nonprod,signatures on HEAD → exit 0.
2. The ci-local summary (artifacts/ci-local/summary.json) passes the cilocal check for its own commit, and that commit
   is an ancestor of HEAD (the pushed release head).
3. Negative: temp repository with a random ghp_ token committed → exit 1, the finding names deploy.env.sample.
4. Negative: temp repository whose range sets global.json rollForward latestPatch → exit 1 naming global.json.