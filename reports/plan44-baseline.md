# PLAN-44 baseline

Base: `origin/main` f8f5cd04 (PLAN-43 merged, PR 38). Normal mode, primary clone. 2026-11-06.

## 0.2 PLAN-43's parts
`WorkflowInstance` has `RequestedByUserId`, `SourceClientId`, `IdempotencyKey`; `WorkflowSodRules.cs` exists. `xmin` is on `WorkflowDefinition` only.

## 0.3 and 0.4
- Build `-warnaserror`: 0 warnings, 0 errors, 24 s.
- Unit filter `Hcm025|Wf0|Data001|Est024`: 304 passed, 0 failed, 92 s.
- Integration filter: deferred to P5 (P-2): PLAN-66 holds the acceptance stack. PR 38's run is the green baseline.

## 0.5 Duplicates and stuck outcomes
Server, read-only as the administrator (sees every tenant), tag `3bd9ee92bcf5`:

| Item | Value |
|---|---|
| `WorkflowInstances` by type and status | RecruitmentRequisition / Completed: 2 |
| Tenants with instances | 1 |
| Running duplicates per entity | 0 |
| Duplicates per idempotency key | 0 |
| Finished instance, linked entity pending: requisition, offer, establishment, competition | 0, 0, 0, 0 |

Acceptance data: taken at P5, under the stack lock.

## Row security
`hr_core_migrator` owns `WorkflowInstances` and all seven entity tables the de-duplication reads (`RecruitmentRequisitions`, `OfferLetters`, `EstablishmentChangeRequests`, `SelectionCommitteeSessions`, `RecruitmentAppeals`, `RecruitmentCompetitions`, `CompetitionResultVersions`). All eight have row security enabled and forced, with `tenant_isolation`. `hr_core_migrator` and `hr_core_app` are `NOSUPERUSER NOBYPASSRLS`.

So the entity tables are as invisible to the migrator as `WorkflowInstances`: a de-duplication that un-forces only `WorkflowInstances` would find no linked instance and keep the newest of every group. The migration un-forces all eight inside its own transaction.

`Status` is stored as text on `WorkflowInstances`, requisitions, establishment changes, competitions and result versions, and as an integer on `OfferLetters` and `RecruitmentAppeals`.

## Where the code differs from the plan
1. **`ApproveAsync` and `RejectAsync` are not inside `OutboxUnitOfWork`.** The plan and P-4 say they are. Neither the service nor any controller references it: each save commits alone. P3 adds it. This is the only wrapper, not a second one.
2. **Nested transactions in the handler chains.** `EstablishmentChangeApplier` (bulk job catalog) opens its own transaction, which throws inside the wrapper. `CodeGeneratorService` does the same on its non-sequence path. P3 makes them join.
3. **`EstablishmentChangeWorkflowHandler` ignores the result of `ApplyApprovedAsync`.** A failed apply leaves the request `Approved`, not `Applied`.
4. **`AssetAssignmentWorkflowSeeder` has a test caller:** `Msg040ClassAPublishOrderTests`. `scripts/wf001-workflow-validate.ps1` requires `CreateViaEngineOrLegacy`, and `Wf001WorkflowEngineTests` calls it.
5. **Appeal, competition and results call `WorkflowStarter` directly,** and so does `PolicyOrchestrator`, so the unique violation is handled in the starter, not only in the coordinator.
6. **`CompetitionLifecycleService.CancelAsync` leaves the instance running,** and `CompetitionWorkflowHandler` does not check the link.
7. **Competition and results are not in the inbox** (`SupportedEntityTypes`); they appear on detail panels only.
8. **`RequestedByUserId` is set only by external starts,** so it is null on every HR.Core type.

## Paths that change an instance's status
| Writer | Transition |
|---|---|
| `WorkflowInstanceActionService.ApproveAsync`, `RejectAsync` | InProgress, Completed, Rejected |
| `ExternalApprovalService.StartAsync`, `CancelAsync` | insert; Cancelled |
| `WorkflowStarter` (coordinator, `PolicyOrchestrator`, appeal, competition, results) | insert |
| `ApprovalRepairService.RunAsync` | Cancelled (orphan hire) |
| `RecruitmentRequisitionCascade.CancelWorkflowAsync` | Cancelled |
| `RecruitmentOfferService.CancelApprovalAsync` | Cancelled |
| `RecruitmentAppealService.AssignReviewerAsync` | Cancelled |
| `EstablishmentChangeLifecycleService.CancelAsync` | Cancelled |

## 0.6 Maker scenarios (P-5)
No acceptance, integration or unit scenario has a submitter who is not the creator, or a requester, succeed on any step of requisition, offer, establishment, competition or results. `central-a` creates and submits; step 1 is `br1-mgr` or `central-a2`; step 2 is `a-management-approver`. Where submitter and creator differ (AC-HCM-25h, `Hcm025ProductionApprovalIntegrationTests`, AC-HCM-31e), the submitter does not act, or is already refused. So no persona changes are expected, and AC-HCM-25 needs no change. `Hcm025ApprovalCoordinatorTests` and `Hcm025ApprovalRepairTests` were scanned, not read line by line.

Inbox fixtures `Wf023WorkInboxUiTests.SeedTwoStepRequisitionAsync` and `SeedTwoStepOfferAsync` do not link their entity, so the linkage rule needs them linked.

## Acceptance classes that act on instances (P-10)
- **prod-like:** AcHcm25ProductionApproval, AcWf43DefinitionGovernance, AcMsg40AtomicPublishing.
- **lab main:** Ac11PolicyWorkflow, AcEst15JobCatalogBulkImport, AcEst17JobCatalogMigrationLegacy, AcEst24EstablishmentWorkflowUi, AcHcm21RequisitionApprovalUi, AcHcm22RecruitmentWorkflowsUi, AcHcm28CommitteeAppeal, AcHcm29HireContract, AcHcm30CandidateExperience, AcHcm31Competition, AcWf23WorkInboxUi.
- **lab isolated:** AcHcm26AssessmentInterview, AcHcm27OfferMerit.
