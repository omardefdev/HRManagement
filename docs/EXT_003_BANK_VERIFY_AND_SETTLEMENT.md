# EXT-003 — Bank account verification and settlement

`bankAccountVerification.requiredBeforeSettle` = true. Settlement to `Settled` without a verified beneficiary account (lab stub verify flag) fails readiness for production bind; in lab harness, fixtures must set `accountVerified: true` before settle.

## States

Submitted, Acked, Settled, Rejected, Returned — only these codes are allowlisted in the matrix.

PAY-004 (partial funding / arrears) and SYR-011 (Syrian channel ops) are out of scope for this pack; they consume these states later.
