# Example Crit/High exception (do not enable in production without AppSec sign-off)
#
# {
#   "id": "EX-2026-001",
#   "packageOrCve": "GHSA-xxxx-xxxx-xxxx",
#   "severity": "High",
#   "owner": "appsec@",
#   "expiresOn": "2026-08-15",
#   "reason": "Fix pending upstream; mitigated by network policy"
# }
#
# Expired entries fail CI via scripts/check-supply-chain-exceptions.sh
