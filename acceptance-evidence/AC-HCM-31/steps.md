31b. central-a seats the 120 admitted applicants in the two sessions (random 8-digit exam numbers), issues the admission
     cards (PDF), records attendance (2 absent per session) and confirms the sessions were held.
31c. Three markers are assigned per session by employee. Each marker sees exam numbers only (no name, no national id) and
     no HR page. First and second markers mark every present paper; one paper differs by 20 (> threshold 10) and the
     reconciler decides it. central-a closes marking.
31d. Results computed by central-a; approval by br1-mgr (committee) then a-management-approver (central-a, who computed,
     is refused); published. The public list shows exam numbers and outcomes, never a name or national id. Candidates
     with consent get RESULTS_PUBLISHED (no score).
31e. HR files an objection on behalf of the second portal applicant (by exam number); finalization is refused while it is
     pending; a-compliance upholds it with a corrected mark → result version 2 (approved and published); v1 superseded;
     the window still starts at the first publication.
31f. Test-only SQL moves the first publication back past the window; finalization allocates the five vacancies by merit
     and preference, lists the reserve per line and rejects the rest (kind CompetitionResult). A second call changes nothing.
31g. Bulk offer drafts for the allocated candidates go into the existing per-offer approval; one offer is declined →
     the first valid reserve of the same line is recalled onto the vacated requisition.