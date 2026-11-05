# AC-EST-18d deactivate impact

- Persona: `central-a`
- Correlation ID: `c5247a2c-0a1d-46ee-84a0-c94fd4f411af`
- HTTP observations: `4`

## Observed requests

- `GET /api/hr-core/api/reference-data/by-code?kind=Grade&code=G5` -> `200`
- `GET /api/hr-core/api/reference-data/656e15b6-f18e-4839-be78-430be182df1f/impact` -> `200`
- `GET /ui/hr-core/ReferenceCatalog/Details/656e15b6-f18e-4839-be78-430be182df1f` -> `200`
- `POST /ui/hr-core/ReferenceCatalog/Deactivate/656e15b6-f18e-4839-be78-430be182df1f` -> `302`
