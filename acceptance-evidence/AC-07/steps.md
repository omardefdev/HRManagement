# AC-07 agency onboarding idempotent

- Persona: `platform-admin`
- Correlation ID: `853acf2c-b472-424c-8140-d4e0ff3c0c9c`
- HTTP observations: `3`

## Observed requests

- `POST api/hr-core/api/onboarding/agencies/apply` -> `200`
- `POST api/hr-core/api/onboarding/agencies/apply` -> `200`
- `POST api/hr-core/api/onboarding/agencies/apply` -> `200`
