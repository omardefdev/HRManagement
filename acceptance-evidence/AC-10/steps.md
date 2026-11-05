# AC-10 policy enforce

- Persona: `central-a`
- Correlation ID: `8cda606e-6300-4c48-bf97-9d640ae1fa03`
- HTTP observations: `5`

## Observed requests

- `POST /api/hr-core/api/policies/templates/a0000001-0000-4000-8000-000000000304/publish` -> `200`
- `POST /api/hr-core/api/policies/templates/a0000001-0000-4000-8000-000000000303/publish` -> `400`
- `GET /api/hr-core/api/policies/snapshots/Assets.Custody` -> `200`
- `POST /api/hr-core/api/policies/enforce` -> `422`
- `GET /api/hr-core/api/policies/ledger?domain=Assets.Custody` -> `200`
