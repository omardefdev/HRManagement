"""Extract DFD evidence from the implemented HRManagement codebase.

Produces tmp/dfd_system_evidence.json consumed by generate_current_system_dfd_drawio.py.
"""

from __future__ import annotations

import json
import re
import sys
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "src"
GATEWAY_SETTINGS = SRC / "Gateway" / "HRManagement.Gateway" / "appsettings.json"
OUTPUT = ROOT / "tmp" / "dfd_system_evidence.json"

sys.path.insert(0, str(ROOT / "scripts"))

from database_schema_parser import (  # noqa: E402
    load_all_database_models,
    scan_consumers,
    scan_http_integrations,
    scan_outbox_contexts,
)

# DbContext name -> DFD service metadata (1.0–15.0)
SERVICE_REGISTRY: dict[str, dict[str, object]] = {
    "GatewayAuditDbContext": {
        "process_id": "1.0",
        "process_num": 1,
        "p_key": "p_gw",
        "d_key": "d_gw",
        "label": "1.0 البوابة والتدقيق\nGateway",
        "db": "gateway_audit_db",
        "l2_page": 3,
        "party": None,
        "sub_process": "1.1",
    },
    "AuthDirectoryDbContext": {
        "process_id": "2.0",
        "process_num": 2,
        "p_key": "p_auth",
        "d_key": "d_auth",
        "label": "2.0 الدليل والمصادقة\nAuth.Directory",
        "db": "auth_directory_db",
        "l2_page": 4,
        "party": "Auth.Directory",
        "sub_process": "2.2",
    },
    "HRCoreDbContext": {
        "process_id": "3.0",
        "process_num": 3,
        "p_key": "p_core",
        "d_key": "d_core",
        "label": "3.0 النواة والحوكمة\nHR.Core",
        "db": "hr_core_db",
        "l2_page": 5,
        "party": "HR.Core",
        "sub_process": "3.2",
    },
    "ContractsDbContext": {
        "process_id": "4.0",
        "process_num": 4,
        "p_key": "p_con",
        "d_key": "d_con",
        "label": "4.0 العقود والخدمة\nHR.Contracts",
        "db": "hr_contracts_db",
        "l2_page": 6,
        "party": "HR.Contracts",
        "sub_process": "4.2",
    },
    "MovementsDbContext": {
        "process_id": "5.0",
        "process_num": 5,
        "p_key": "p_mov",
        "d_key": "d_mov",
        "label": "5.0 الحركات\nHR.Movements",
        "db": "hr_movements_db",
        "l2_page": 7,
        "party": "HR.Movements",
        "sub_process": "5.2",
    },
    "HealthDbContext": {
        "process_id": "6.0",
        "process_num": 6,
        "p_key": "p_hea",
        "d_key": "d_hea",
        "label": "6.0 الصحة المهنية\nHR.Health",
        "db": "hr_health_db",
        "l2_page": 8,
        "party": "HR.Health",
        "sub_process": "6.1",
    },
    "TrainingDbContext": {
        "process_id": "7.0",
        "process_num": 7,
        "p_key": "p_tra",
        "d_key": "d_tra",
        "label": "7.0 التدريب\nHR.Training",
        "db": "hr_training_db",
        "l2_page": 9,
        "party": None,
        "sub_process": "7.1",
    },
    "IDCardDbContext": {
        "process_id": "8.0",
        "process_num": 8,
        "p_key": "p_id",
        "d_key": "d_id",
        "label": "8.0 البطاقة\nIDCard",
        "db": "idcard_db",
        "l2_page": 10,
        "party": "IDCard",
        "sub_process": "8.1",
    },
    "PayrollDbContext": {
        "process_id": "9.0",
        "process_num": 9,
        "p_key": "p_pay",
        "d_key": "d_pay",
        "label": "9.0 الرواتب\nPayroll",
        "db": "payroll_db",
        "l2_page": 11,
        "party": "Payroll",
        "sub_process": "9.1",
    },
    "AdminLogisticsDbContext": {
        "process_id": "10.0",
        "process_num": 10,
        "p_key": "p_log",
        "d_key": "d_log",
        "label": "10.0 اللوجستيات\nAdmin.Logistics",
        "db": "admin_db",
        "l2_page": 12,
        "party": "Admin.Logistics",
        "sub_process": "10.3",
    },
    "FileStorageDbContext": {
        "process_id": "11.0",
        "process_num": 11,
        "p_key": "p_fil",
        "d_key": "d_fil",
        "label": "11.0 الملفات\nFile.Storage",
        "db": "file_storage_db",
        "l2_page": 13,
        "party": None,
        "sub_process": "11.3",
    },
    "ArchiveDbContext": {
        "process_id": "12.0",
        "process_num": 12,
        "p_key": "p_arc",
        "d_key": "d_arc",
        "label": "12.0 الأرشيف\nRecords.Archive",
        "db": "archive_db",
        "l2_page": 14,
        "party": "Records.Archive",
        "sub_process": "12.1",
    },
    "ComplianceDbContext": {
        "process_id": "13.0",
        "process_num": 13,
        "p_key": "p_cmp",
        "d_key": "d_cmp",
        "label": "13.0 الامتثال\nCompliance",
        "db": "compliance_db",
        "l2_page": 15,
        "party": None,
        "sub_process": "13.1",
    },
    "AnalyticsDbContext": {
        "process_id": "14.0",
        "process_num": 14,
        "p_key": "p_an",
        "d_key": "d_an",
        "label": "14.0 التحليلات\nDev.Analytics",
        "db": "analytics_db",
        "l2_page": 16,
        "party": None,
        "sub_process": "14.1",
    },
    "ITInfraDbContext": {
        "process_id": "15.0",
        "process_num": 15,
        "p_key": "p_it",
        "d_key": "d_it",
        "label": "15.0 البنية التقنية\nIT.Infra",
        "db": "it_infra_db",
        "l2_page": 17,
        "party": "IT.Infra",
        "sub_process": "15.1",
    },
}

