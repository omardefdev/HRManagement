# ARC-001 — Independent live criticality review waiver

| Field | Value |
|---|---|
| Card | ARC-001 |
| Expires | When ARC-001a–e Accepted, or 2027-01-31 — whichever first |

Live architecture failure walkthrough, controlled dependency shutdown, recovery-order drill, and full dependency discovery are **deferred**. Engineering adopts lab criticality contracts as a startup/CI binding. This waiver does **not** authorize `LiveCriticalityClaim=true`, raise `National24x7Claim`, or close PHY-001 / SRE-002 / BCP-001 / CAP-001. (ARC-002 is a separate production reference-architecture binding Done card.)
