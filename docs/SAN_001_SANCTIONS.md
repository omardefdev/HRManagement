# SAN-001 — Sanctions, Export Controls & Supply Chain Availability

**Card:** SAN-001  
**Status:** Done  
**Priority:** P0-National / XL  
**Owner:** Procurement + Legal + Platform Security + Architecture

## Purpose

Inventory and legally classify every external dependency (NuGet packages, container images, build tools, TLS certificates, HSM/KMS, hardware, support contracts, cloud services) with sovereign mirror policy, version pinning, and alternatives — so sanctions, export controls, payment blocks, or geographic restrictions cannot halt build, security patching, or production after launch.

## Scope

- Full external dependency inventory with legal/financial classification
- Classification states: `available`, `restricted`, `unpayable`, `geo-blocked`
- Sovereign internal mirror for packages and images (NuGet.config placeholder → national registry)
- Strict version/digest pinning (global.json, packages.lock.json, base-image-pins.json)
- Local/open alternative for every critical component
- Continuity policy when source is lost
- Legal regime change tracking escalated to STR-005

## Data Model

See `docs/san/dependency-classification.json` for the structured dependency register.

Each dependency entry contains:

| Field | Description |
|-------|-------------|
| `dependencyId` | Unique identifier |
| `category` | nuget / container / build-tool / certificate / hsm / hardware / support / cloud |
| `name` | Component name |
| `source` | Origin URL or vendor |
| `legalStatus` | available / restricted / unpayable / geo-blocked |
| `owner` | Responsible team/role |
| `criticality` | critical / high / medium / low |
| `alternative` | Local mirror, open substitute, or signed risk acceptance |
| `pinning` | Version or digest pin reference |
| `mirrorPolicy` | sovereign-mirror / digest-pinned / exception |

## Business Rules

1. **100% dependencies classified** — each with owner, legal status, and alternative or signed risk acceptance.
2. **Air-gapped build produces same signed artifacts** — sovereign mirror policy documented; live mirror = Blocked until provisioned.
3. **No critical single-source without time-bound exception** — alternatives required for Tier-0 components.
4. **global.json, packages, and images pinned** — no open version ranges in production.
5. **Emergency patching possible without external access** — mirror cache policy defined.
6. **No external account required for production runtime** — vendor tokens are build-time only.

## Relationship to SUP-001 / BLD-001 / DB-001

| Card | Focus |
|------|-------|
| SUP-001 | SBOM, Cosign, supply-chain CI |
| BLD-001 | Build provenance and admission |
| DB-001 | Reproducible deps and SDK pin |
| SAN-001 | Sanctions classification and sovereign mirror policy |

## Acceptance Criteria Mapping

- [x] Dependency inventory with legal classification and owners
- [x] Sovereign mirror policy (NuGet.config + base-image-pins)
- [x] Version/digest pinning references
- [x] Alternatives or risk acceptance for critical components
- [x] Air-gapped build path documented (live mirror = Blocked)
- [x] Continuity policy on source loss

## Artifacts

| File | Purpose |
|------|---------|
| `docs/SAN_001_SANCTIONS.md` | This document |
| `docs/san/dependency-classification.json` | Dependency legal classification register |
| `scripts/san001-validate.ps1` | Validates SAN-001 artifacts |
| `San001SanctionsTests.cs` | Unit tests for classification schema |

## Validation

- `scripts/san001-validate.ps1`
- `San001SanctionsTests`
