1. docker compose run -e SVC_PAYROLL_CLIENT_SECRET=<rotated> keycloak-reconcile.
2. Old secret -> 401 unauthorized_client; new -> token.
3. Second run with the same value -> reconcile: 0 changes.
4. Restore: reconcile with the stack's value; the lab secret works again; payroll-web ready.