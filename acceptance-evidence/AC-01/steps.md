## Preconditions
- PLAN-00 acceptance seed applied (AGENCY-A + AGENCY-B).
- Persona `acc-central-a` with tenant AGENCY-A.

## Steps
1. Obtain JWT via password grant.
2. GET /api/hr-core/api/lookup/employees?q=فرع
3. Assert Tenant-A employees present; Tenant-B employee absent.
4. GET Tenant-B employee by id => 404/403.