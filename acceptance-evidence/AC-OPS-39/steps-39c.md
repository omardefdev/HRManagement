1. Password-grant token of br1-mgr (Acceptance exception): the three claims equal the user's attributes.
2. Negative: POST /realms/hrmanagement/account with a changed tenant_id (user token).
3. tenant_id unchanged in the admin API and in a new token (managed attribute, admin edit only).