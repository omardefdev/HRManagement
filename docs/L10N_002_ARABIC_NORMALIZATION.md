# L10N-002 — Arabic Text Normalization, Collation & Deduplication

**Card:** L10N-002  
**Status:** Done  
**Priority:** P1-National / L  
**Owner:** Data Platform + HRCore + Data Quality

## Problem

Arabic text stored in `Employee.FirstName`, `FullName`, and other name fields uses raw
Unicode without normalization. Common orthographic variants — أحمد vs احمد vs أحمَد —
are treated as different strings, breaking:

- **Search** (`Contains(term)` on raw text misses variants)
- **Unique constraints** (duplicate employees with variant spellings)
- **Deduplication** (MDM-002 golden record matching)
- **Sorting** (inconsistent collation)

## Solution

### ArabicTextNormalizer

`SharedKernel/Localization/ArabicTextNormalizer.cs` — a deterministic, versioned
normalizer that produces a canonical form for matching without modifying the original.

### Normalization Rules (v1)

| Rule | Input | Output | Example |
|------|-------|--------|---------|
| Remove diacritics | U+064B–U+065F, U+0670 | removed | أحمَد → أحمد |
| Hamza/Alef → bare Alef | آ أ إ ٱ | ا | أحمد → احمد |
| Alef Maqsura → Ya | ى | ي | مصطفى → مصطفي |
| Ta Marbuta → Ha | ة | ه | فاطمة → فاطمه |
| Remove Tatweel | ـ (U+0640) | removed | عـلـي → علي |
| Arabic-Indic digits → ASCII | ٠-٩, ۰-۹ | 0-9 | ١٢٣ → 123 |
| Collapse whitespace | multiple spaces | single | محمد  علي → محمد علي |
| Trim | leading/trailing | removed | |

### Design Principles

1. **Original preserved** — stored text is never modified; normalized form is a computed column or separate indexed field.
2. **Versioned** — `RuleVersion` increments when rules change, triggering re-indexing.
3. **Deterministic** — same input always produces same output.
4. **No automatic merging** — normalization informs matching but does not trigger automatic record merging (MIG-004 controls that).

### Database Strategy

- Normalized column alongside original (e.g. `FirstNameNormalized`)
- Unique constraints and search indexes on normalized columns
- PostgreSQL ICU collation (`ar-u-ks-level1`) recommended for sort order
- Re-indexing on rule version change via migration

### Integration Points

- **Search**: lookup API normalizes search term before `Contains` comparison
- **Deduplication**: MDM-002 uses normalized form for candidate matching
- **Reports/PDF/Export**: display original text, sort by normalized
- **Civil Registry (SYR-009)**: incoming names normalized for matching

## API

```csharp
// Normalize a string
string normalized = ArabicTextNormalizer.Normalize("أحمَد");
// Result: "احمد"

// Compare two strings
bool same = ArabicTextNormalizer.AreEquivalent("أحمد", "احمد");
// Result: true

// Check rule version for re-indexing
int version = ArabicTextNormalizer.RuleVersion;
```

## Files

- `SharedKernel/Localization/ArabicTextNormalizer.cs`
- `scripts/l10n002-validate.ps1`
- `L10n002ArabicNormalizationTests.cs`
