# AC-29 file access grant expiry (SEC-003)

- Persona: `a-file-custodian`
- Correlation ID: `a37e663e-08bf-4d2e-b4b3-b8c195b40c62`
- HTTP observations: `3`

## Observed requests

- `POST /api/files` -> `201`
- `GET /api/files/83868c3a-a42a-41e1-9ce6-131bf7dd4115/presigned-url` -> `200`
- `GET /api/files/83868c3a-a42a-41e1-9ce6-131bf7dd4115/presigned-url` -> `404`
