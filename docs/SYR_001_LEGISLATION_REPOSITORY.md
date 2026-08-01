# SYR-001 — Syrian Legislation Repository & Legal Source Binding

**Card:** SYR-001  
**Status:** Done  
**Priority:** P0-National / XL  
**Owner:** Legal Product Owner + Ministry Legal Teams

## Overview

Establishes a structured, version-controlled repository of Syrian legislation that
governs HR/payroll/leave/pension/tax decisions. Every rule, calculation, and policy
in the system must trace back to a specific legal source (law, decree, regulation)
with article reference, effective date, and certified text.

## Scope

### Primary Legislation

| Law/Decree | Subject | Status |
|------------|---------|--------|
| **Law 50/2004** | Basic Employment Law (قانون العاملين الأساسي) | Active with amendments |
| **Law 53/2021** | Labor Code for private/mixed sector | Active within scope |
| **Decree 67/2026** | Wage reform tables | Active |
| **Decree 68/2026** | Compensation reform | Active |
| **Decree 135/2026** | Social insurance amendments | Active |
| **Decree 188/2025** | Fixed public holidays | Active |
| **Law 12/2024** | Personal Data Protection | Active |

### Supporting Instruments

- Executive instructions (تعليمات تنفيذية)
- Ministerial decisions
- Official Gazette (الجريدة الرسمية) publications
- Special regime regulations (أنظمة خاصة)

## Data Model

### Legislation Register

See `docs/syr/legislation-register.json` for the structured register with hierarchy,
effective dates, and source binding.

Each legislation entry contains:

| Field | Description |
|-------|-------------|
| `legalSourceId` | Unique identifier for the legal source |
| `type` | law / decree / regulation / instruction / decision |
| `number` | Official number (e.g. "50/2004") |
| `titleAr` | Arabic title |
| `titleEn` | English title (optional) |
| `issueDate` | Date of issuance |
| `effectiveDate` | Date of effect |
| `publicationDate` | Official Gazette publication date |
| `status` | active / amended / repealed / superseded |
| `amendedBy` | List of amending instruments |
| `repeals` | List of repealed provisions |
| `articles` | Structured article references |
| `certifiedTextHash` | SHA-256 hash of the certified text |
| `owner` | Legal custodian |

### Source Binding

See `docs/syr/legal-source-binding.md` — every system rule must reference a
`legal_source_id` with article and effective date.

## Business Rules

1. **No rule without source** — every HR/payroll/leave/pension/tax rule must have a `legal_source_id`, article reference, effective date, and legal owner.
2. **No press-source rules** — rules cannot be based solely on press reports; certified text required.
3. **Conflict visibility** — conflicting provisions are surfaced, not auto-resolved by code.
4. **Change notification** — legislative changes produce impact reports before taking effect.
5. **Immutable history** — past legal versions are never modified; only new versions appended.
6. **Hash verification** — certified text packages are hash-verified for integrity.

## Verification

- Independent sample tracing: hire, promotion, leave, salary, dismissal, pension → legal text
- Retroactive amendment and repeal scenarios
- Conflict/overlap between two laws
- Hash and signature verification of legal package

## Dependencies

STR-002, GOV-002, LEG-001

## Files

- `docs/syr/legislation-register.json` — structured legislation register
- `docs/syr/legal-source-binding.md` — binding protocol
- `scripts/syr001-validate.ps1` — artifact validation
- `Syr001LegislationTests.cs` — xUnit tests
