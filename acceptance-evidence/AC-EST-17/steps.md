# AC-EST-17 migration legacy JobCode

- Persona: `central-a`
- Correlation ID: `f983079f-74cb-49d3-b815-0fa54b65db5c`
- HTTP observations: `5`

## Observed requests

- `POST /api/hr-core/api/establishment/job-catalog/import?mode=Migration` -> `200`
- `POST /api/hr-core/api/establishment/changes/a8abcaca-a190-4fe9-ab08-3ad0aa93f407/submit` -> `200`
- `POST /api/hr-core/api/workflow-instances/5f88294d-ce14-4ca9-8257-65e3b28a6e17/approve` -> `200`
- `POST /api/hr-core/api/workflow-instances/5f88294d-ce14-4ca9-8257-65e3b28a6e17/approve` -> `200`
- `GET /api/hr-core/api/lookup/job-catalog?q=LEG-001&page=1&pageSize=5` -> `200`
