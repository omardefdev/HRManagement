# SYR-008 — Certificate Lifecycle Management

## Overview

All TLS, code-signing, and employee certificates for the Syrian HR platform are issued by the Syrian National CA or Government CA. Private keys are generated and stored in HSM under Syrian custody with duty separation.

## Certificate Types

| Type | CA | Key Storage | Max Validity | Renewal Lead |
|------|----|-------------|--------------|--------------|
| Server TLS | Syrian National CA | HSM | 397 days | 30 days |
| Code Signing | Syrian Government CA | HSM | 3 years | 90 days |
| Employee (digital signature) | Syrian Government CA | HSM / secure token | 2 years | 30 days |
| Timestamp Authority | Syrian Government CA | HSM | 10 years | 1 year |

## Issuance Procedure

1. **CSR generation** — private key generated inside HSM; CSR exported.
2. **Identity verification** — server: infrastructure owner signs request; employee: in-person verification.
3. **CA submission** — CSR submitted to national CA with approval chain.
4. **Certificate installation** — certificate imported to HSM/server; OCSP/CRL endpoints verified.
5. **Monitoring** — automated alerts at renewal-lead threshold.

## Renewal

- Automated monitoring triggers alert at renewal-lead days before expiry.
- New key pair generated in HSM; old key remains for verification of existing signatures.
- Zero-downtime deployment: new certificate deployed alongside old; old removed after propagation.

## Revocation

- Immediate revocation for compromised keys via national CA CRL.
- Emergency revocation procedure: HSM operator + security officer dual authorization.
- OCSP responder updated within 1 hour of revocation.
- All signed artifacts referencing revoked certificate flagged for re-signing.

## Key Ceremony

- Initial root key ceremony conducted with NAITS oversight.
- Dual custody: two separate authorized individuals required.
- Ceremony recorded, hashed, and signed.
- Backup key shares stored in separate secure locations within Syria.

## CRL / OCSP

- CRL published every 24 hours and on revocation events.
- OCSP responder available at `ocsp.national-ca.gov.sy`.
- Services validate certificate status at TLS handshake and signature verification.

## Audit

- Certificate inventory reviewed monthly.
- Issuance/renewal/revocation events logged in evidence chain (EVD-001).
- Annual key ceremony review by independent auditor.
