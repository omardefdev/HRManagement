# AC-12 payroll SoD

- Persona: `payroll-processor`
- Correlation ID: `6d3c95b9-1474-40e4-b65e-72a0e6d6c066`
- HTTP observations: `3`

## Observed requests

- `POST /api/payroll/runs` -> `201`
- `POST /api/payroll/runs/07d17e18-dceb-4c30-989d-5880d61f3d78/approve` -> `403`
- `POST /api/payroll/runs/07d17e18-dceb-4c30-989d-5880d61f3d78/submit-for-review` -> `400`
