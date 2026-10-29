Seed: public posting a0000001-0000-4000-8000-000000000908.
1. a-management-approver (Module.HrCore lets the role into HR.Core) opens the posting Details (read is allowed).
2. Negative: POST PublicJobPostings/Edit → 403 (explicit [Authorize(Policy = HrCoreWrite)] since PLAN-25, G-15;
   the gateway's HrCoreWrite rule for mutating requests refuses it as well). The posting is unchanged.