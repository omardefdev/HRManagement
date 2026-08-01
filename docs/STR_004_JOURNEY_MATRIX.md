# STR-004 — Journey matrix

Source: [`str-product/nofork-matrix.json`](str-product/nofork-matrix.json) — [ADR-STR-004](ADR-STR-004-single-product-no-fork.md).

## ConfigTaxonomy

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| SurfaceKinds | Yes | architecture | surfaces[] kinds |
| ForbiddenCode | Yes | architecture | code-forbidden rows |

## ExtensionPoints

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| AllowedMechanisms | Yes | product | EXTENSION_POINTS |
| EntitlementFlags | Yes | product | STR-003 / REL-001 |

## DualAgencySameRelease

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| SameDigest | Yes | program | agency-a + agency-b |
| ConflictingPolicy | Yes | product | walkthrough |

## PromotionRollback

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| ConfigPackSchema | Yes | platform | CONFIG_PROMOTION |
| UpgradeStub | Yes | platform | UPGRADE_REHEARSAL → PROD-001 |

## RepoNoForkRule

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| NoAgencyBranches | Yes | platform | REPO_NOFORK_RULE |
| ValidateHarness | Yes | qa | str004-nofork-validate |

Blockers: PROD-001, ONB-001 = Blocked.
