# AC-29 file access grant expiry (SEC-003)

- Persona: `a-file-custodian`
- Correlation ID: `44166043-9af4-4c84-b9ff-959de01f756a`
- HTTP observations: `3`

## Observed requests

- `POST /api/files` -> `201`
- `GET /api/files/b37755d1-88d9-47de-8a6a-e979452bd027/presigned-url` -> `200`
- `GET /api/files/b37755d1-88d9-47de-8a6a-e979452bd027/presigned-url` -> `404`
