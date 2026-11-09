# PROD-006 Tenant RLS

rlsRequired; crossTenantForbidden. Fail: cross-tenant-rollup-fail. Pass: tenant-scoped-pass.

## National shared read (PLAN-48)

The ten tables of `NationalTenant.TableNames` keep `tenant_isolation` (the only policy with `WITH CHECK`) and gain
`national_shared_read`: `PERMISSIVE FOR SELECT USING ("TenantId"::text = '00000000-0000-4000-8000-000000000001')`. Every agency
session reads the national rows; writes stay under `tenant_isolation` alone, so an agency cannot insert, update, delete or lock
(`SELECT ... FOR UPDATE/SHARE`) a national row.

- Applied on both migration paths: `scripts/run-migrations.sh` (for `hr-core`, after `harden_tenant_rls`; and
  `deploy/bootstrap/bootstrap.sh` on the demo server) runs `scripts/sql/ten048-national-shared-read.sql`;
  `DatabaseMigrationExtensions.ApplyTenantRlsHardeningAsync` applies `TenantRlsMigrationHelper.NationalSharedReadSql` for a
  context with `INationalShared` entities.
- Checked on both: the SQL check block (and `VerifyNationalSharedReadSql`) fails if the policy is missing on a listed table, or
  present on any other. Production fails the start; other environments warn.
- Proved as a non-owner role without `BYPASSRLS` under `FORCE` (`Ten048NationalRlsIntegrationTests`): agency B reads the
  national rows and not agency A's; an `UPDATE` or `INSERT` of a national row is refused; `OwnOrNational` works through EF;
  PLAN-47's validator accepts the national schema's fields; PLAN-47's immutability triggers still refuse a write to a version
  that is not a draft.
- The demo server connects HR.Core as the superuser, so row security is not enforced there (OPS-D, wave 2).
