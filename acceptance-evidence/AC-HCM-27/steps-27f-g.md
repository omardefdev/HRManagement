1. Published requisition with ranks 1..3 and one Assessed application.
2. Rank 1's offer is issued (two-step workflow) and accepted; central-a hires the candidate through the Hire form.
3. AC-27f: ranks 2 and 3 become ReserveListed with ReserveUntil = today + 6 months; the Assessed application is Rejected.
4. Test-only time travel: rank 2's ReserveUntil is set to yesterday.
5. The hr-core container is restarted so RecruitmentReserveExpiryJob runs its first pass (two minutes after start).
6. AC-27g: rank 2 becomes Rejected («انتهاء صلاحية قائمة الاحتياط»); rank 3 stays ReserveListed.