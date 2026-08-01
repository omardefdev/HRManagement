# PRV-001 — اتفاقيات / مصفوفة مشاركة البيانات

## Inter-service (internal processors under same controller)

| From | To | Channel | Data | Purpose |
|---|---|---|---|---|
| HR.Core | Payroll | Events/API | EmployeeId, grade, status | Pay calculation |
| HR.Core | HR.Contracts | Events | EmployeeId, org | Hire/contract |
| HR.Contracts | Payroll | Events | Leave/separation | Deductions / final pay |
| HR.Movements | HR.Core / Payroll | Saga events | Transfer impact | Org + pay sync |
| Modules | File.Storage | Gateway + grants | FileId, holder | Attachments |
| IT.Infra | Auth.Directory | Internal API | Employee sync | Directory |
| Services | Dev.Analytics | ETL/read models | Aggregates | Dashboards |

Each flow must stay within the ROPA purpose; new fields require ROPA update + review.

## External

| Party | Data | Agreement |
|---|---|---|
| Treasury / payment channel (EXT-003) | Payment batch lines | Integration agreement + purpose limitation |
| Civil registry (EXT-002) | Identity match fields | Federation / MoU |
| Citizens (portal) | Own application data | Notice + lawful basis |

## Rule

No production export/integration without a ROPA activity id and (if external) a recorded sharing agreement reference.
