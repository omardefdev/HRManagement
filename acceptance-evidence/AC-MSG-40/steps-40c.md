1. SQL: draft policy versions with a recorded maker.
2. POST policies/templates/{id}/publish (API) and PolicyTemplates/PublishFromUi (UI, which published nothing before PLAN-40).
3. idcard_db: one receipt for each deterministic MessageId within 60 s.
4. Negative: a version with no maker is refused by four-eyes (400): no receipt.