# ADR-S2S-042: the trust boundary between services

## Status

Accepted (PLAN-42, decisions D-POL-2 and D-S2S-1..4 of 2026-10-31).

## Context

Before this plan a remote denial or an outage became an allow, Shadow changed what callers did, one HMAC secret let any container make a "gateway" proof, one Kubernetes secret held every service's credentials, and a user could reach routes meant for services.

## Decision

### Gateway proof v2

- The gateway signs with an ECDSA P-256 private key that only it holds. Modules hold the public key.
- Payload: `v2\n{unixTs}\n{nonce}\n{METHOD}\n{pathAndQuery}\n{base64url(SHA256(Authorization header or ""))}`. Headers: `X-HR-Gateway-Proof` (base64url, IEEE P1363), `-Ts`, `-Nonce` (16 random bytes), `-Path`.
- Allowed skew is 60 seconds. A nonce is accepted once per instance, for twice the skew, and is registered only after the signature verifies, so a forger cannot fill the cache.
- A service account needs no proof: its token says who it is. `azp` decides whether a principal is a service account; a user named `service-account-x` is not one.
- Keys are PEM or a single-line base64 of the DER (PKCS#8, SPKI), because `.env` and Compose do not carry multi-line values safely. A missing or broken key stops the boot, and a module that holds the private key stops too.
- HMAC v1 is accepted in `Development` only, with `GatewayCallerProof:AllowV1InDevelopment=true`.

Accepted risk: instances do not share the nonce cache, so a proof can be replayed to a second instance within 60 seconds. The proof still works only with the token it was made for. A shared cache (Redis) and mTLS are wave 2.

### Failure behaviour of policy enforcement

| Mode | Reply 200 | Anything else |
|------|-----------|---------------|
| Off | no call | no call |
| Shadow | caller gets the Off decision; the server's decision is kept aside | caller gets the Off decision; a warning and a counter |
| Enforce | used as it is, allow or deny | `FailClosed`: refused with a clear message. `AllowWithAlert`: allowed, degraded, an error in the log |

- The Off decision is the static custody rules for `Assets.Custody` and a neutral allow for every other domain.
- The server's `EffectiveMode` decides how a 200 is read. The local mode decides the failure path only. A difference warns and counts.
- A 200 without a decision or a mode is a failure, not an allow.
- `AllowWithAlert` stops the boot in Production (D-POL-2).
- No policy is copied locally any more, so nothing can leak between tenants.

### Who may ask

- `POST api/internal/policies/enforce` serves the owning service only: `PolicyDomainOwners` maps each domain to one client and its subject types. `Core.*` is never reached over HTTP. PLAN-49's catalog replaces this table.
- The server derives mode, actor (`X-Acting-User`) and tenant (`X-Tenant-ID`). A `TenantId` in the body that differs is a 400.
- A workflow starts only for a triple of `PolicyWorkflowTriggerAllowlist`: today (`Assets.Custody`, `AssetAssignment`, `ASSET-ASSIGN-APPROVAL`).
- `CodeKindOwners` maps each official number kind to one client (D-S2S-3); every other kind is issued inside HR.Core only. The tenant comes from the request, never the body.
- `S2s042InternalRoutesAreServiceOnlyTests` walks every web assembly: every `api/internal/*` action carries a `ServicePolicies` policy.

### Legal hold

`HttpEmployeeLifecycleGate` uses a service token and refuses on anything but a clear 200 (D-S2S-4), at creation and at publication. All three paths that create a separation take it: the handler, the wizard and the inbound event. A refused inbound event fails and stays on the broker's retry and error path.

### Secrets

Each Deployment references `<deployment>-secrets` alone (`docs/S2S_SECRET_MATRIX.md`). A placeholder in any key stops the boot outside Development, Testing and Acceptance.

## Antiforgery on the internal POSTs

`PolicyEnforceInternalController` and `CodeGenerationInternalController` carry `[ValidateAntiForgeryToken]` on the class and `[IgnoreAntiforgeryToken]` on their actions. CSRF needs an ambient credential, and these actions accept only a client_credentials token, which no browser holds. The class attribute keeps any POST added later protected by default. This also satisfies `cs/web/missing-token-validation` without a new baseline entry; that is a side effect to be aware of, not the reason.

## Consequences

- Enforce makes HR.Core a stop point for the domains in it. No remote domain is in Enforce in production today.
- Rolling back to a tag before this plan needs `GATEWAY_CALLER_PROOF_SECRET`, so it stays in the server's `.env` until the next plan that deploys removes it.
- A refusal must reach the user, so the gateway now forwards the flash message cookie `.AspNetCore.Mvc.CookieTempDataProvider` by exact name on staff routes. It stays dropped on the careers route. It carries no identity, and only the service that wrote it can read it.
- The image entrypoint is bash, because dash drops environment variables with a dot in the name. Modes such as `PolicyEngine__Assets.Custody` reach the process now; before this plan they silently fell back to Off.
