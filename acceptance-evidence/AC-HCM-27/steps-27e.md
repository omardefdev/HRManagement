1. Published requisition with rank 1; its offer is issued through the two-step workflow.
2. Test-only time travel: the offer's ExpiryDate is set to yesterday.
3. AC-27e (negative): accepting the expired offer is rejected (offer stays Issued).
4. The hr-core container is restarted so RecruitmentOfferExpiryJob runs its first pass (one minute after start).
5. AC-27e (positive): the offer becomes Expired and the application OfferDeclined («انتهاء صلاحية العرض»).