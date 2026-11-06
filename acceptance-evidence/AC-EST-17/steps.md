# AC-EST-17 migration legacy JobCode

- Persona: `central-a`
- Correlation ID: `4317ec26-492c-4d16-8f98-1f2595c5c132`
- HTTP observations: `5`

## Observed requests

- `POST /api/hr-core/api/establishment/job-catalog/import?mode=Migration` -> `200`
- `POST /api/hr-core/api/establishment/changes/7e68c345-cc75-428b-ae6c-73280c0ad69d/submit` -> `200`
- `POST /api/hr-core/api/workflow-instances/7ea97d5f-7a5d-43bd-ad88-c6305d567bda/approve` -> `200`
- `POST /api/hr-core/api/workflow-instances/7ea97d5f-7a5d-43bd-ad88-c6305d567bda/approve` -> `200`
- `GET /api/hr-core/api/lookup/job-catalog?q=LEG-001&page=1&pageSize=5` -> `200`
