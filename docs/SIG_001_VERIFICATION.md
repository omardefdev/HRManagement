# SIG-001 — Verification

## Automated

| Check | Location |
|---|---|
| Canonical hash determinism | `Sig001SignatureTests.Same_payload_produces_same_hash` |
| Sign + verify roundtrip | `Sig001SignatureTests.Sign_and_verify_roundtrip` |
| Tamper detection | `Sig001SignatureTests.Tampered_payload_fails_verification` |
| Append-only interceptor | `Sig001SignatureTests.Update_and_delete_are_rejected` |
| Policy enforcement | `Sig001SignatureTests.Policy_rejects_unknown_subject_type` |
| Schema/docs present | `Sig001SignatureTests.Docs_exist` |
| Validate script | `scripts/sig001-signature-validate.ps1` |

## Manual verification drill

1. Sign a contract payload through `ISignatureService.SignAsync`.
2. Retrieve the `SignatureRecord` from the database.
3. Using an independent tool (OpenSSL / PowerShell), verify the Base64 signature against the canonical payload hash using the certificate's public key.
4. Attempt to UPDATE the `SignatureRecord` → must fail with `InvalidOperationException`.
5. Attempt to DELETE the `SignatureRecord` → must fail with `InvalidOperationException`.
6. Modify one byte of the original payload, recompute the canonical hash → must differ from the stored hash, verification must fail.

## Certificate lifecycle verification

1. Sign with a certificate valid until T+2 years.
2. After certificate expiry (simulated), verify that the signature is still cryptographically valid against the stored public key.
3. LTV status should reflect certificate chain status at time of signing.

## Acceptance mapping

| Criterion | Evidence |
|---|---|
| Identity from authenticated claim | `SignerSubject` bound to identity token, not form |
| Any byte change invalidates | Canonical hash + signature verification |
| Operator cannot fabricate | Private key required; interceptor prevents mutation |
| Verification after cert expiry | LTV fields + public key archive |
| Independent verification | `SignatureVerifier` + OpenSSL recipe in validate script |
