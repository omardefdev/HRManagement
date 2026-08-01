# Third-Party License Compatibility

**Version:** 1.0  
**Date:** 2026-06-12  
**Purpose:** Record open-source dependencies and confirm commercial redistribution compatibility.

## Summary

| Component | License | Commercial use | Notes |
|-----------|---------|----------------|-------|
| .NET / ASP.NET Core | MIT | Yes | Runtime dependency |
| PostgreSQL client (Npgsql) | PostgreSQL License | Yes | Database driver |
| Entity Framework Core | MIT | Yes | ORM |
| MassTransit | Apache-2.0 | Yes | Messaging |
| RabbitMQ.Client | Apache-2.0 / MPL-2.0 | Yes | Messaging transport |
| Keycloak | Apache-2.0 | Yes | Identity server (separate deployment) |
| YARP | MIT | Yes | API gateway |
| Serilog | Apache-2.0 | Yes | Logging |
| OpenTelemetry | Apache-2.0 | Yes | Observability |
| AdminLTE 3 | MIT | Yes | UI theme |
| Bootstrap 5 | MIT | Yes | CSS framework |
| jQuery | MIT | Yes | Client script |
| QuestPDF | Community / Commercial | Review | PDF generation — verify edition at sale time |

## Commercial product guidance

- **Permissive stack (MIT/Apache):** Safe for proprietary licensing when shipped as compiled binaries with license notices preserved.
- **Keycloak:** Deployed as separate infrastructure; Apache-2.0 allows commercial hosting.
- **QuestPDF:** Confirm Community vs Professional license before payslip PDF distribution at scale.

## Required notices in deployments

Include `LICENSE` (proprietary) plus notices for MIT/Apache components in deployment documentation or an `NOTICE.txt` file generated at release time.
