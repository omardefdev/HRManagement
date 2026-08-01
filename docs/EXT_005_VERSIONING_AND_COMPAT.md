# EXT-005 — Versioning and compatibility

## Semver

`versioning.semverRequired` = true. Breaking changes require a major bump (`breakingRequiresMajorBump`).

Breaking change without major bump → FAIL (`breaking-without-bump-fail`).

## N-1 support

`compatibility.supportNMinus1` = true and `measureBackwardCompat` = true. Consumers on previous minor/major-1 must pass lab compat → PASS (`n1-compat-pass`).

Certified consumers are protected under consumer-driven contracts (see CDC journey).