CLUSTER_TO_CONTEXT: dict[str, str] = {
    "hr-core-cluster": "HRCoreDbContext",
    "hr-contracts-cluster": "ContractsDbContext",
    "hr-health-cluster": "HealthDbContext",
    "hr-movements-cluster": "MovementsDbContext",
    "hr-training-cluster": "TrainingDbContext",
    "payroll-cluster": "PayrollDbContext",
    "idcard-cluster": "IDCardDbContext",
    "records-archive-cluster": "ArchiveDbContext",
    "admin-logistics-cluster": "AdminLogisticsDbContext",
    "it-infra-cluster": "ITInfraDbContext",
    "dev-analytics-cluster": "AnalyticsDbContext",
    "compliance-cluster": "ComplianceDbContext",
    "auth-directory-cluster": "AuthDirectoryDbContext",
    "file-storage-cluster": "FileStorageDbContext",
}

# From HireSagaOptions / SeparationSagaOptions / TransferSagaOptions in source
SAGA_PARTIES: dict[str, list[dict[str, str]]] = {
    "hire": [
        {"party": "HR.Core", "context": "HRCoreDbContext", "sub": "3.2"},
        {"party": "Payroll", "context": "PayrollDbContext", "sub": "9.1"},
        {"party": "Auth.Directory", "context": "AuthDirectoryDbContext", "sub": "2.2"},
        {"party": "IDCard", "context": "IDCardDbContext", "sub": "8.1"},
        {"party": "Records.Archive", "context": "ArchiveDbContext", "sub": "12.1"},
        {"party": "IT.Infra", "context": "ITInfraDbContext", "sub": "15.1"},
    ],
    "separation": [
        {"party": "Payroll", "context": "PayrollDbContext", "sub": "9.1"},
        {"party": "HR.Core", "context": "HRCoreDbContext", "sub": "3.2"},
        {"party": "IDCard", "context": "IDCardDbContext", "sub": "8.1"},
        {"party": "Auth.Directory", "context": "AuthDirectoryDbContext", "sub": "2.2"},
        {"party": "Admin.Logistics", "context": "AdminLogisticsDbContext", "sub": "10.3"},
        {"party": "IT.Infra", "context": "ITInfraDbContext", "sub": "15.1"},
        {"party": "Records.Archive", "context": "ArchiveDbContext", "sub": "12.1"},
    ],
    "transfer": [
        {"party": "Payroll", "context": "PayrollDbContext", "sub": "9.1"},
        {"party": "HR.Core", "context": "HRCoreDbContext", "sub": "3.2"},
        {"party": "Auth.Directory", "context": "AuthDirectoryDbContext", "sub": "2.2"},
        {"party": "Admin.Logistics", "context": "AdminLogisticsDbContext", "sub": "10.3"},
    ],
}

PARTY_TO_CONTEXT: dict[str, str] = {
    entry["party"]: entry["context"]
    for parties in SAGA_PARTIES.values()
    for entry in parties
}

HTTP_TARGET: dict[str, tuple[str, str]] = {
    "lookup": ("HRCoreDbContext", "GET /api/lookup/**"),
    "workflow": ("HRCoreDbContext", "POST /api/workflow-instances"),
    "policy": ("HRCoreDbContext", "EnforceAsync / Evaluate"),
    "code": ("HRCoreDbContext", "Generate code"),
    "file": ("FileStorageDbContext", "RegisterReference"),
    "keycloak": ("AuthDirectoryDbContext", "Keycloak Admin REST"),
    "directory_sync": ("AuthDirectoryDbContext", "POST /api/internal/directory/sync-employee"),
}

