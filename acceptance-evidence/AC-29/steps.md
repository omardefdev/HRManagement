# AC-29 file access grant expiry (SEC-003)

- Persona: `a-file-custodian`
- Correlation ID: `af61175e-9c0a-4ed1-a986-674146e7a408`
- HTTP observations: `3`

## Observed requests

- `POST /api/files` -> `201`
- `GET /api/files/be1b5825-4548-4be3-82da-f5f87e7e67da/presigned-url` -> `200`
- `GET /api/files/be1b5825-4548-4be3-82da-f5f87e7e67da/presigned-url` -> `404`
