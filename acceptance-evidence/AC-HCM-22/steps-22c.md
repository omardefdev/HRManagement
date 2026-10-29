Negative (seed): the session a0000001-0000-4000-8000-000000000925 has a chair only → submit refused, SubmittedAt stays empty.
Positive:
1. central-a publishes a requisition on an AC33-POOL position and enters an HR application;
   precondition (test-only SQL, assessment is AC-HCM-26): the application is Assessed.
2. central-a creates a Ranking session and adds the seeded roster (session a0000001-0000-4000-8000-000000000924):
   a chair + 3 members, all linked to no persona.
3. Every member votes Agree; decision Recommend with proposed rank 1; submit → SubmittedAt/LockedAt, RECRUITMENT-SELECTION-COMMITTEE.
4. br1-mgr approves steps 1 and 2, a-management-approver step 3 → ApprovedAt set.