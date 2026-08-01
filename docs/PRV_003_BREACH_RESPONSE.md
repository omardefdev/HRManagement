# PRV-003 — Data Breach Response and Notification

## Objective

Define the end-to-end process for triaging, scoping, notifying, and learning from data breaches, ensuring compliance with statutory timelines and cross-agency coordination.

## Workflow overview

Machine-readable workflow: [`docs/prv/breach-response-workflow.json`](./prv/breach-response-workflow.json).

### Phases

| Phase | Target | Owner |
|---|---|---|
| **Detection & Triage** | ≤ 4 hours | CSIRT + DPO |
| **Scoping** | ≤ 24 hours | CSIRT + service owners |
| **Risk Assessment** | ≤ 48 hours | DPO + Legal |
| **Regulator Notification** | ≤ 72 hours (statutory) | DPO |
| **Individual Notification** | Without undue delay | DPO + Communications |
| **Remediation** | Per severity SLA | Engineering + Security |
| **Post-Incident Review** | ≤ 14 days | All parties |

## Triage criteria

| Field | Source |
|---|---|
| Affected data categories | Data classification inventory (PRV-001) |
| Affected individuals | Discovery service (PRV-002 DSAR path) |
| Risk to rights/freedoms | DPIA outcomes (PRV-001) |
| Breach vector | EVD-002 forensic evidence |

## Decision: notify or not

The DPO makes the notification decision based on risk assessment. The decision, reasoning, and sign-off are recorded in the evidence chain (EVD-001). A "no notify" decision requires explicit sign-off from DPO and Legal.

## Processor obligations

External processors must notify the controller within the time defined by the data processing agreement (target: ≤ 24 hours before statutory deadline). Late supplier notification triggers escalation per SRE-003.

## Templates

- Regulator notification (formal, multilingual)
- Individual notification (clear, actionable, multilingual)
- Internal escalation
- Cross-agency coordination

## Validation

```powershell
scripts/prv003-validate.ps1
```

## Related cards

- PRV-001 (ROPA / DPIA)
- PRV-002 (individual rights / DSAR)
- EVD-002 (forensic readiness)
- SRE-003 (incident command)
