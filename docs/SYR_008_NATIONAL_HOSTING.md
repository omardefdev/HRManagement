# SYR-008 — National Hosting, Domain, and Sovereign Certificates

## Purpose

Document and enforce the national hosting topology for the Syrian HR platform: approved data centers, `.gov.sy` domains, sovereign TLS/code-signing certificates, HSM/KMS under Syrian custody, and certificate lifecycle management.

## Scope

- Hosting in approved national data center / sovereign cloud
- Primary and DR site topology
- `.gov.sy` / `.sy` domain registration per accreditation
- DNS management and egress controls
- HSM/KMS under Syrian custody with duty separation
- Server, employee, and software certificates from national/government CA
- CRL/OCSP and trusted time source
- Support access logging
- Prevention of unauthorized external administration

## Artifacts

| File | Purpose |
|------|---------|
| `docs/syr/hosting-topology.json` | Site definitions, roles, egress rules, DR pairing |
| `docs/syr/certificate-lifecycle.md` | Certificate issuance, renewal, revocation, and key ceremony procedures |

## Acceptance Criteria

- Written approval for topology, data locations, and copies
- Root/data keys under Syrian custody with duty separation
- No production secret held by vendor
- Certificate lifecycle (CRL/OCSP/time source) tested
- Failover does not transfer data outside approved site
- All endpoints use official signed certificates
- NAITS inspection passed
- Key ceremony conducted
- Egress audit clean
- DR with sovereignty verification
- Unsigned artifact deployment rejected

## Validation

- `scripts/syr008-validate.ps1`
- `Syr008HostingTests`
