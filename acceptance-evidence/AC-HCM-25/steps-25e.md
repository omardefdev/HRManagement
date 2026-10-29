1. central-a enters an application on the seeded published requisition, rejects it, files an appeal and assigns a-compliance.
2. Negative (gate): a-compliance-2 POSTs a Decide for it → refused (AppealApprovalGate: not the assigned reviewer).
3. Negative (role): central-a (hr-core, allowed into the appeal pages) sees the server-computed reason
   «هذه الخطوة تتطلب دور «compliance».» on the appeal and its Decide POST is refused — the review step requires «compliance»
   (the actor role is no longer hard-coded to Compliance).
4. Positive: a-compliance decides «Upheld» → Decided, instance Completed.