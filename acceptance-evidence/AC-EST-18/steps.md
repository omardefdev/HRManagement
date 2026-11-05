# AC-EST-18d deactivate impact

- Persona: `central-a`
- Correlation ID: `07c7c17e-051c-436c-9dd1-5ba41a94be65`
- HTTP observations: `4`

## Observed requests

- `GET /api/hr-core/api/reference-data/by-code?kind=Grade&code=G5` -> `200`
- `GET /api/hr-core/api/reference-data/c983e68e-3900-4d55-8875-f8d98f4c5c49/impact` -> `200`
- `GET /ui/hr-core/ReferenceCatalog/Details/c983e68e-3900-4d55-8875-f8d98f4c5c49` -> `200`
- `POST /ui/hr-core/ReferenceCatalog/Deactivate/c983e68e-3900-4d55-8875-f8d98f4c5c49` -> `302`