# Known event publishers per service (from Outbox usage + codebase review)
EVENT_PUBLISHERS: dict[str, list[str]] = {
    "HRCoreDbContext": ["IEmployeeCreated", "IOrgUnitChanged"],
    "ContractsDbContext": [
        "IStartHireSaga",
        "IHireCaseCompleted",
        "ILeaveApproved",
        "IAdvanceApproved",
        "ISeparationCompleted",
        "IExecuteHireParty",
        "IExecuteSeparationParty",
    ],
    "MovementsDbContext": [
        "ITransferExecuted",
        "ISecondmentExecuted",
        "IExecuteTransferParty",
        "IExecuteSecondmentParty",
    ],
    "HealthDbContext": ["IMedicalRecommendationIssued", "IMedicalDecisionIssued"],
    "TrainingDbContext": ["ITrainingCompleted", "IMissionApproved"],
    "IDCardDbContext": ["ICardIssued"],
    "PayrollDbContext": ["IPayrollProcessed"],
    "ComplianceDbContext": ["IComplaintResolved", "IDisciplinaryActionTaken", "ILegalJudgmentIssued"],
    "AdminLogisticsDbContext": ["IAssetAssignmentApproved", "IAssetLossApproved"],
    "ArchiveDbContext": ["ITransactionArchived"],
    "AnalyticsDbContext": ["IExportJobRequested"],
    "HRCoreDbContext_export": [],  # placeholder
}

# Fix duplicate key - merge export into HRCore
EVENT_PUBLISHERS["HRCoreDbContext"].append("IExportJobRequested")
EVENT_PUBLISHERS["ContractsDbContext"].append("IExportJobRequested")
EVENT_PUBLISHERS["PayrollDbContext"].append("IExportJobRequested")
EVENT_PUBLISHERS["ComplianceDbContext"].append("IExportJobRequested")
EVENT_PUBLISHERS["ArchiveDbContext"].append("IExportJobRequested")
del EVENT_PUBLISHERS["HRCoreDbContext_export"]


def scan_yarp_routes() -> list[dict[str, object]]:
    text = GATEWAY_SETTINGS.read_text(encoding="utf-8-sig")
    data = json.loads(text)
    routes = data.get("ReverseProxy", {}).get("Routes", {})
    by_context: dict[str, dict[str, object]] = {}
    for route_name, route in routes.items():
        cluster_id = route.get("ClusterId", "")
        context = CLUSTER_TO_CONTEXT.get(cluster_id)
        if not context:
            continue
        path = route.get("Match", {}).get("Path", "")
        if "/health/upstreams/" in path:
            continue
        entry = by_context.setdefault(
            context,
            {"context": context, "cluster_id": cluster_id, "api_paths": [], "ui_paths": [], "extra_paths": []},
        )
        if path.startswith("/api/"):
            if path not in entry["api_paths"]:
                entry["api_paths"].append(path)
        elif path.startswith("/ui/"):
            if path not in entry["ui_paths"]:
                entry["ui_paths"].append(path)
        else:
            if path not in entry["extra_paths"]:
                entry["extra_paths"].append(path)
    result = []
    for context in SERVICE_REGISTRY:
        if context == "GatewayAuditDbContext":
            continue
        if context in by_context:
            row = by_context[context]
            meta = SERVICE_REGISTRY[context]
            result.append(
                {
                    **row,
                    "process_id": meta["process_id"],
                    "p_key": meta["p_key"],
                    "label": meta["label"],
                }
            )
    return sorted(result, key=lambda r: SERVICE_REGISTRY[r["context"]]["process_num"])  # type: ignore[arg-type]


def scan_export_publishers() -> dict[str, list[str]]:
    result: dict[str, list[str]] = {}
    for path in SRC.rglob("*ExportJobService.cs"):
        text = path.read_text(encoding="utf-8-sig", errors="ignore")
        if "Publish<IExportJobRequested>" not in text:
            continue
        parts = path.parts
        if "Services" not in parts:
            continue
        idx = parts.index("Services")
        folder = parts[idx + 1]
        folder_map = {
            "HRCore": "HRCoreDbContext",
            "HRContracts": "ContractsDbContext",
            "Payroll": "PayrollDbContext",
            "Compliance": "ComplianceDbContext",
            "DevAnalytics": "AnalyticsDbContext",
            "RecordsArchive": "ArchiveDbContext",
        }
        ctx = folder_map.get(folder)
        if ctx:
            result.setdefault(ctx, [])
            if "IExportJobRequested" not in result[ctx]:
                result[ctx].append("IExportJobRequested")
    return result


