## 23a

br1-mgr (hr-core + branch-manager, RIF) opens «صندوق العمل».
Expected: REQ-2026-A-AC33-PEND1 (step 1, role hr-core) and OFF-2026-A-AC33-PEND (step 1) are listed;
REQ-2026-A-AC33-PEND2 (step 2, role hr-management-approver) is not.

## 23b

a-management-approver (role hr-management-approver, no hr-core).
Expected: REQ-2026-A-AC33-PEND2 (step 2) is listed; the step-1 requisition and step-1 offer are not;
the legacy request (role hr-core) is not.

## 23c

central-a (hr-core, whole agency) created every seeded item. Expected: none of them is listed (creator SoD),
although its role would match step 1.
Precondition: the seeded items are still pending (SQL). Positive control: br1-mgr, with the same step role, does
list the step-1 requisition and offer — so their absence for central-a is the creator rule, not missing data.

## 23d

a-compliance (role compliance only) is the assigned reviewer of the seeded appeal; a-compliance-2 is not.
Expected: a-compliance lists the appeal and only appeals; a-compliance-2 does not list it.

## 23e

For br1-mgr: GET ui/hr-core/api/stats/pending-approvals → {count, legacyCount, workflowCount}, then the inbox.
Expected: count == workflowCount + legacyCount == number of item rows == the «معلّق» badge, and count ≥ 3
(two seeded workflow items and the legacy request are actionable for br1-mgr).

## 23f

Seed: legacy ApprovalRequest a0000001-0000-4000-8000-000000000926 (Pending, step 1 role hr-core, requested by central-a).
Expected: br1-mgr's inbox lists it with the «legacy» badge and the /Approvals/Review/<id> link, and the review page opens.