# EXT-002 — Match and mismatch

| Outcome | Behavior |
|---|---|
| Match | Proceed with audited result |
| Mismatch | Human review queue `civil-registry-mismatch-review` |
| Unavailable | See unavailable mode |
| Error | Retry/backoff; no silent truth |

Mismatch without human workflow → policy violation. Fixture `mismatch-review-pass` proves routed review.
