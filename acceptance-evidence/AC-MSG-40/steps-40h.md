1. SQL: an agency-A unit in hr_core_db, so no event is published for it.
2. Restart HR.Core: the start-up re-sync publishes every unit under its tenant; the snapshot appears in Payroll and HR.Contracts within 120 s and the outbox drains.
3. Negative: a second restart leaves one snapshot per service (the consumers upsert by id).