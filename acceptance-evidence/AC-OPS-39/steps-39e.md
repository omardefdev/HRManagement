1. pg_stat_activity on keycloak_db: the only client user is keycloak; keycloak owns the database and every table.
2. Negative: psql as keycloak to hr_core_db -> permission denied for database.