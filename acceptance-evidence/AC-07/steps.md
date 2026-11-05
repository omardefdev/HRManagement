# AC-07 agency onboarding idempotent

- Persona: `platform-admin`
- Correlation ID: `ac1870b1-27c2-4307-b894-458967d8be5e`
- HTTP observations: `3`

## Observed requests

- `POST api/hr-core/api/onboarding/agencies/apply` -> `200`
- `POST api/hr-core/api/onboarding/agencies/apply` -> `200`
- `POST api/hr-core/api/onboarding/agencies/apply` -> `200`
