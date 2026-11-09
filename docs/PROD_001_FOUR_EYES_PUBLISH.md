# PROD-001 — Four-eyes publish

Publisher and reviewer roles must differ. Same-role publish fails.

## PLAN-47: how it works in HR.Core

- **Who authors, who publishes (D-POL-3).** Writing a draft needs the `Policy.Author` policy: `admin`, `hr-core` or
  `policy-author`. Publishing needs `Policy.Approver`: `admin`, `hr-management-approver` or `policy-approver`. The lists come
  from `PolicyEngine:Roles:Author` and `PolicyEngine:Roles:Approver` (an array or a comma list), so they change without code.
  A service account is neither. The gateway applies the same policies to `…/PolicyTemplates/PublishFromUi`,
  `…/api/policies/templates/{id}/publish` (approver) and the draft paths (author).
- **Antiforgery on every lifecycle write.** The eight API writes (drafts, rollback drafts, add, change and remove a rule, the
  effective time, discard, publish) carry `[ValidateAntiForgeryToken]`, because they run with a gateway session as well as
  a bearer token. An API client first calls `GET api/policies/antiforgery` (any policy reader), keeps the antiforgery cookie
  (the service scopes it to `/ui/hr-core`, so a client of `/api/hr-core` sends it at `/`) and sends `requestToken` in the
  header named by `headerName`. The pages carry their form token as before. `evaluate` and `simulate` are unchanged.
- **Nobody publishes what they edited.** Every change to a draft is a row of `PolicyVersionEdits` (create, copy, add, update,
  remove, effective time, discard) with its user. `PolicyPublishService`, the only publish path, refuses a publisher who is
  among the editors or the draft's `CreatedBy` (403), a publisher without an identity (403), and a draft with no editor on
  record (400). Holding both roles changes nothing.
- **No override.** `ForceSameActorForLab` is gone from the API, the controller and the page. The one exception is
  `PolicyEngine:Publish:AllowSameActorInDevelopment=true` in the Development environment.
- **The PROD-001 guards are wired.** Publishing calls `ValidateFourEyesPublish("policy-author", "policy-approver")`,
  `ValidateInvalidPublish` (an invalid version is never published) and, for a rollback, `ValidateRollback`. The publication
  records `MakerUserId`, `CheckerUserId`, `MakerRole`, `CheckerRole`, `Kind` and `EvidenceRef`.
- **Seed change sets (D-POL-11)** publish without four eyes as `SystemSeed`, with the set's id as the evidence; with
  `PolicyEngine:Seeds:RequireApproval=true` they leave a draft for an approver instead.
- **Proof:** `Pol047FourEyesTests`, AC-POL-47a and 47d.
