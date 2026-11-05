# AC-09 policy shadow

- Persona: `central-a`
- Correlation ID: `8103f317-2528-4b5b-9e7b-839811d662f2`
- HTTP observations: `3`

## Observed requests

- `POST /api/hr-core/api/policies/templates/a0000001-0000-4000-8000-000000000303/simulate` -> `200`
- `POST /api/hr-core/api/policies/enforce` -> `200`
- `GET /api/hr-core/api/policies/ledger?domain=Assets.Custody` -> `200`
