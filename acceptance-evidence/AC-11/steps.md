# AC-11 policy workflow + signatures

- Persona: `b-branch-manager`
- Correlation ID: `f416c397-bf28-425d-a370-715b95c5a42f`
- HTTP observations: `9`

## Observed requests

- `POST /api/hr-core/api/policies/enforce` -> `200`
- `GET /api/hr-core/api/workflow-instances/116caba4-8477-4b0f-8982-700baf30a251` -> `200`
- `POST /api/hr-core/api/workflow-instances/116caba4-8477-4b0f-8982-700baf30a251/approve` -> `400`
- `POST /api/hr-core/api/workflow-instances/116caba4-8477-4b0f-8982-700baf30a251/approve` -> `200`
- `POST /api/hr-core/api/workflow-instances/116caba4-8477-4b0f-8982-700baf30a251/approve` -> `400`
- `POST /api/hr-core/api/workflow-instances/116caba4-8477-4b0f-8982-700baf30a251/approve` -> `200`
- `GET /api/hr-core/api/workflow-instances/116caba4-8477-4b0f-8982-700baf30a251/history` -> `200`
- `GET /api/hr-core/api/signatures?subjectType=AssetAssignment` -> `200`
- `GET /api/hr-core/api/policies/ledger` -> `200`
