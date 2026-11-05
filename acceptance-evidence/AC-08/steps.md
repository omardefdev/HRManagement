# AC-08 quarantine reconciliation

- Persona: `platform-admin`
- Correlation ID: `9dcebfb9-25ac-4396-8b77-9ae89a975a8d`
- HTTP observations: `5`

## Observed requests

- `GET /api/tenant-quarantine?resolved=false` -> `200`
- `PATCH /api/employees/a0000001-0000-4000-8000-000000000901` -> `409`
- `POST /api/tenant-quarantine/58cafb7a-c28e-4afb-90c5-51e2c8cad2c5/resolve` -> `200`
- `GET /api/tenant-quarantine?resolved=false` -> `200`
- `PATCH /api/employees/a0000001-0000-4000-8000-000000000901` -> `200`
