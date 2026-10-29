1. AC-30d: a citizen applies with a diploma (DIP < BACH) and 1 year of experience (< 2) → flags BelowMinQualification and
   BelowMinExperience; HR Details shows «دون المؤهل المطلوب»; the application is screened (UnderReview);
   shortlisting without a reason is refused; with a reason → Shortlisted, EligibilityOverrideReason + audit EligibilityOverride.
2. AC-30e: central-a schedules an interview → event InterviewScheduled → exactly one FileDrop message (key {eventId}:Email)
   whose subject and body hold no national id.
3. AC-30e (negative): test-only SQL clears NotificationsPlannedAt on that event (replan) and the dispatcher runs again →
   still one CandidateNotification for the key, the FileDrop file is not rewritten.
4. AC-30b (negative): the shortlisted/interviewed candidate's /careers/my shows «مدعو للمقابلة» and no hire step («التعيين»).