# MIG-004 — National ID exceptions

| Code | Route | Auto-merge |
|---|---|---|
| missing | ExceptionQueue | Forbidden |
| invalid-checksum | ExceptionQueue | Forbidden |
| twin-shared | HumanReview | Forbidden |
| foreign-id | HumanReview | Forbidden |

Twins sharing a national ID (or apparent shared ID) must never auto-merge; evidence and dual control apply via human review.

Foreign IDs require HumanReview with documented identity proofing — not silent Map to Domestic NationalId.
