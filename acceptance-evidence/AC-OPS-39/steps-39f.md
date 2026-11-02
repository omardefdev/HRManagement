1. central-a2 signs in through the gateway (code flow) and creates an employee with a national ID (stored as CfDJ8...).
2. A second create form is loaded (antiforgery token).
3. docker compose up -d --force-recreate (every service), all applications ready.
4. The same browser session opens the employee: the national ID is shown in full; the form from step 2 is accepted.