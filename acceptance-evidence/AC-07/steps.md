# AC-07 agency onboarding idempotent

- Persona: `platform-admin`
- Correlation ID: `e989a12f-1d27-415d-841b-f16200d65441`
- HTTP observations: `3`

## Observed requests

- `POST api/hr-core/api/onboarding/agencies/apply` -> `200`
- `POST api/hr-core/api/onboarding/agencies/apply` -> `200`
- `POST api/hr-core/api/onboarding/agencies/apply` -> `200`
