1. Publish a requisition with a closing date (≥ today + 7).
2. Test-only time travel: set its application closing date to yesterday.
3. Restart the hr-core container so RecruitmentIntakeCloseJob runs its first pass (one minute after start).
4. AC-26i: IntakeClosedAt is set, the posting is unpublished, the requisition stays Published,
   /careers shows «التقديم مغلق», and HR intake on the requisition is rejected.