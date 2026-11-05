# AC-EST-17 migration legacy JobCode

- Persona: `central-a`
- Correlation ID: `5841f601-7a8e-4460-8040-fbefeaf0e6c1`
- HTTP observations: `5`

## Observed requests

- `POST /api/hr-core/api/establishment/job-catalog/import?mode=Migration` -> `200`
- `POST /api/hr-core/api/establishment/changes/ea37689e-e70d-4c42-9633-b6241d83ec3e/submit` -> `200`
- `POST /api/hr-core/api/workflow-instances/e80c3907-e5c0-4283-ab69-877c1ac1ad06/approve` -> `200`
- `POST /api/hr-core/api/workflow-instances/e80c3907-e5c0-4283-ab69-877c1ac1ad06/approve` -> `200`
- `GET /api/hr-core/api/lookup/job-catalog?q=LEG-001&page=1&pageSize=5` -> `200`
