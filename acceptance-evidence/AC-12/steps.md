# AC-12 payroll SoD

- Persona: `payroll-processor`
- Correlation ID: `4d3ed2a9-24e5-4d85-937a-1b4535a847f8`
- HTTP observations: `3`

## Observed requests

- `POST /api/payroll/runs` -> `201`
- `POST /api/payroll/runs/7d399dc9-81b5-41b1-919a-c666e762f455/approve` -> `403`
- `POST /api/payroll/runs/7d399dc9-81b5-41b1-919a-c666e762f455/submit-for-review` -> `400`
