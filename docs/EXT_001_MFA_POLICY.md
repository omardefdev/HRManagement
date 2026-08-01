# EXT-001 — MFA policy

Phishing-resistant MFA is **required** for:

- payroll-operator  
- security-admin  
- break-glass-operator  

Lab authenticators: `webauthn-stub`, `hardware-key-stub`. SMS OTP is **forbidden** for sensitive roles.

Production hardware fleet remains **Blocked** (`phishing-mfa-fleet`).
