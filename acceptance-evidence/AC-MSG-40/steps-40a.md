1. SQL: an approved promotion (effective yesterday) with its RequestRecorded and Approved checklist steps, for an employee with an open employment record.
2. Gateway UI (a-movements): POST PromotionRequests/Execute -> 302; the request is Executed.
3. hr_movements_db: the OutboxMessage sequence advanced (a row was written with the state).
4. hr_core_db: one inbox receipt for the deterministic MessageId UUIDv5(IPromotionExecuted:<id>:v1) and one note in the employment record within 60 s.
5. Negative: the same message published again with the same MessageId -> still one receipt and one note.