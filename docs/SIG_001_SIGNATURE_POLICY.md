# SIG-001 — Legal Digital Signature Policy

## Purpose

Define the rules governing when, how, and by whom legally binding digital signatures are applied within the HR Management platform.

## Scope

All HR decisions, contracts, and documents that carry legal weight under national employment and administrative law.

## Signing Requirements

| Subject Type | Min Signers | Organizational Seal | Notes |
|---|---|---|---|
| Contract | 2 | Yes | Both parties must sign; employer seal required |
| Decision | 1 | Yes | Issuing authority signs + organizational seal |
| Payroll | 2 | No | Maker + checker; seal not required for internal |
| Promotion | 1 | Yes | Decision authority + organizational seal |
| Transfer | 1 | Yes | Decision authority + organizational seal |

## Signature Properties

Every `SignatureRecord` captures:

- **Identity**: `SignerSubject` from authenticated identity claim (not form input)
- **Role**: `SignerRole` at the moment of signing
- **Intent**: Legal intent expressed (approve, witness, seal)
- **Timestamp**: `OccurredAtUtc` from server clock
- **Certificate**: `CertificateThumbprint` of the X.509 certificate used
- **Payload Hash**: SHA-256 of the canonical (deterministic, key-sorted JSON) payload
- **Signature**: Base64-encoded PKCS#1 signature over the payload hash
- **Algorithm**: Signature algorithm identifier
- **LTV Status**: Long-Term Validation status for certificate chain

## Immutability

- SignatureRecords are **append-only**; UPDATE and DELETE are rejected by `SignatureImmutabilityInterceptor`.
- Database-level REVOKE on UPDATE/DELETE is recommended in production runbooks.

## Key Management

| Phase | Approach |
|---|---|
| Development | Self-signed RSA 2048-bit certificate (auto-generated) |
| Staging | PFX certificate from internal CA |
| Production | HSM-backed keys via `ISignatureService` abstraction |

The `ISignatureService` interface abstracts the signing backend. Replace `SoftwareSignatureService` with an HSM implementation when hardware is available.

## Canonical Payload

Payloads are serialized to deterministic JSON (alphabetically sorted keys, minified, camelCase) before SHA-256 hashing. This ensures:

1. **Determinism**: Same logical payload always produces the same hash
2. **WYSIWYS**: What You See Is What You Sign
3. **Tamper evidence**: Any modification invalidates the signature

## Verification

Independent verification is possible with:
1. The original payload (or its canonical hash)
2. The signing certificate's public key
3. The `SignatureRecord` containing the signature bytes

See [SIG_001_VERIFICATION.md](SIG_001_VERIFICATION.md) for verification procedures.

## Compliance

- Signatures are stored per-tenant with tenant isolation enforced.
- Certificate thumbprints enable audit trail per signing key.
- LTV status tracking supports long-term archive validity.
