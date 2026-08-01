# PSIRT-001 — Vulnerability Disclosure Channel

## Reporting a vulnerability

Security researchers and users can report vulnerabilities through the following channels:

| Channel | Address | Encryption |
|---|---|---|
| Email | security@hrplatform.gov.sy | PGP key available at /pgp-key.asc |
| Web form | /security/report | TLS required |
| Authenticated in-app | Settings → Report Security Issue | Session-bound |

## Safe harbor

We commit to:
- **No legal action** against good-faith reporters who follow this policy.
- **Acknowledgment** within 24 hours of receipt.
- **Coordinated disclosure**: we ask reporters to allow us the SLA period before public disclosure.
- **Credit**: reporters will be credited in the advisory unless they prefer anonymity.

## Scope

In scope:
- All production and staging endpoints.
- All API endpoints documented in OpenAPI specs.
- Authentication and authorization flows.
- File storage and download paths.
- Inter-service communication.

Out of scope:
- Social engineering of staff.
- Physical security.
- Third-party services not operated by the platform team.
- Denial of service attacks (report but do not test).

## Report requirements

A good report includes:
1. Description of the vulnerability.
2. Steps to reproduce.
3. Affected component/endpoint.
4. Impact assessment.
5. Suggested fix (optional).

## Response process

1. Acknowledgment within 24 hours.
2. Severity assignment per [`severity-sla.json`](./severity-sla.json).
3. Regular status updates to reporter.
4. Advisory published after fix deployed.
5. Reporter credited (with consent).
