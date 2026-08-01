# Runbook — Certificate Expiry

**Card:** SRE-002  
**Alert:** `TLSCertExpiringSoon` (windows at 60 / 30 / 15 / 7 days)  
**Severity:** P2 (> 7 days) / P1 (≤ 7 days or expired)  
**Last reviewed:** 2026-07-31

## Impact

- Expired TLS certificates cause immediate connection failures for all clients.
- Internal mTLS expiry breaks inter-service communication.
- External-facing cert expiry shows browser security warnings and blocks users.

## Diagnosis

1. Identify the expiring certificate:
   ```bash
   kubectl get certificates -A -o custom-columns=NAME:.metadata.name,NS:.metadata.namespace,READY:.status.conditions[0].status,EXPIRY:.status.notAfter
   ```
2. Check cert-manager logs for renewal failures:
   ```bash
   kubectl logs -n cert-manager deploy/cert-manager -l app=cert-manager --tail=100
   ```
3. Verify ACME / internal CA connectivity.
4. Check if the certificate secret exists and is current:
   ```bash
   kubectl get secret <tls-secret> -o jsonpath='{.data.tls\.crt}' | base64 -d | openssl x509 -noout -dates
   ```

## Mitigation

| Step | Action | Expected result |
|------|--------|-----------------|
| 1 | Trigger manual cert-manager renewal: `kubectl cert-manager renew <cert>` | New cert issued |
| 2 | If CA unreachable: fix network/DNS, retry | Renewal succeeds |
| 3 | If ACME rate-limited: use backup CA or manual CSR | Cert replaced |
| 4 | If already expired: replace secret manually, restart ingress/pods | TLS restored |

## Escalation

- If cert expired and not resolvable in 15 min → page **Security on-call**
- If SOV-003 key-custody ceremony required → engage **Cryptography Authority**

## Post-resolution

- Verify all endpoints respond with valid certificates.
- Confirm cert-manager watch is active for future renewals.
- Review alert thresholds (60/30/15/7 day windows per OPS-007).
- Trigger postmortem if any user-facing or inter-service TLS failure occurred.
