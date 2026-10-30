Linux clean-HEAD run (scripts/ci-local.ps1), trx files:
- Plan034ScopedIndexTests (SQLite, real controllers): 6 cases × 12 actions, including "BR2 employee → 403", plus
  3 cases (review E-1) where 110 newer BR2 rows must not push the BR1 rows past the page-size cap.
- Plan034ScopedIndexPostgreSqlTests (Testcontainers): ContractsController and PaymentHoldsController on PostgreSQL.
- BolaArchitectureTests: no Index(employeeId) action without a scope guard, no allow-list.