def build_http_s2s_flows(http_flags: dict[str, dict[str, bool]]) -> list[dict[str, str]]:
    flows: list[dict[str, str]] = []
    for context, flags in http_flags.items():
        if context not in SERVICE_REGISTRY:
            continue
        source = SERVICE_REGISTRY[context]["p_key"]
        for flag, enabled in flags.items():
            if not enabled or flag not in HTTP_TARGET:
                continue
            target_ctx, label = HTTP_TARGET[flag]
            target = SERVICE_REGISTRY[target_ctx]["p_key"]
            flows.append({"source": source, "target": target, "kind": "http", "label": label, "flag": flag})
    return flows


def build_consumer_index(bindings) -> dict[str, list[dict[str, str]]]:
    by_event: dict[str, list[dict[str, str]]] = {}
    for binding in bindings:
        by_event.setdefault(binding.event_name, []).append(
            {
                "context": binding.context,
                "database": binding.database,
                "consumer_file": binding.consumer_file,
                "p_key": SERVICE_REGISTRY.get(binding.context, {}).get("p_key", ""),
            }
        )
    return by_event


def build_event_flows(consumers_by_event: dict[str, list[dict[str, str]]]) -> dict[str, list[dict[str, str]]]:
    """Producer->bus and bus->consumer flows for L1."""
    producer_to_bus: list[dict[str, str]] = []
    bus_to_consumer: list[dict[str, str]] = []

    for context, events in EVENT_PUBLISHERS.items():
        if context not in SERVICE_REGISTRY:
            continue
        p_key = SERVICE_REGISTRY[context]["p_key"]
        unique = sorted(set(events))
        if unique:
            producer_to_bus.append(
                {
                    "source": p_key,
                    "target": "bus",
                    "label": " / ".join(unique[:4]) + (" …" if len(unique) > 4 else ""),
                    "events": unique,
                    "context": context,
                }
            )

    seen_consumer: set[tuple[str, str]] = set()
    for event_name, consumers in sorted(consumers_by_event.items()):
        for consumer in consumers:
            p_key = consumer.get("p_key", "")
            if not p_key:
                continue
            key = (event_name, p_key)
            if key in seen_consumer:
                continue
            seen_consumer.add(key)
            bus_to_consumer.append(
                {
                    "source": "bus",
                    "target": p_key,
                    "label": event_name,
                    "context": consumer["context"],
                    "consumer_file": consumer["consumer_file"],
                }
            )

    return {"producer_to_bus": producer_to_bus, "bus_to_consumer": bus_to_consumer}


def load_evidence() -> dict[str, object]:
    models = load_all_database_models()
    consumers = scan_consumers(models)
    http_flags = scan_http_integrations()
    outbox_contexts = sorted(scan_outbox_contexts())
    yarp_routes = scan_yarp_routes()
    export_publishers = scan_export_publishers()
    consumers_by_event = build_consumer_index(consumers)
    http_s2s = build_http_s2s_flows(http_flags)
    event_flows = build_event_flows(consumers_by_event)

    all_contexts = set(SERVICE_REGISTRY)
    direct_publish = sorted(all_contexts - set(outbox_contexts))

    services_list = [
        {
            "context": ctx,
            **{k: v for k, v in meta.items() if k != "context"},
        }
        for ctx, meta in sorted(SERVICE_REGISTRY.items(), key=lambda x: x[1]["process_num"])  # type: ignore[arg-type, return-value]
    ]

    return {
        "generated_from": "dfd_system_evidence.py",
        "service_count": len(SERVICE_REGISTRY),
        "services": services_list,
        "yarp_routes": yarp_routes,
        "yarp_service_count": len(yarp_routes),
        "http_integrations": http_flags,
        "http_s2s_flows": http_s2s,
        "outbox_contexts": outbox_contexts,
        "direct_publish_contexts": direct_publish,
        "export_publishers": export_publishers,
        "consumers": [
            {
                "event_name": b.event_name,
                "context": b.context,
                "database": b.database,
                "consumer_file": b.consumer_file,
                "p_key": SERVICE_REGISTRY.get(b.context, {}).get("p_key", ""),
            }
            for b in consumers
        ],
        "consumers_by_event": consumers_by_event,
        "event_publishers": EVENT_PUBLISHERS,
        "event_flows": event_flows,
        "saga_parties": SAGA_PARTIES,
        "party_to_context": PARTY_TO_CONTEXT,
    }


def main() -> None:
    evidence = load_evidence()
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(json.dumps(evidence, ensure_ascii=False, indent=2), encoding="utf-8")
    print(
        json.dumps(
            {
                "output": str(OUTPUT.relative_to(ROOT)).replace("\\", "/"),
                "yarp_services": evidence["yarp_service_count"],
                "consumers": len(evidence["consumers"]),
                "http_s2s": len(evidence["http_s2s_flows"]),
            },
            ensure_ascii=False,
        )
    )


if __name__ == "__main__":
    main()
