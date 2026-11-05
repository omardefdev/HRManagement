# AC-11 policy workflow + signatures

- Persona: `b-branch-manager`
- Correlation ID: `8d4cf58d-a16b-4c69-995f-2614bb2ed4e4`
- HTTP observations: `9`

## Observed requests

- `POST /api/hr-core/api/policies/enforce` -> `200`
- `GET /api/hr-core/api/workflow-instances/ea1f3e30-29f9-4bb0-b2ed-f8bc0e040c04` -> `200`
- `POST /api/hr-core/api/workflow-instances/ea1f3e30-29f9-4bb0-b2ed-f8bc0e040c04/approve` -> `400`
- `POST /api/hr-core/api/workflow-instances/ea1f3e30-29f9-4bb0-b2ed-f8bc0e040c04/approve` -> `200`
- `POST /api/hr-core/api/workflow-instances/ea1f3e30-29f9-4bb0-b2ed-f8bc0e040c04/approve` -> `400`
- `POST /api/hr-core/api/workflow-instances/ea1f3e30-29f9-4bb0-b2ed-f8bc0e040c04/approve` -> `200`
- `GET /api/hr-core/api/workflow-instances/ea1f3e30-29f9-4bb0-b2ed-f8bc0e040c04/history` -> `200`
- `GET /api/hr-core/api/signatures?subjectType=AssetAssignment` -> `200`
- `GET /api/hr-core/api/policies/ledger` -> `200`
