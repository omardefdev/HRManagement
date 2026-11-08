# PROD-001 — Effective dating

Historical evaluations resolve the pinned version for `asOf`. Drift fails validation.

## PLAN-47: effective dating of policy versions

- **A version can take effect later.** The author sets the draft's `EffectiveFrom` (UTC; `PUT api/policies/versions/{id}/effective-from`
  or the page). When it is published, the version in force stays in force until then: only its `EffectiveTo` is set.
- **No retroactive effect (D-POL-10).** An `EffectiveFrom` earlier than now − 5 minutes is refused («النفاذ بأثر رجعي غير مسموح»),
  when it is set and again at publish. `PolicyEngine:Publish:AllowRetroactive=true` opens it.
- **Evaluation as of a date.** `PolicyEnforceRequest.AsOfUtc` is honoured by `EvaluateAsync` and `EnforceAsync` in HR.Core,
  by `POST api/policies/evaluate` and by the simulation page. The ledger records `EvaluatedAsOf`. PLAN-42's internal endpoint
  (`api/internal/policies/enforce`) clears it: a service is decided on the policy in force now.
- **The effective window** is `[EffectiveFrom, EffectiveTo)`; the highest version number wins inside it.
- **The cache** holds only "now": an entry lives 5 minutes, or until the next `EffectiveFrom` of the domain's templates if that
  is sooner. An as-of evaluation reads the database. Every publication clears the domain in every HR.Core instance
  (`PolicyPublishedCacheInvalidationConsumer` on a temporary queue per instance).
- **Proof:** `Pol047EffectiveDatingTests`, AC-POL-47c.
