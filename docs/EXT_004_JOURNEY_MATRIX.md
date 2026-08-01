# EXT-004 — Journey matrix

Source: [`ext-pki/pki-signature-matrix.json`](ext-pki/pki-signature-matrix.json) — [ADR-EXT-004](ADR-EXT-004-national-pki-signatures-timestamps.md).

## SignApproval

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| SignerIntentPolicy | Yes | legal | signerIntentRequired |
| MissingSignerRoleFail | Yes | qa | missing-signer-role-fail |

## OrgSeal

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| SealPolicy | Yes | security | organizationalSeals |

## TrustedTimestamp

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| TsaPolicy | Yes | security | trustedTimestamp |
| TsaOutageDegradePass | Yes | qa | tsa-outage-degrade-pass |

## RevocationFailClosed

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| RevocationPolicy | Yes | security | revocation |
| RevokedCertFail | Yes | qa | revoked-cert-fail |

## ExpiredCertLtv

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| LtvPolicy | Yes | legal | ltv |
| ExpiredWithoutLtvFail | Yes | qa | expired-without-ltv-fail |
| ExpiredCertLtvPass | Yes | qa | expired-cert-ltv-pass |

## ClockFailure

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| ClockPolicy | Yes | security | clockSkewPolicy |
| ClockSkewFail | Yes | qa | clock-skew-fail |

## HsmKeyCustody

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| HsmPolicy | Yes | security | hsmCustody |
| KeyExfilFail | Yes | qa | key-exfil-fail |

## ArchiveRestoreVerify

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| ArchivePolicy | Yes | records | ltv |
| ArchiveRestoreVerifyPass | Yes | qa | archive-restore-verify-pass |

## AlgorithmAgility

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| AlgPolicy | Yes | security | algorithmAgility |
| DeprecatedAlgFail | Yes | qa | deprecated-alg-fail |

## FormSignatureGate

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| FormNotLegalPolicy | Yes | legal | signatures |
| FormSignatureAsLegalFail | Yes | qa | form-signature-as-legal-fail |
| SignatureAuditPass | Yes | qa | signature-audit-pass |

Soft blockers: SOV-003 = Satisfied (key-sovereignty binding); national-pki-live / hsm-prod / tsa-live / SYD-011 = Blocked.
