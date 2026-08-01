-- OBS-003: DB roles for gateway_audit_db (run as superuser / db owner).
-- Application runtime should use hrm_audit_writer (INSERT) not a superuser.
-- Investigation UI / SIEM readers use hrm_audit_reader (SELECT only).

-- CREATE ROLE hrm_audit_writer LOGIN PASSWORD 'CHANGE_ME';
-- CREATE ROLE hrm_audit_reader LOGIN PASSWORD 'CHANGE_ME';

GRANT CONNECT ON DATABASE gateway_audit_db TO hrm_audit_writer, hrm_audit_reader;
GRANT USAGE ON SCHEMA public TO hrm_audit_writer, hrm_audit_reader;

-- Writer: INSERT only on audit tables
GRANT INSERT ON TABLE gateway_audit_entries, security_audit_events TO hrm_audit_writer;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO hrm_audit_writer;
-- Legal holds: writer may INSERT; updates to Active should be ops-only (document separately)
GRANT INSERT ON TABLE audit_legal_holds TO hrm_audit_writer;

-- Reader: SELECT only
GRANT SELECT ON TABLE gateway_audit_entries, security_audit_events, audit_legal_holds TO hrm_audit_reader;

-- Explicitly revoke mutation from reader
REVOKE INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public FROM hrm_audit_reader;

-- Prefer revoking UPDATE/DELETE from app role as well:
-- REVOKE UPDATE, DELETE ON TABLE gateway_audit_entries, security_audit_events FROM hrm_audit_writer;
