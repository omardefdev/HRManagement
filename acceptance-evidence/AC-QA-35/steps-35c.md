1. git fetch origin main; origin/main must equal local main.
2. gh run list --workflow ci.yml --branch main --event push: the run whose headSha is origin/main.
3. Every job of that run concluded success, except the Acceptance job, skipped while ENABLE_ACCEPTANCE_JOB is unset
   (obligation CI-ACCEPTANCE-DISABLED).