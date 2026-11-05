AC-HCM-33b — ACCEPTANCE_REQUIRE_ALL=true turns a skipped required scenario into a failure.

Observed: (1) P1, without the stack, filter `AcHcm2|Ac33`: 18 failed (required AcHcm2x scenarios), 1 skipped (Ac33, not required). (2) P6 live run on the clean stack: AC-HCM-26h (required, precondition missing) failed with «Required acceptance scenario blocked (ACCEPTANCE_REQUIRE_ALL=true): No second central hr-core persona…» and recorded Blocked in acceptance-evidence/AC-HCM-26/result.json.
