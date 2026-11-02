1. hr_core_db cloned (protected national IDs) and an empty scratch key ring.
2. Negative: a one-off hr-core-web on the clone with the empty ring exits with the canary message; the ring stays empty.
3. Positive: the same with DataProtection__AllowKeyRingReset=true keeps running and logs Critical.
4. Clone and scratch ring removed; the stack's HR.Core is ready, its canary passed. (The acceptance stack shares one key-ring volume between services, so the lost dp_hr_core volume is played by the empty scratch ring.)