# EXT-001 — Assurance levels

Lab AAL/FAL mapping (NIST-aligned labels for policy, not a live NIST attestation):

| Level | FAL | Use |
|---|---|---|
| AAL1 | FAL1 | Low-risk citizen self-service |
| AAL2 | FAL2 | Standard employee / HR approver |
| AAL3 | FAL3 | Payroll, security-admin, break-glass |

Each persona in the matrix declares `minAal` / `minFal` / `risk`. High/Critical personas must not map below AAL2; Critical phishing-resistant roles require AAL3.
