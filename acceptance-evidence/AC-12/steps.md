# AC-12 payroll SoD

- Persona: `payroll-processor`
- Correlation ID: `ad0996cf-c9e4-431f-a10f-f44053e7c9ee`
- HTTP observations: `3`

## Observed requests

- `POST /api/payroll/runs` -> `201`
- `POST /api/payroll/runs/6635979f-0138-41fd-9e7f-be838ec49f3c/approve` -> `403`
- `POST /api/payroll/runs/6635979f-0138-41fd-9e7f-be838ec49f3c/submit-for-review` -> `400`
