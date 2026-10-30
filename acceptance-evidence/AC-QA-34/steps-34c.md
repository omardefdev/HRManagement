1. ProgramGovernanceEvaluator.Evaluate (the rule ProgramGovernanceStartupValidator applies at startup with
   EnforceOnStartup=true) on the real matrix and decision log, now → no failure.
2. The same matrix with both simulation exceptions back to Declared (the state before PLAN-34): 2026-09-15 → no
   failure; 2026-09-16 → "EX-STR005-SIM-INCIDENT-WINDOW: Declared exception expired".
3. The closure decision PG-EXC-2026-10-001 is in the decision log. Unit canary: RuntimeGovernanceDocumentsTests
   starts the real validators with each service's Production/Staging settings on TimeProvider.System.