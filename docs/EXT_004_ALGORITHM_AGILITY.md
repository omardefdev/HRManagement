# EXT-004 — Algorithm agility

| Set | Algorithms |
|---|---|
| Allowed | RSA-PSS-SHA256, ECDSA-P256-SHA256 |
| Deprecated | MD5, SHA1, RSA-PKCS1-SHA1 |

Signing or accepting a deprecated algorithm as valid → FAIL (`deprecated-alg-fail`).

New algorithms require matrix/policy version bump and dual-role signing (see matrix `signing` roles).
