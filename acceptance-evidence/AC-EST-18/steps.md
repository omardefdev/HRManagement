# AC-EST-18d deactivate impact

- Persona: `central-a`
- Correlation ID: `c04f7610-8a82-43c9-b72c-5afdb01d8518`
- HTTP observations: `4`

## Observed requests

- `GET /api/hr-core/api/reference-data/by-code?kind=Grade&code=G5` -> `200`
- `GET /api/hr-core/api/reference-data/adebb39b-0e64-4026-9f78-8dd0bc928b7b/impact` -> `200`
- `GET /ui/hr-core/ReferenceCatalog/Details/adebb39b-0e64-4026-9f78-8dd0bc928b7b` -> `200`
- `POST /ui/hr-core/ReferenceCatalog/Deactivate/adebb39b-0e64-4026-9f78-8dd0bc928b7b` -> `302`
