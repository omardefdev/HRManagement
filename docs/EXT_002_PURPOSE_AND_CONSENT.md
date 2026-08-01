# EXT-002 — Purpose and consent

`consentRequired: true` — every civil-registry query must carry `consentRef` and a purpose from the allowlist:

- hire-identity-proof  
- payroll-beneficiary-verify  
- migration-nid-exception  
- dsar-identity-confirm  

Purpose outside allowlist → FAIL (`purpose-mismatch-fail`). Aligns with GOV-001 DPIA / purpose limitation.
