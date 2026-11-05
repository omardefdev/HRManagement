# AC-12 payroll SoD

- Persona: `payroll-processor`
- Correlation ID: `a004d2cf-5a3c-4b50-aae9-62d975a88ee2`
- HTTP observations: `3`

## Observed requests

- `POST /api/payroll/runs` -> `201`
- `POST /api/payroll/runs/afd33644-b423-44e6-87cc-4f247238c4db/approve` -> `403`
- `POST /api/payroll/runs/afd33644-b423-44e6-87cc-4f247238c4db/submit-for-review` -> `400`
