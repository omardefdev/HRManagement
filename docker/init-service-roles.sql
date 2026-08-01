-- IAM-004: reference SQL for per-service roles (idempotent documentation).
-- Prefer docker/init-service-roles.sh at container first-boot (env-substituted passwords).
-- Manual apply example (replace passwords):
--
--   CREATE ROLE hr_core_app LOGIN PASSWORD '...';
--   GRANT CONNECT ON DATABASE hr_core_db TO hr_core_app;
--   \c hr_core_db
--   GRANT USAGE, CREATE ON SCHEMA public TO hr_core_app;
--
-- Negative check: hr_core_app must NOT CONNECT to payroll_db.

SELECT 'See docker/init-service-roles.sh and scripts/verify-postgres-service-roles.sh' AS note;
