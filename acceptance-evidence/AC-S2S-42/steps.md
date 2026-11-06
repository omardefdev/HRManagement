# svc-compliance erases with dsar.subject.erase; dsar.subject.read alone is refused

- Persona: `svc-compliance`
- Correlation ID: `3516f4a3-0d64-450f-aeb2-12f6df351560`
- HTTP observations: `2`

## Observed requests

- `POST /api/internal/dsar-subjects/candidates/{id}/anonymize` -> `403`
- `POST /api/internal/dsar-subjects/candidates/{id}/anonymize` -> `200`
