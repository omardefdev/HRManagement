# EXT-005 — Throttle, tenant, and errors

## Throttling

Default `defaultRpm` = 120, `burst` = 30, `onExceed` = 429. Exceeding quota with correct 429 + correlation → PASS (`throttle-exceed-pass`).

## Tenant

`tenantHeaderRequired` and `crossTenantDeny` = true (joins TEN-003).

## Errors

`errorModel.problemDetailsRequired` = true. `nonRepudiation.correlationIdRequired` = true on every exchange.
