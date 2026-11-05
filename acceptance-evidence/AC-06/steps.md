# AC-06 transfer access flip pattern

- Persona: `br1-mgr+br2-mgr`
- Correlation ID: `f71bea27-8a40-441d-aa90-f89111325a27`
- HTTP observations: `4`

## Observed requests

- `GET /api/hr-core/api/lookup/employees/a0000001-0000-4000-8000-000000000201` -> `200`
- `GET /api/hr-core/api/lookup/employees/a0000001-0000-4000-8000-000000000203` -> `403`
- `GET /api/hr-core/api/lookup/employees/a0000001-0000-4000-8000-000000000203` -> `200`
- `GET /api/hr-core/api/lookup/employees/a0000001-0000-4000-8000-000000000201` -> `403`
