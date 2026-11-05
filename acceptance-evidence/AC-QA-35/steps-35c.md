1. git fetch origin main; origin/main must equal local main (a CI checkout has no local main).
2. gh run list --workflow ci.yml --branch main --event push: the run whose headSha is origin/main; inside the job,
   the newest finished push run.
3. Every job of that run concluded success, or was skipped because the dedupe guard proved the pull request had
   tested the same tree (PLAN-37).