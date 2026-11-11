# Policy Domain Registry

Generated from `PolicyDomainCatalog` (`src/Shared/HRManagement.SharedKernel/Policy/PolicyDomainCatalog.cs`), the one source
of truth about the domains of the policy engine (PLAN-49, G-POL-02, owner decision D-POL-1). **Do not edit by hand.**
`Pol049PolicyDomainCatalogTests` compares this file with the catalog; after a catalog change, run that test once with
`POL049_WRITE_REGISTRY=1` to write it again. The validator, the engine, the internal enforce endpoint, the start-up check and
the policy page (`/ui/hr-core/PolicyTemplates`) read the same catalog.

## Governed domains (5)

Each has a proved call site and a seeded national template, and runs in Shadow in the Production and Staging files: the engine
records its decision in the ledger and changes no outcome (D-POL-13: no domain is in Enforce). A rule may use only the effects,
the SetField fields and the workflow codes listed here, a workflow code only at the action it is listed at, and a condition on
`action` may name only the actions listed here. A governed domain may hold no rule: `IDCard.Lifecycle` holds none since its
orphan rule was retired (PLAN-49), and a card request is then answered by the engine's default, an allow whose ledger row
names no version.

| Domain | Owner service | Owner client | Entity types and actions | Effects | SetField fields | Workflow triggers | Call sites |
|---|---|---|---|---|---|---|---|
| `Assets.Custody` | Admin.Logistics | `svc-admin-logistics` | Asset: Classify, Create; AssetAssignment: Assign | Allow, Deny, RequireEvidence, TriggerWorkflow, SetField, SelectCodePolicy | isPrivileged | AssetAssignment: ASSET-ASSIGN-APPROVAL (at Assign, started by OwnerApprovalContract) | `src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Integration/AssetClassifier.cs`<br>`src/Services/AdminLogistics/Admin.Logistics.Web/Controllers/AssetsController.cs` |
| `IDCard.Lifecycle` | IDCard | `svc-idcard` | CardRequest: Process | Allow, Deny | - | - | `src/Services/IDCard/IDCard.Web/Controllers/CardRequestsController.cs` |
| `Contracts.Leave` | HR.Contracts | `svc-hr-contracts` | LeaveRequest: Approve, Create, Update | Allow, Deny | - | - | `src/Services/HRContracts/HR.Contracts.Application/Features/LeaveRequests/LeaveRequestHandlers.cs`<br>`src/Services/HRContracts/HR.Contracts.Application/Features/InboundEvents/InboundEventCommands.cs` |
| `Core.Recruitment` | HR.Core | - (inside HR.Core) | OfferLetter: Issue; RecruitmentAppeal: FileAppeal; RecruitmentApplication: Hire; RecruitmentRequisition: Publish, PublishInternal, SubmitForApproval; SelectionCommitteeSession: SubmitCommittee | Allow, Deny, TriggerWorkflow | - | OfferLetter: RECRUITMENT-OFFER-APPROVAL (at Issue, started by HrCoreCoordinator), RECRUITMENT-OFFER-OVERRIDE-APPROVAL (at Issue, started by HrCoreCoordinator); RecruitmentAppeal: RECRUITMENT-APPEAL-REVIEW (at FileAppeal, started by HrCoreCoordinator); RecruitmentRequisition: RECRUITMENT-REQUISITION-APPROVAL (at SubmitForApproval, started by HrCoreCoordinator); SelectionCommitteeSession: RECRUITMENT-SELECTION-COMMITTEE (at SubmitCommittee, started by HrCoreCoordinator) | `src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentRequisitionLifecycleService.cs`<br>`src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentOfferService.cs`<br>`src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/SelectionCommitteeLifecycleService.cs`<br>`src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentAppealService.cs`<br>`src/Services/HRCore/HR.Core.Web/Controllers/RecruitmentApplicationsController.cs` |
| `Core.Establishment` | HR.Core | - (inside HR.Core) | EstablishmentChangeRequest: SubmitForApproval | Allow, Deny, TriggerWorkflow | - | EstablishmentChangeRequest: ESTABLISHMENT-CHANGE-APPROVAL (at SubmitForApproval, started by HrCoreCoordinator) | `src/Services/HRCore/HR.Core.Infrastructure/Services/Establishment/EstablishmentChangeLifecycleService.cs` |

## Out of scope (15)

No call site. The national template is retired by a final version with no rules (seed change set `POL-049-SCOPE-RETIRE`),
the mode is Off whatever the configuration says (a key that is not Off stops the service at start-up), the engine answers
a neutral decision with no ledger row, and a draft, a rule or a publication is refused. Bringing a domain back means a
descriptor in the catalog, a call site and a seed change set.

| Domain | Service | Reason |
|---|---|---|
| `Assets.Procurement` | Admin.Logistics | لا نقطة استدعاء: استلام التوريد تحكمه قواعد Admin.Logistics المحلية. |
| `Contracts.Advance` | HR.Contracts | لا نقطة استدعاء: السلف تحكمها قواعد HR.Contracts المحلية، وقاعدتها المبذورة كانت بأثر لا يستهلكه أحد (RequireDualApproval). |
| `Contracts.Hire` | HR.Contracts | لا نقطة استدعاء: التعيين يحكمه اعتماد خطاب العرض، ومسار HIRE-WIZARD مسحوب (PLAN-46). |
| `Contracts.Separation` | HR.Contracts | لا نقطة استدعاء: اعتماد الانفصال مسجَّل في العقد العابر للخدمات (PLAN-46، ويكمله PLAN-60)، ومسار SEPARATION-WIZARD مسحوب. |
| `Contracts.Contract` | HR.Contracts | لا نقطة استدعاء: تجديد العقد تحكمه قواعد HR.Contracts المحلية، وقاعدته المبذورة كانت بأثر لا يستهلكه أحد (RequireApproval). |
| `Movements.Transfer` | HR.Movements | لا نقطة استدعاء: مسار TRANSFER-APPROVAL مسحوب (PLAN-46)، والنقل تحكمه قواعد HR.Movements المحلية. |
| `Movements.Workforce` | HR.Movements | لا نقطة استدعاء ولا قواعد مبذورة. |
| `Core.Employment` | HR.Core | لا نقطة استدعاء ولا قواعد مبذورة. |
| `Health.Insurance` | HR.Health | لا نقطة استدعاء: قاعدته المبذورة كانت بأثر لا يستهلكه أحد (RequirePrivacyMask). |
| `Compliance.Disciplinary` | Compliance | لا نقطة استدعاء: اعتماد الإجراء التأديبي يمر بالعقد العابر للخدمات (PLAN-46) لا بقاعدة سياسة. |
| `Payroll.Period` | Payroll | لا نقطة استدعاء: قاعدته المبذورة كانت بأثر لا يستهلكه أحد (RequireDualApproval). |
| `IT.Tickets` | IT.Infra | لا نقطة استدعاء: مسار IT-TICKET مسحوب (PLAN-46). |
| `Archive.Retention` | Records.Archive | لا نقطة استدعاء: الاحتفاظ والحجز القانوني تحكمهما قواعد Records.Archive المحلية. |
| `Training.Learning` | HR.Training | لا نقطة استدعاء: قاعدته المبذورة كانت بأثر لا يستهلكه أحد (BlockUntilChecklist). |
| `Analytics.ETL` | Dev.Analytics | لا نقطة استدعاء: قاعدته المبذورة كانت بأثر لا يستهلكه أحد (RequireApproval). |

Local rule files of every service, and whether the engine governs their subject: `docs/policy-rules-inventory.csv`
(written by `scripts/policy-inventory.ps1`).
