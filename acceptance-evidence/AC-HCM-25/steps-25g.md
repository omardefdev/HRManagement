Live facts on the running stack (patched on start-up by WorkflowPackSeeder.RetireAsync and
PolicyEngineDomainSeeds.DeactivateRuleIfPresentAsync):
1. R-REC-HIRE has no effect («[]») and its name is marked «[معطّلة]»; R-REC-ORG and R-REC-HIRE-OFFER remain.
2. RECRUITMENT-HIRE is unpublished with ValidTo set.
3. No RecruitmentApplication workflow instance is in progress (hires on this stack — AC-HCM-22a/30/33c — started none).
The Enforce-mode hire itself (fresh DB, pre-PLAN-25 DB with rule + published definition + binding, static fallback)
is proven by Hcm025RecruitmentHireRetiredTests (unit) — Enforce is not the deployed mode (Production = Shadow).