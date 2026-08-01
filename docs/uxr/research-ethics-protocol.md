# UXR-001 — Research ethics and privacy protocol

**Card:** UXR-001  
**Effective:** 2026-07-31  
**Owner:** Service Design + DPO (Privacy Operations)

## Principles

1. **Voluntary participation** — participants may withdraw at any time without affecting employment or service access.
2. **Informed consent** — written or recorded verbal consent before any data collection.
3. **Minimize harm** — no deceptive tasks; no exposure to live disciplinary or medical records.
4. **Privacy by design** — align with PRV-001 ROPA and PRV-002 individual-rights handling.

## Consent requirements

Before each session, facilitators must confirm:

- [ ] Purpose of the study and how findings will be used
- [ ] Recording scope (screen, audio, notes)
- [ ] Anonymization: names and agency identifiers removed in reports
- [ ] Right to skip any task or question
- [ ] Contact for withdrawal or data deletion requests (DPO channel)

Consent template stored in research ops toolkit; version tracked in session metadata.

## Data handling

| Data type | Rule |
|-----------|------|
| Production employee records | **Prohibited** in sessions — use staging/synthetic personas |
| National ID, bank account | **Never** displayed or typed during studies |
| Screen recordings | Encrypted storage; retention ≤ 12 months unless legal hold |
| Session notes | De-identified within 5 business days |
| Health / payroll / disciplinary flows | DPO pre-approval required; redacted fixtures only |

## Vulnerable participants

- Assistive-technology users: provide accessible consent in preferred format.
- Low-literacy or low-digital-literacy participants: plain-language Arabic; allow supporter present if participant chooses.
- Field workers on shared devices: ensure logout and cache clear after session.

## Conflict of interest

Researchers must not facilitate sessions at their own agency if they hold approval authority over participants. Escalate to national Service Design pool for cross-agency moderation.

## Incident response

If PII is accidentally captured:

1. Stop recording immediately.
2. Notify DPO within 4 hours.
3. Delete affected segment per PRV-002 correction workflow.
4. Document in research incident log (no PII in log body).

## Continuous ethics review

- Quarterly review of consent forms and retention against GOV-001 retention matrix.
- New journey types (e.g., performance appeal, whistleblower) require DPO sign-off before recruitment.

## Validation

Referenced by `scripts/uxr001-validate.ps1` and `Uxr001ResearchTests`.
