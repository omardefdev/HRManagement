# EXT-004 — Signer intent and organizational seals

## Signer intent

`signerIntentRequired` and `signerRoleRequired` = true. Every legal signature must record:

| Field | Rule |
|---|---|
| signerId | Proven identity (lab: mock cert subject) |
| signerRole | Organizational capacity (e.g. payroll-approver) |
| intentStatement | Explicit act (approve / seal / acknowledge) |

Missing role → FAIL (`missing-signer-role-fail`).

## Form signatures

`formSignatureNotLegalEvidence` = true. UI form signatures (`ContractSignaturesController`) are **not** legal evidence even if labeled Digital. Treating them as court-grade truth → FAIL (`form-signature-as-legal-fail`).

## Organizational seals

`cryptoSealRequiredForLegal` = true; `metadataOnlyInsufficient` = true. `OfficialSealRecord` name/file/dates alone do not constitute a cryptographic seal. Legal seal requires crypto seal under this pack's mock policy (live CA = EXT-004a).

See [ADR-EXT-004](ADR-EXT-004-national-pki-signatures-timestamps.md).
