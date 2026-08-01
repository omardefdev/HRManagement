# ADR-EXT-004 — PKI/signature production binding (supersedes pack-only closure)

| Field | Value |
|---|---|
| Status | Accepted |
| Card | EXT-004 (card 76) |
| Date | 2026-07-30 |
| Supersedes | PKI/signature contract gate pack as insufficient for production |
| Dependencies | STR-002 Satisfied; OBS-003 Satisfied; SOV-003 Satisfied (key-sovereignty binding; live HSM = SOV-003a) |

## Context

The prior pack published the PKI/signature matrix, signer-intent/LTV/revocation/HSM contracts, and a shape validate harness, but hosts did not load the matrix at startup and CI did not gate Staging/Production `ExtPki` binding.

## Decision

| Topic | Choice |
|---|---|
| Source of truth | `docs/ext-pki/pki-signature-matrix.json` |
| Runtime | `ExtPkiOptions` + `IExtPkiProvider` + `IPkiSignatureClaimGuard` |
| Signer | intent + role required |
| Form signatures | not legal evidence |
| Seals | crypto seal required; metadata-only insufficient |
| Cert / revocation | fail-closed; CRL + OCSP |
| LTV | required after expiry + archive evidence |
| HSM | private key never leaves HSM; lab mock allowed |
| Clock | maxSkewSeconds > 0; fail-closed |
| Algorithms | allowlist + deprecations |
| Live PKI | Default `LivePkiClaim=false`; forbidden until EXT-004a–f |
| Soft blockers | STR-002/OBS-003 Satisfied; SOV-003 Satisfied (key-sovereignty binding); national-pki-live / hsm-prod / tsa-live / SYD-011 Blocked |
| Closure | **Done (production pki-signature binding)** |

## Consequences

- Staging/Production fail startup on incomplete PKI policy, dishonest blockers, or live PKI claim.
- CI runs `ext004-pki-signature-runtime-validate.ps1`.
- Live CA/HSM/TSA, SIG-001 UI, and EXT-003e treasury signature bind remain out of scope.
- Treasury matrix keeps `EXT-004=NotStarted` until EXT-003e (do not flip in this binding).
