# DOC-001 — Official Documents, Templates, and Verification

## Objective

Provide a versioned, multilingual template system for producing official documents (decisions, certificates, letters) with digital signature, QR verification, and archival-quality output (PDF/A). Corrections create superseding documents — never in-place overwrites.

## Design

### OfficialDocumentTemplate (SharedKernel)

| Property | Purpose |
|---|---|
| `TemplateCode` | Short code for official numbering (DEC, CRT, LTR) |
| `NameAr` / `NameEn` | Multilingual template name |
| `Version` | Semantic version — new version on any change |
| `EffectiveFromUtc` / `EffectiveToUtc` | Temporal validity |
| `Status` | Draft → Active → Deprecated → Archived |
| `OutputFormat` | PDF/A (default), HTML, DOCX |
| `DataBindingSchemaJson` | JSON schema for required fields |
| `TemplateBody` | Template content (HTML/Razor/XSLT) |
| `QrVerificationEnabled` | QR code for independent verification |
| `SignatureRequired` | Requires digital signature/seal |
| `CorrectionCreatesSuperseding` | Corrections produce new documents |
| `AccessibilityRequired` | WCAG 2.2 AA tagged PDF compliance |

### Document lifecycle

1. Template authored and versioned.
2. Data binding fields populated from domain entities.
3. Preview rendered for approval.
4. Official document generated (PDF/A) with QR + signature.
5. Document archived with hash and version linkage.
6. Corrections issue superseding document (original preserved).

### Verification

Each official document embeds a QR code linking to a verification endpoint. Independent verification checks:
- Document hash matches stored hash.
- Signature is valid (LTV per SIG-001).
- Template version matches the version used at generation time.

## Acceptance criteria mapping

| Criterion | Implementation |
|---|---|
| Document bound to template/data/rule version | `Version`, `DataBindingSchemaJson`, `EffectiveFromUtc` |
| Official copy is immutable | `CorrectionCreatesSuperseding` = true |
| Independent verification via QR | `QrVerificationEnabled` + verification endpoint |
| Printable and accessible | `AccessibilityRequired` + PDF/A output |

## Validation

```powershell
scripts/doc001-validate.ps1
```

## Related cards

- SIG-001 (digital signatures)
- FILE-001 (file lifecycle)
- LEG-001 (records retention)
- L10N-001 (multilingual)
