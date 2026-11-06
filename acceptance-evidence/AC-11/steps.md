# AC-11 policy workflow + signatures

- Persona: `b-branch-manager`
- Correlation ID: `ee02ab80-d302-45c6-bfd3-1828c5e92885`
- HTTP observations: `10`

## Observed requests

- `POST /api/internal/policies/enforce` -> `200`
- `POST /api/workflow-instances` -> `201`
- `GET /api/hr-core/api/workflow-instances/98d00889-b9ef-4ee8-b4c3-1003a05f7112` -> `200`
- `POST /api/hr-core/api/workflow-instances/98d00889-b9ef-4ee8-b4c3-1003a05f7112/approve` -> `400`
- `POST /api/hr-core/api/workflow-instances/98d00889-b9ef-4ee8-b4c3-1003a05f7112/approve` -> `200`
- `POST /api/hr-core/api/workflow-instances/98d00889-b9ef-4ee8-b4c3-1003a05f7112/approve` -> `400`
- `POST /api/hr-core/api/workflow-instances/98d00889-b9ef-4ee8-b4c3-1003a05f7112/approve` -> `200`
- `GET /api/hr-core/api/workflow-instances/98d00889-b9ef-4ee8-b4c3-1003a05f7112/history` -> `200`
- `GET /api/hr-core/api/signatures?subjectType=AssetAssignment` -> `200`
- `GET /api/hr-core/api/policies/ledger` -> `200`
