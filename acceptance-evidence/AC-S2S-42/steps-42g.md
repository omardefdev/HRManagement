1. 120 parallel numbers for svc-admin-logistics on the internal route: 120 unique, each with IssuedByClientId.
2. Negative: central-a: api/codes/generate does not exist, the internal route is 403, the counter does not move (psql).
3. Negative: AdministrativeDecision for svc-admin-logistics: 403. TenantId of agency B in the body: the number is in agency A and B's counter stands still.