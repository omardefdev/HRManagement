# FIX-CAREERS-REF: the candidate sees his reference number on the careers portal

**The defect.** A candidate who applied on `/careers` saw an empty badge on the confirmation page, and none of the portal's refusals
(an unavailable posting, a duplicate application, a closed competition). HR.Core hands both to the next page through its MVC flash
cookie (`.AspNetCore.Mvc.CookieTempDataProvider`); the browser sends it back, but the gateway's cookie filter
(`GatewayUpstreamCookieForwarding`) forwarded that cookie on the staff routes only (PLAN-42) and dropped it on the careers route.
Reproduced on the lab stack and read on the demo before the fix (`reports/fix-careers-ref-progress.md`, F0).

**The fix.**
- The careers route forwards the flash cookie by its exact name, beside the antiforgery cookies and `careers_session`. Every other
  cookie stays dropped (near-names included), and the careers route still carries no `Authorization`. The cookie is HR.Core's own,
  scoped to `/careers` by HR.Core's path base there, protected by HR.Core's keys, with no identity in it. The staff routes forward
  exactly what they did.
- The confirmation page shows the number in its badge, once. With no number (a reload, an expired cookie, a direct visit) it shows no
  empty badge: it says the number is shown once and that the agency's recruitment office can help, and it promises a message with
  the number only when a candidate notification provider is configured (not `Provider=None`, the demo's setting).
- The competition page showed a refusal twice once the flash crossed (the view and its layout both printed it): the view's copy is
  removed.

**Tests that read the page.** AC-HCM-30a and AC-HCM-31a read the number from the confirmation page and only check it against the
database; both recruitment journeys do the same; AC-HCM-30a and AC-HCM-31a prove a refused apply's message on the page it lands on.
Unit tests pin the filter on every route and the transform without staff credentials; an in-process HR.Core test renders the page
with the cookie the filter forwards and without it. A server check (`AcCareersRefVpsChecks`) proves the flash crosses on the demo,
writing nothing.

**Not changed (for the owner's decision).** `Notifications:Candidate:Provider` is `None` on Production and the demo, so the
«تم استلام الطلب» message and the portal's one-time code stay pending there; `/careers/track` works, `/careers/my` signs nobody in.
A real provider (SMTP or SMS) needs the owner's decision and credentials.
