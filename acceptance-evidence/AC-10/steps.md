# AC-10 policy enforce

- Persona: `central-a`
- Correlation ID: `ab961967-9d46-4fe4-b31b-d22a905f4d6a`
- HTTP observations: `5`

## Observed requests

- `POST /api/hr-core/api/policies/templates/a0000001-0000-4000-8000-000000000304/publish` -> `200`
- `POST /api/hr-core/api/policies/templates/a0000001-0000-4000-8000-000000000303/publish` -> `400`
- `GET /api/hr-core/api/policies/snapshots/Assets.Custody` -> `200`
- `POST /api/internal/policies/enforce` -> `200`
- `GET /api/hr-core/api/policies/ledger?domain=Assets.Custody` -> `200`
