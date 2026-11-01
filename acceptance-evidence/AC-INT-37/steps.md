## 37a

1. a-contracts-maker: leave request, advance request, separation (SeparationWizard/Step1, Resignation).
2. Every new ServiceTransaction has a ReferenceNumber and one hr_core_db.CodeIssuances row of kind ServiceTransaction.

## 37b

1. RecruitmentToHireJourneyProdLikeE2ETests ran on the prod-like stack (no wiring from an acceptance file).
2. Its Appointment transaction has a number with a CodeIssuances row; IssueAppointmentDecision issues a decision number with its row.

## 37c

1. a-idcard issues a card, a-it registers a device, a-compliance opens a legal case, a-logistics-maker enters an asset.
   (the card through IssuedCards/Renew: IssuedCards/Create cannot bind its entity, a pre-existing defect).
2. Each gets its number from HR.Core (svc-idcard / svc-it-infra / svc-compliance / svc-admin-logistics, scope hr-core.workflow) with one CodeIssuances row.

## 37d

1. Leave created (UI) and its approval event ILeaveApproved published as HR.Contracts does: an ArchiveItem with an HR.Core ArchiveReference appears within 60 s.
2. hr-core-web stopped; a second leave approved: no ArchiveItem, the message is retried and lands in the archive consumer's _error queue.
3. hr-core-web started and healthy; the _error messages are moved back to the consumer queue (replay): exactly one ArchiveItem.

## 37e

1. hr-core-web, hr-contracts-web and hr-movements-web log 'LegalPack loaded ... sha256=' with the repository file's SHA-256.
2. A Resignation for a law50 employee, a transfer and a promotion are created (the rules are not empty).
3. A one-off hr-movements-web with LegalPacks__ServiceRegimesPath=/nonexistent.json exits with a message naming the key and the pack.

## 37f

1. One-off hr-contracts-web with Integration__HrCoreBaseUrl= (empty): exits at start-up naming the key.
2. One-off hr-contracts-web with http://localhost:5010: exits with 'must use Docker service DNS'.
3. The running stack is unchanged.

## 37g

1. client_credentials token for svc-hr-contracts with its matrix scopes; POST /api/internal/codes/generate (X-Tenant-ID) = 200 and one CodeIssuances row.
2. Token for svc-hr-health (no hr-core.workflow, hence no api-hr-core audience): refused (401) and the CodeIssuances count is unchanged.

## 37h

1. scripts/int037-wiring-check.ps1: production, single-host, prod-like, parity and the k8s overlays PASS.
2. On a copy without hr-contracts-web's Integration__HrCoreBaseUrl it exits 1 naming the service and the key
   (ProductionWiringMatrixTests.Checker_reports_a_missing_url_with_service_and_key is the same check in unit tests).