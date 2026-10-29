# Legal Source Binding Protocol

**Card:** SYR-001  
**Purpose:** Every rule, calculation, and policy decision in the HRManagement system
must be traceable to a specific legal source.

## Binding Requirements

### Every System Rule Must Have

1. **`legal_source_id`** — references an entry in `legislation-register.json`
2. **Article reference** — specific article(s) that authorize the rule
3. **Effective date** — when the rule takes effect
4. **Legal owner** — the ministry/authority responsible for the source text
5. **Certified text** — hash-verified copy of the authorizing text

### Binding Record Format

```json
{
  "ruleId": "PAY-RULE-BASE-WAGE-G5",
  "description": "Base wage for Grade 5 employees",
  "legalSourceId": "SYR-DEC-67-2026",
  "articles": [1, 4],
  "effectiveDate": "2026-04-01",
  "expiryDate": null,
  "owner": "Ministry of Finance",
  "certifiedTextHash": "SHA-256 hash of source text",
  "bindingStatus": "active"
}
```

## Prohibitions

1. **No press-source rules** — a rule cannot exist based solely on press coverage;
   a certified text from the Official Gazette or issuing authority is required.
2. **No code-resolved conflicts** — when two legal texts conflict, the system
   surfaces the conflict for legal review rather than choosing one programmatically.
3. **No silent modifications** — changing a past legal version is not allowed;
   amendments create new versions with explicit effective dates.

## Change Management

### When Legislation Changes

1. New entry added to `legislation-register.json` with `amendedBy`/`repeals` links
2. Impact report generated listing all affected rules
3. Affected rules updated with new `legal_source_id` and effective date
4. Four-eyes review before activation
5. Rollback suspends new rules but preserves historical calculations

### Retroactive Changes

- Retroactive amendments are applied with explicit retroactive effective date
- Historical calculations are not silently rewritten
- Re-calculation produces a new version, keeping the old version for audit

## Verification

For any randomly sampled HR decision (hire, promote, leave, salary, dismiss, pension):
1. Trace to the system rule that produced it
2. Trace the rule to its `legal_source_id`
3. Verify the article, effective date, and certified text hash
4. Confirm the text is from an authorized source (Official Gazette or ministry)

## Recruitment Competitions (HCM-031, PLAN-31)

A multi-vacancy competition (`RecruitmentCompetition`) carries its own `LegalSourceId` (validated against the register
like a requisition) and **per-competition legal inputs**, entered by HR and approved with the competition
(RECRUITMENT-COMPETITION-APPROVAL). The system holds **no legal value** for any of them: base, Staging and Production
configuration define no `Recruitment:Competition:Defaults` (checked by `scripts/hcm031-validate.ps1`); Development only
pre-fills the form with laboratory values.

| Input | Field | Binding |
|-------|-------|---------|
| مدة الاعتراض على النتائج (أيام، من أول نشر) | `ObjectionWindowDays` (1–60) | يُحدَّد من المرجع المعتمد عند اعتماد D-2 |
| مدة صلاحية قائمة الاحتياط (أشهر) | `ReserveValidityMonths` (0–36) | يُحدَّد من المرجع المعتمد عند اعتماد D-2 |
| الحد الأقصى للأسطر لكل مرشح | `MaxLinesPerCandidate` (≥ 1) | يُحدَّد من المرجع المعتمد عند اعتماد D-2 |
| علامة النجاح (وعلامة نجاح خاصة بالسطر) | `PassMark`, `CompetitionLine.LinePassMark` | يُحدَّد من المرجع المعتمد عند اعتماد D-2 |
| عتبة الفرق بين المصححين وقاعدة المطابقة | `ReconcileThreshold`, `ReconcileRule` | يُحدَّد من المرجع المعتمد عند اعتماد D-2 |
| ترتيب كسر التعادل | `TieBreakOrder` | يُحدَّد من المرجع المعتمد عند اعتماد D-2 |

The ranges above are input validation only (`CompetitionWorkflowRules`), not legal values. Every result version stores
its computation (`Sha256`, diff, approval instance), so a published ranking traces to the competition's approved inputs
and to its legal source. Acceptance fixtures (`AcceptanceCompetitionPatch`) use test data, never asserted as law.

## Integration Points

- **SYR-003**: Service regime assignment references legal sources
- **SYR-005**: Wage/tax pack rules bind to Decrees 67/68
- **SYR-006**: Insurance/pension rules bind to Decree 135
- **SYR-007**: Data protection rules bind to Law 12/2024
- **SYR-014**: Holiday rules bind to Decree 188/2025
