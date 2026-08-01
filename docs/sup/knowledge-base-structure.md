# SUP-002 — Knowledge Base Structure

**Card:** SUP-002  
**Purpose:** Define the structure and governance of the service desk knowledge base.

## Structure

```
knowledge-base/
├── access-identity/
│   ├── KB-ACC-001-password-reset.md
│   ├── KB-ACC-002-mfa-enrollment.md
│   └── KB-ACC-003-role-request.md
├── payroll/
│   ├── KB-PAY-001-payslip-not-generated.md
│   ├── KB-PAY-002-deduction-discrepancy.md
│   └── KB-PAY-003-retroactive-adjustment.md
├── leave-attendance/
│   ├── KB-LEA-001-balance-discrepancy.md
│   ├── KB-LEA-002-approval-stuck.md
│   └── KB-LEA-003-attendance-correction.md
├── integration/
│   ├── KB-INT-001-civil-registry-timeout.md
│   └── KB-INT-002-treasury-payment-reject.md
├── platform/
│   ├── KB-PLT-001-slow-dashboard.md
│   └── KB-PLT-002-file-upload-error.md
└── _templates/
    └── KB-TEMPLATE.md
```

## Article template

Each knowledge base article must contain:

1. **Title** — concise problem statement
2. **Article ID** — `KB-{CATEGORY}-{NNN}`
3. **Category** — maps to ticket categorization
4. **Symptoms** — what the user reports
5. **Diagnosis** — L1 triage steps
6. **Resolution** — step-by-step fix
7. **Escalation criteria** — when to route to L2/L3
8. **Related articles** — cross-references
9. **Last reviewed** — date and reviewer name

## Governance

| Rule | Detail |
|------|--------|
| Top 20 coverage | The 20 most frequent ticket types must have articles |
| Review cycle | Quarterly accuracy review |
| New known error | Article created within 5 business days of identification |
| Ownership | Each article has a named owner |
| Retirement | Obsolete articles archived, not deleted |
| Feedback | L1 agents can flag articles as inaccurate or incomplete |

## Metrics

| Metric | Target |
|--------|--------|
| Articles per top-20 issue | 100 % |
| Articles reviewed in last 90 days | ≥ 80 % |
| L1 usage rate (articles opened per ticket) | ≥ 60 % |
| Article accuracy flag rate | ≤ 5 % |

## Integration with ticketing

- Ticket system auto-suggests relevant KB articles based on category.
- Resolution linked to KB article ID for FCR tracking.
- Problem management creates KB articles from recurring incidents.
