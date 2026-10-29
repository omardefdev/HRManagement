1. AC-30a: a citizen applies on the seeded requisition REQ-2026-A-CX01 through the gateway (/careers/{posting}) with email +
   notification consent (documents: AC-30c) → RecruitmentApplication Submitted at once, IntakeChannel Public, linked to the
   citizen row; central-a sees it in RecruitmentApplications?RequisitionId=….
2. AC-30b: central-a rejects with a note. The citizen signs in on /careers/my (reference + national id → one-time code read
   from the FileDrop file of key otp:{challengeId}:Email) → «غير مقبول» and «يمكنك الاستئناف حتى …»; /careers/track shows
   «غير مقبول».
3. AC-30h: the candidate files the appeal from /careers/my → RecruitmentAppeals row FiledBy Candidate, Pending; listed for HR.