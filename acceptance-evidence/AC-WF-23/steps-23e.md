For br1-mgr: GET ui/hr-core/api/stats/pending-approvals → {count, legacyCount, workflowCount}, then the inbox.
Expected: count == workflowCount + legacyCount == number of item rows == the «معلّق» badge, and count ≥ 3
(two seeded workflow items and the legacy request are actionable for br1-mgr).