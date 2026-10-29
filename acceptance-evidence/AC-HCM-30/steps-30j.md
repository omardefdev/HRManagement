Fixture: two unlinked citizen rows on REQ-2026-A-CX01 (AC30-BF-0001 Received, AC30-BF-0002 UnderReview; recent dates) and the
seed pack's unlinked internal application (UnderReview) on the BR1 requisition.
1. scripts/hcm030-backfill-unified-intake.ps1 -DryRun -DockerContainer <acceptance postgresql> → reports, writes nothing.
2. AC-30j: -Apply → every unlinked requisition-linked row gets a RecruitmentApplication (Public/Internal) with the mapped
   status (Received → Submitted, UnderReview → UnderReview; Rejected for a closed/cancelled requisition) and the link.
3. AC-30j (negative): a second -Apply links nothing (Total linked: 0) and inserts no RecruitmentApplications.