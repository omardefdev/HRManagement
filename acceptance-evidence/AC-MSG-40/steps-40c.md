1. SQL: draft policy versions with a recorded maker.
2. POST policies/templates/{id}/publish (API) and PolicyTemplates/PublishFromUi (UI, which published nothing before PLAN-40).
3. hr_core_db: the OutboxMessage sequence moved for each publication, and no row is left for its deterministic MessageId within 60 s (PLAN-42: no consumer until PLAN-47).
4. Negative: a version with no maker is refused by four-eyes (400): the sequence does not move.