# AC-11 policy workflow + signatures

- Persona: `b-branch-manager`
- Correlation ID: `9f36048a-0c35-45c4-a9e1-5fb78cb23fb5`
- HTTP observations: `9`

## Observed requests

- `POST /api/hr-core/api/policies/enforce` -> `200`
- `GET /api/hr-core/api/workflow-instances/1fe5b9b4-a7b4-4dd8-a9f4-c851274594a8` -> `200`
- `POST /api/hr-core/api/workflow-instances/1fe5b9b4-a7b4-4dd8-a9f4-c851274594a8/approve` -> `400`
- `POST /api/hr-core/api/workflow-instances/1fe5b9b4-a7b4-4dd8-a9f4-c851274594a8/approve` -> `200`
- `POST /api/hr-core/api/workflow-instances/1fe5b9b4-a7b4-4dd8-a9f4-c851274594a8/approve` -> `400`
- `POST /api/hr-core/api/workflow-instances/1fe5b9b4-a7b4-4dd8-a9f4-c851274594a8/approve` -> `200`
- `GET /api/hr-core/api/workflow-instances/1fe5b9b4-a7b4-4dd8-a9f4-c851274594a8/history` -> `200`
- `GET /api/hr-core/api/signatures?subjectType=AssetAssignment` -> `200`
- `GET /api/hr-core/api/policies/ledger` -> `200`
