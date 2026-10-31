1. powershell -File scripts/scm001-branch-protection-validate.ps1 -CheckLiveGitHub: the live protection equals the
   file, or (private repository on the free plan, 403 "Upgrade to GitHub Pro") the open exception
   SCM-NO-GITHUB-PROTECTION plus the installed pre-push guard with -ProtectMain.
2. Negative: the guard refuses a push of HEAD to refs/heads/main (exit 1).
3. Negative: PR from plan35-negative-check with a deliberately red unit test: its CI check failed, the PR was closed
   without merge (mergeStateStatus recorded; BLOCKED only where GitHub enforces protection).