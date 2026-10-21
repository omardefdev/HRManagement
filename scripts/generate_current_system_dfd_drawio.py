"""Generate a DFD workbook that matches the implemented HRManagement system.

The approved thesis DFD (logical D1–D8) is left untouched. This workbook maps
1:1 onto the 15 services and 15 databases, and labels flows as HTTP, event, or
logical Guid reference. Live bank/government connectors that exist only as a
data model are named and marked as non-live.
"""

from __future__ import annotations

import json
import sys
from datetime import datetime, timezone
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))

from dfd_system_evidence import (  # noqa: E402
    SERVICE_REGISTRY,
    load_evidence,
)
from generate_approved_dfd_drawio import (  # noqa: E402
    BLUE,
    PAGE_HEIGHT,
    PAGE_WIDTH,
    Flow,
    Node,
    add_page,
)


OUTPUT = ROOT / "docs" / "output" / "HRManagement_DFD_Current.drawio"
VALIDATION = ROOT / "tmp" / "current_dfd_validation.json"
EVIDENCE: dict[str, object] = {}

EVENT = "#059669"
REF = "#C9784A"


def http(
    source: str,
    target: str,
    label: str,
    source_port: tuple[float, float] = (1.0, 0.5),
    target_port: tuple[float, float] = (0.0, 0.5),
    *,
    balance_key: str = "",
) -> Flow:
    return Flow(source, target, f"HTTP: {label}", source_port, target_port, color=BLUE, balance_key=balance_key)


def event(
    source: str,
    target: str,
    label: str,
    source_port: tuple[float, float] = (1.0, 0.5),
    target_port: tuple[float, float] = (0.0, 0.5),
) -> Flow:
    return Flow(source, target, f"حدث: {label}", source_port, target_port, dashed=True, color=EVENT)


def ref(
    source: str,
    target: str,
    label: str,
    source_port: tuple[float, float] = (1.0, 0.5),
    target_port: tuple[float, float] = (0.0, 0.5),
) -> Flow:
    return Flow(source, target, f"مرجع: {label}", source_port, target_port, dashed=True, color=REF)


def data(
    source: str,
    target: str,
    label: str,
    source_port: tuple[float, float] = (1.0, 0.5),
    target_port: tuple[float, float] = (0.0, 0.5),
    *,
    balance_key: str = "",
) -> Flow:
    return Flow(source, target, label, source_port, target_port, balance_key=balance_key)


SCOPE = (
    "كل خدمة تملك قاعدة PostgreSQL خاصة. لا FK عابر. الربط عبر HTTP أو حدث MassTransit أو Guid. "
    "التحكم المصاحب: TenantId وCorrelationId. الخط المتقطع الأخضر = حدث، البرتقالي = مرجع Guid، الأزرق = HTTP."
)


def services_l1() -> list[tuple[str, str, str, str]]:
    """L1 process/store keys and labels from evidence registry."""
    rows: list[tuple[str, str, str, str]] = []
    for ctx in sorted(SERVICE_REGISTRY, key=lambda c: SERVICE_REGISTRY[c]["process_num"]):  # type: ignore[arg-type, return-value]
        meta = SERVICE_REGISTRY[ctx]
        rows.append((meta["p_key"], meta["d_key"], meta["label"], meta["db"]))  # type: ignore[arg-type]
    return rows


def yarp_route_label(route: dict[str, object]) -> str:
    api = route.get("api_paths") or []
    ui = route.get("ui_paths") or []
    extra = route.get("extra_paths") or []
    parts: list[str] = []
    if api:
        parts.append(str(api[0]))
    if ui:
        parts.append(str(ui[0]))
    for path in extra:
        parts.append(str(path))
    return " و".join(parts[:2]) if parts else str(route.get("cluster_id", ""))


def build_l1_integration_flows() -> list[Flow]:
    """HTTP S2S, events, and external flows for L1 from evidence."""
    flows: list[Flow] = []
    http_s2s = EVIDENCE.get("http_s2s_flows") or []
    for index, row in enumerate(http_s2s):
        port = 0.2 + (index % 5) * 0.12
        flows.append(http(row["source"], row["target"], row["label"], (1.0, port), (0.0, port)))  # type: ignore[index]

    producer_flows = (EVIDENCE.get("event_flows") or {}).get("producer_to_bus") or []
    for index, row in enumerate(producer_flows):
        port = 0.15 + (index % 8) * 0.08
        flows.append(event(row["source"], "bus", row["label"], (1.0, port), (0.0, 0.55)))  # type: ignore[index]

    consumer_flows = (EVIDENCE.get("event_flows") or {}).get("bus_to_consumer") or []
    # Group bus->consumer by target to avoid clutter: one arrow per service with event list
    by_target: dict[str, list[str]] = {}
    for row in consumer_flows:
        by_target.setdefault(row["target"], []).append(row["label"])  # type: ignore[index]
    for index, (target, events) in enumerate(sorted(by_target.items())):
        port = 0.2 + (index % 10) * 0.07
        label = " / ".join(sorted(set(events))[:5])
        if len(events) > 5:
            label += " …"
        flows.append(event("bus", target, label, (0.5, 1.0), (0.0, port)))

    flows.extend(
        [
            http("p_fil", "a_minio", "Put/Get/Delete", (0.5, 0.0), (0.5, 1.0)),
            http("a_minio", "p_fil", "كائن الملف", (0.7, 1.0), (0.7, 0.0)),
            http("p_auth", "a_kc", "Admin REST مستخدمين وأدوار", (0.5, 0.0), (0.3, 1.0)),
        ]
    )
    return flows


def context_page(mxfile: ET.Element) -> None:
    nodes = [
        Node(
            "system",
            "process",
            "0\nمنصة HRManagement\n15 خدمة خلف البوابة YARP\nقواعد منفصلة + أحداث MassTransit + ملفات MinIO",
            1080,
            520,
            840,
            280,
        ),
        Node("a_admin", "external", "مسؤول المنصة", 80, 220, 320, 88),
        Node("a_hr", "external", "موارد بشرية مركز وفرع", 80, 560, 320, 88),
        Node("a_approver", "external", "المعتمد والمدير", 80, 900, 320, 88),
        Node("a_employee", "external", "الموظف (ESS)", 2560, 220, 320, 88),
        Node("a_applicant", "external", "المتقدم للتوظيف", 2560, 500, 320, 88),
        Node("a_supplier", "external", "المورد", 2560, 780, 320, 88),
        Node("a_keycloak", "external", "Keycloak\nمزود الهوية", 1180, 180, 300, 88),
        Node("a_minio", "external", "MinIO\nكائنات الملفات", 1580, 180, 300, 88),
        Node(
            "rule",
            "note",
            "جهات حقيقية فقط. المصرف والخزينة والوزارات موصلات مخطط لها (CAP-EXT-01) وليست قناة حية، لذلك لا تظهر هنا. "
            "كل سهم مسمى وفي اتجاه واحد. لا اتصال جهة خارجية بمخزن.",
            780,
            920,
            1440,
            72,
        ),
    ]
    flows = [
        data("a_admin", "system", "تهيئة جهة وصلاحيات منصة", (1.0, 0.5), (0.0, 0.18), balance_key="C-ADMIN-IN"),
        data("system", "a_admin", "سجل تدقيق البوابة ونتيجة التهيئة", (0.0, 0.28), (1.0, 0.72), balance_key="C-ADMIN-OUT"),
        data("a_hr", "system", "معاملات موظف وعقد وحركة وراتب", (1.0, 0.4), (0.0, 0.48), balance_key="C-HR-IN"),
        data("system", "a_hr", "حالات ومعاملات وتقارير ضمن النطاق", (0.0, 0.62), (1.0, 0.72), balance_key="C-HR-OUT"),
        data("a_approver", "system", "اعتماد سير العمل والقرارات", (1.0, 0.4), (0.0, 0.78), balance_key="C-APP-IN"),
        data("system", "a_approver", "طلب اعتماد ونتيجة القرار", (0.0, 0.88), (1.0, 0.72), balance_key="C-APP-OUT"),
        data("a_employee", "system", "إجازة وسلفة وتعلم وخدمة ذاتية", (0.0, 0.4), (1.0, 0.18), balance_key="C-EMP-IN"),
        data("system", "a_employee", "قرار وإشعار ورصيد", (1.0, 0.28), (0.0, 0.72), balance_key="C-EMP-OUT"),
        data("a_applicant", "system", "طلب توظيف ووثائق", (0.0, 0.4), (1.0, 0.42), balance_key="C-CAND-IN"),
        data("system", "a_applicant", "رقم تتبع وحالة الطلب", (1.0, 0.52), (0.0, 0.72), balance_key="C-CAND-OUT"),
        data("a_supplier", "system", "فاتورة وشحنة", (0.0, 0.4), (1.0, 0.72), balance_key="C-SUP-IN"),
        data("system", "a_supplier", "أمر توريد ونتيجة استلام", (1.0, 0.82), (0.0, 0.72), balance_key="C-SUP-OUT"),
        http("a_keycloak", "system", "OIDC رمز وجمهور", (0.5, 1.0), (0.42, 0.0), balance_key="C-KC-IN"),
        http("system", "a_keycloak", "إعادة توجيه المصادقة", (0.52, 0.0), (0.7, 1.0), balance_key="C-KC-OUT"),
        http("system", "a_minio", "Put/Get/Delete object", (0.62, 0.0), (0.4, 1.0), balance_key="C-MINIO-IN"),
        http("a_minio", "system", "بايتات الملف", (0.6, 1.0), (0.72, 0.0), balance_key="C-MINIO-OUT"),
    ]
    add_page(
        mxfile,
        1,
        "01 - مخطط السياق",
        "مخطط سياق النظام الحالي (DFD Context)",
        "حدود المنصة كما هي منفذة: مستخدمون + Keycloak + MinIO. المصرف والوزارات خارج الحدود الحية.",
        nodes,
        flows,
        height=1180,
        scope_note=None,
    )


def level_one_page(mxfile: ET.Element) -> None:
    services = services_l1()
    yarp_routes = EVIDENCE.get("yarp_routes") or []
    nodes = [
        Node("a_user", "external", "مستخدمو الواجهة", 80, 140, 240, 70),
        Node("a_kc", "external", "Keycloak", 360, 140, 200, 70),
        Node("a_minio", "external", "MinIO", 600, 140, 200, 70),
        Node("bus", "interface", "RabbitMQ / MassTransit", 2280, 140, 280, 70),
        Node("yarp", "interface", "YARP /api و/ui + JWT", 920, 140, 280, 70),
        Node(
            "legend",
            "note",
            f"كل مستطيل خدمة = عملية DFD ومستطيل البيانات تحتها = قاعدتها فقط. "
            f"YARP يوجّه {len(yarp_routes)} عنقوداً خلفياً + البوابة.",
            1240,
            140,
            980,
            70,
        ),
    ]
    flows: list[Flow] = [
        http("a_user", "yarp", "طلبات UI/API", (1.0, 0.5), (0.0, 0.5)),
        http("yarp", "a_user", "صفحات واستجابات", (0.0, 0.7), (1.0, 0.7)),
        http("a_kc", "yarp", "OIDC", (1.0, 0.5), (0.0, 0.35)),
        http("yarp", "a_kc", "تحدي المصادقة", (0.2, 0.0), (0.5, 1.0)),
    ]
    for index, (pkey, dkey, plabel, dlabel) in enumerate(services):
        col = index % 5
        row = index // 5
        x = 80 + col * 580
        y = 260 + row * 380
        nodes.append(Node(pkey, "process", plabel, x, y, 340, 96))
        nodes.append(Node(dkey, "store", dlabel, x + 20, y + 210, 300, 70))
        flows.append(data(pkey, dkey, "كتابة مجال الخدمة", (0.38, 1.0), (0.38, 0.0)))
        flows.append(data(dkey, pkey, "قراءة مجال الخدمة", (0.66, 0.0), (0.66, 1.0)))

    flows.append(http("yarp", "p_gw", "كل /api و/ui تمر من هنا ثم تُوجَّه", (0.5, 1.0), (0.5, 0.0)))
    for index, route in enumerate(yarp_routes):
        p_key = route["p_key"]
        label = yarp_route_label(route)
        port = 0.15 + (index % 14) * 0.05
        flows.append(http("p_gw", p_key, label, (1.0, port), (0.0, port)))  # type: ignore[arg-type]
    flows.append(
        http("p_core", "yarp", "lookup/workflow/policy/codes تستضيفها النواة", (0.5, 0.0), (0.7, 1.0))
    )
    flows.extend(build_l1_integration_flows())

    add_page(
        mxfile,
        2,
        "02 - المستوى الأول: خمس عشرة خدمة",
        "المستوى الأول — عملية لكل خدمة وقاعدة لكل خدمة",
        "تدفقات YARP وHTTP S2S والأحداث مُستخرجة من appsettings.json وMassTransit consumers.",
        nodes,
        flows,
        height=1480,
        scope_note=SCOPE,
    )


def gateway_page(mxfile: ET.Element) -> None:
    nodes = [
        Node("a_user", "external", "مستخدم المتصفح", 80, 360, 240, 80),
        Node("a_kc", "external", "Keycloak", 80, 560, 240, 80),
        Node("a_svc", "external", "عناقيد الخدمات الخمس عشرة", 2480, 360, 400, 80),
        Node("a_alert", "external", "alert-sink (Webhook)", 2480, 720, 400, 80),
        Node("p1", "process", "1.1 مصادقة OIDC على البوابة", 420, 340, 340, 100),
        Node("p2", "process", "1.2 توجيه YARP + تمرير JWT", 860, 340, 340, 100),
        Node("p3", "process", "1.3 كتابة سجل التدقيق", 1300, 340, 340, 100),
        Node("p4", "process", "1.4 فحص صحة العناقيد", 1740, 340, 340, 100),
        Node("d1", "store", "gateway_audit_entries", 420, 720, 300, 80),
        Node("d2", "store", "security_audit_events", 900, 720, 300, 80),
        Node("d3", "store", "EvidenceArtifacts / EvidenceRecords", 1380, 720, 360, 80),
        Node("d4", "store", "gateway_audit_chain_state", 1820, 720, 300, 80),
    ]
    flows = [
        http("a_user", "p1", "طلب /ui أو /api", (1.0, 0.4), (0.0, 0.4)),
        http("a_kc", "p1", "رمز OIDC", (1.0, 0.5), (0.0, 0.7)),
        http("p1", "p2", "جلسة وجمهور JWT", (1.0, 0.5), (0.0, 0.5)),
        http("p2", "a_svc", "/api/{خدمة} و/ui/{خدمة} و/health", (1.0, 0.5), (0.0, 0.5)),
        data("p2", "p3", "Path وUserSub وCorrelationId بعد التوجيه", (1.0, 0.5), (0.0, 0.5)),
        data("p3", "d1", "قيد تدقيق لاحق فقط", (0.38, 1.0), (0.38, 0.0)),
        data("p3", "d2", "حدث أمني BulkLookup وغيره", (0.7, 1.0), (0.3, 0.0)),
        data("p3", "d4", "سلسلة التجزئة Sequence", (1.0, 0.8), (0.3, 0.0)),
        http("p4", "a_svc", "GET /health عبر عنوان العنقود", (1.0, 0.5), (0.0, 0.7)),
        data("p3", "d3", "واجهة أدلة مستضافة؛ لا عميل من خدمات أخرى", (0.5, 1.0), (0.5, 0.0)),
        http("p3", "a_alert", "SIEM POST /webhook", (1.0, 0.8), (0.0, 0.5)),
        ref("p3", "a_kc", "UserSub / ActorSub = claim sub", (0.0, 0.8), (1.0, 0.8)),
    ]
    add_page(
        mxfile,
        3,
        "03 - البوابة والتدقيق",
        "تفكيك 1.0 — Gateway وgateway_audit_db",
        "لا MassTransit ولا Outbox. لا lookup من البوابة: YARP يمرّر فقط. الأدلة على قرص محلي لا MinIO.",
        nodes,
        flows,
        height=980,
        scope_note=SCOPE,
    )


def auth_page(mxfile: ET.Element) -> None:
    nodes = [
        Node("bus", "interface", "RabbitMQ", 80, 360, 240, 80),
        Node("a_kc", "external", "Keycloak Admin REST", 80, 560, 240, 80),
        Node("p_it", "process", "15.1 مزامنة موظف (IT.Infra)", 80, 760, 240, 90),
        Node("p1", "process", "2.1 استهلاك أحداث الموظف/الفصل/النقل", 420, 340, 400, 100),
        Node("p2", "process", "2.2 إنشاء أو تعطيل حساب الدليل", 920, 340, 400, 100),
        Node("p3", "process", "2.3 تهيئة مستخدم Keycloak", 1420, 340, 400, 100),
        Node("p4", "process", "2.4 ACK طرف التعيين/الفصل", 1920, 340, 400, 100),
        Node("d1", "store", "AuthUsers", 420, 720, 280, 80),
        Node("d2", "store", "DirectoryEntries", 820, 720, 280, 80),
        Node("d3", "store", "Roles / UserRoles", 1220, 720, 280, 80),
        Node("d_core", "store", "hr_core_db.Employee (مرجع)", 1680, 720, 340, 80),
    ]
    flows = [
        event("bus", "p1", "IEmployeeCreated / ISeparationCompleted / ITransferExecuted / IExecuteHireParty", (1.0, 0.5), (0.0, 0.5)),
        data("p1", "p2", "EmployeeId ووحدة النطاق", (1.0, 0.5), (0.0, 0.5)),
        data("p2", "d1", "AuthUser", (0.3, 1.0), (0.3, 0.0)),
        data("p2", "d2", "DirectoryEntry", (0.6, 1.0), (0.4, 0.0)),
        data("p2", "p3", "KeycloakUserId مطلوب", (1.0, 0.5), (0.0, 0.5)),
        http("p3", "a_kc", "users / roles / enable", (0.0, 0.6), (1.0, 0.4)),
        ref("p2", "d_core", "EmployeeId / ScopeOrgUnitId بلا FK", (0.8, 1.0), (0.3, 0.0)),
        event("p4", "bus", "IHirePartyCompleted / ISeparationPartyCompleted", (0.5, 0.0), (0.7, 1.0)),
        http("p_it", "p2", "POST /api/internal/directory/sync-employee", (1.0, 0.4), (0.0, 0.85)),
        data("p2", "d3", "ربط الأدوار", (0.9, 1.0), (0.3, 0.0)),
    ]
    add_page(
        mxfile,
        4,
        "04 - الدليل والمصادقة",
        "تفكيك 2.0 — Auth.Directory وauth_directory_db",
        "بلا Outbox. KeycloakUserId مرجع HTTP لا FK. لا lookup إلى النواة.",
        nodes,
        flows,
        height=980,
        scope_note=SCOPE,
    )


def core_page(mxfile: ET.Element) -> None:
    nodes = [
        Node("a_hr", "external", "موارد بشرية / منصة", 80, 300, 260, 80),
        Node("bus", "interface", "RabbitMQ", 80, 500, 260, 80),
        Node("p_gw", "process", "1.2 توجيه YARP (Gateway)", 80, 700, 260, 80),
        Node("p1", "process", "3.1 إدارة الجهة والهيكل المؤرخ", 420, 280, 360, 96),
        Node("p2", "process", "3.2 إدارة الموظف والملف", 860, 280, 360, 96),
        Node("p3", "process", "3.3 إنفاذ السياسة وتوليد الرمز", 1300, 280, 360, 96),
        Node("p4", "process", "3.4 سير العمل والتفويض", 1740, 280, 360, 96),
        Node("p5", "process", "3.5 lookup للخدمات الأخرى", 2180, 280, 360, 96),
        Node("d1", "store", "Tenants / OrgUnits / OrgUnitVersion", 420, 680, 360, 80),
        Node("d2", "store", "Employees / EmploymentRecords", 860, 680, 360, 80),
        Node("d3", "store", "Policy* / CodeIssuance", 1300, 680, 360, 80),
        Node("d4", "store", "WorkflowDefinition / Instance", 1740, 680, 360, 80),
        Node("d5", "store", "Outbox / FileReferenceOutbox", 2180, 680, 360, 80),
        Node("d_files", "store", "file_storage_db (مرجع)", 2180, 840, 360, 70),
    ]
    flows = [
        http("p_gw", "p1", "/ui/hr-core وإسناد الجهة", (1.0, 0.3), (0.0, 0.7)),
        data("a_hr", "p1", "بيان جهة وهيكل", (1.0, 0.4), (0.0, 0.4)),
        data("p1", "d1", "Tenant وOrgUnit", (0.38, 1.0), (0.38, 0.0)),
        data("p1", "p2", "نطاق الوحدة للموظف", (1.0, 0.5), (0.0, 0.5)),
        data("p2", "d2", "Employee", (0.38, 1.0), (0.38, 0.0)),
        event("p2", "bus", "IEmployeeCreated / IOrgUnitChanged", (0.0, 0.6), (1.0, 0.4)),
        http("p3", "d3", "Evaluate / Enforce / Generate", (0.38, 1.0), (0.38, 0.0)),
        http("p5", "p3", "عملاء السياسة والرموز من الخدمات", (0.0, 0.5), (1.0, 0.5)),
        http("p5", "p2", "GET /api/lookup/employees|org-units", (0.0, 0.3), (1.0, 0.7)),
        data("p4", "d4", "بدء وموافقة مثيل", (0.38, 1.0), (0.38, 0.0)),
        data("p2", "d5", "Outbox أحداث + مراجع ملفات", (1.0, 0.8), (0.0, 0.3)),
        http("p2", "d_files", "FileReferenceOutbox ثم RegisterReference", (0.8, 1.0), (0.3, 0.0)),
        ref("p2", "d_files", "PhotoFileId / Document.FileId", (0.6, 1.0), (0.2, 0.0)),
    ]
    add_page(
        mxfile,
        5,
        "05 - النواة والحوكمة",
        "تفكيك 3.0 — HR.Core وhr_core_db",
        "النواة تستضيف lookup والسياسة وسير العمل والرموز. Outbox موجود. RelatedEmployeeId في الديوان FK داخلي.",
        nodes,
        flows,
        height=1040,
        scope_note=SCOPE,
    )


def contracts_page(mxfile: ET.Element) -> None:
    nodes = [
        Node("a_hr", "external", "موارد بشرية / موظف ESS", 80, 320, 280, 80),
        Node("bus", "interface", "RabbitMQ", 80, 540, 280, 80),
        Node("p_core", "process", "3.3 سياسة ورموز (HR.Core)", 80, 760, 280, 80),
        Node("p1", "process", "4.1 استقبال مرشح وتعيين", 460, 280, 340, 96),
        Node("p2", "process", "4.2 عقد ومباشرة وHire Saga", 900, 280, 340, 96),
        Node("p3", "process", "4.3 إجازة وسلفة (سياسة ثم سير عمل)", 1340, 280, 380, 96),
        Node("p4", "process", "4.4 إنهاء خدمة وSeparation Saga", 1820, 280, 380, 96),
        Node("d1", "store", "Candidates / HireCases", 460, 680, 300, 80),
        Node("d2", "store", "Contracts / ServiceTransactions", 900, 680, 320, 80),
        Node("d3", "store", "LeaveRequests / Advances", 1340, 680, 320, 80),
        Node("d4", "store", "SeparationRecords / Checklists", 1820, 680, 340, 80),
        Node("d_out", "store", "Outbox", 2280, 680, 240, 80),
    ]
    flows = [
        data("a_hr", "p1", "طلب توظيف أو مباشرة", (1.0, 0.4), (0.0, 0.4)),
        data("p1", "d1", "مرشح وحالة", (0.38, 1.0), (0.38, 0.0)),
        data("p1", "p2", "قبول مرشح", (1.0, 0.5), (0.0, 0.5)),
        data("p2", "d2", "عقد ومعاملة خدمة", (0.38, 1.0), (0.38, 0.0)),
        event("p2", "bus", "IStartHireSaga / IHireCaseCompleted / IExecuteHireParty", (0.0, 0.7), (1.0, 0.35)),
        data("a_hr", "p3", "طلب إجازة أو سلفة", (1.0, 0.7), (0.0, 0.35)),
        http("p3", "p_core", "EnforceAsync إجازة + Generate رمز", (0.0, 0.7), (1.0, 0.4)),
        data("p3", "d3", "طلب وأثر", (0.38, 1.0), (0.38, 0.0)),
        event("p3", "bus", "ILeaveApproved / IAdvanceApproved", (0.0, 0.5), (1.0, 0.55)),
        data("a_hr", "p4", "سبب إنهاء الخدمة", (1.0, 0.9), (0.0, 0.35)),
        data("p4", "d4", "سجل فصل وقائمة إغلاق", (0.38, 1.0), (0.38, 0.0)),
        event("p4", "bus", "IExecuteSeparationParty / ISeparationCompleted", (0.0, 0.5), (1.0, 0.75)),
        event("bus", "p2", "ITransactionArchived / party ACK", (1.0, 0.5), (0.0, 0.85)),
        event("bus", "p4", "party ACK من الأطراف", (1.0, 0.7), (0.0, 0.85)),
        data("p2", "d_out", "رسائل موثوقة", (1.0, 0.8), (0.2, 0.0)),
        ref("p1", "p_core", "EmployeeId بعد الإنشاء", (0.0, 0.3), (1.0, 0.2)),
    ]
    add_page(
        mxfile,
        6,
        "06 - العقود والخدمة",
        "تفكيك 4.0 — HR.Contracts وhr_contracts_db",
        "Hire Saga وSeparation Saga منفذتان. الإجازة تستدعي السياسة في النواة. الأرشيف يستهلك الإجازة/السلفة وينشر ITransactionArchived.",
        nodes,
        flows,
        height=1000,
        scope_note=SCOPE,
    )


def movements_page(mxfile: ET.Element) -> None:
    nodes = [
        Node("a_hr", "external", "فرع مصدر / فرع هدف", 80, 380, 280, 80),
        Node("bus", "interface", "RabbitMQ", 80, 600, 280, 80),
        Node("p1", "process", "5.1 تسجيل نقل أو ندب أو إعارة", 460, 320, 380, 100),
        Node("p2", "process", "5.2 ساغا النقل/الندب وتنفيذ اللقطة", 960, 320, 400, 100),
        Node("p3", "process", "5.3 نشر التنفيذ وأطراف العملية", 1480, 320, 400, 100),
        Node("d1", "store", "TransferRequests / SecondmentRequests", 460, 700, 400, 80),
        Node("d2", "store", "LoanAssignmentRequests / Snapshots", 980, 700, 400, 80),
        Node("d3", "store", "Outbox", 1500, 700, 280, 80),
        Node("d_core", "store", "hr_core_db.OrgUnit (مرجع)", 1900, 700, 360, 80),
    ]
    flows = [
        data("a_hr", "p1", "طلب حركة", (1.0, 0.5), (0.0, 0.5)),
        data("p1", "d1", "طلب وحالة", (0.38, 1.0), (0.38, 0.0)),
        data("p1", "p2", "قبول الهدف", (1.0, 0.5), (0.0, 0.5)),
        data("p2", "d2", "لقطة تنفيذ", (0.38, 1.0), (0.38, 0.0)),
        event("p2", "p3", "تنفيذ معتمد", (1.0, 0.5), (0.0, 0.5)),
        event("p3", "bus", "ITransferExecuted / ISecondmentExecuted / ILoanAssignment* / IExecute*Party", (0.0, 0.6), (1.0, 0.5)),
        data("p3", "d3", "Outbox", (0.5, 1.0), (0.5, 0.0)),
        ref("p1", "d_core", "FromOrgUnitId / ToOrgUnitId بلا FK", (1.0, 0.8), (0.2, 0.0)),
    ]
    add_page(
        mxfile,
        7,
        "07 - الحركات",
        "تفكيك 5.0 — HR.Movements وhr_movements_db",
        "لا HTTP lookup/files مستخدم. السياسة مسجّلة وغير مستدعاة. أطراف النقل: رواتب ونواة ودليل ولوجستيات.",
        nodes,
        flows,
        height=980,
        scope_note=SCOPE,
    )


def health_page(mxfile: ET.Element) -> None:
    nodes = [
        Node("a_staff", "external", "كادر صحي / طبيب", 80, 360, 280, 80),
        Node("bus", "interface", "RabbitMQ", 80, 560, 280, 80),
        Node("p1", "process", "6.1 ملف طبي وتوصية/قرار", 460, 320, 400, 100),
        Node("d1", "store", "hr_health_db", 460, 680, 360, 80),
        Node("d_core", "store", "hr_core_db (مرجع EmployeeId)", 960, 680, 360, 80),
    ]
    flows = [
        data("a_staff", "p1", "إحالة وفحوص", (1.0, 0.5), (0.0, 0.5)),
        data("p1", "d1", "MedicalRecord / Recommendation", (0.38, 1.0), (0.38, 0.0)),
        event("p1", "bus", "IMedicalRecommendationIssued / IMedicalDecisionIssued", (0.0, 0.6), (1.0, 0.4)),
        ref("p1", "d_core", "EmployeeId / PrimaryOrgUnitId", (1.0, 0.8), (0.2, 0.0)),
    ]
    add_page(
        mxfile,
        8,
        "08 - الصحة المهنية",
        "تفكيك 6.0 — HR.Health وhr_health_db",
        "بلا HTTP S2S مستخدم. بلا Outbox. أحداث طبية فقط.",
        nodes,
        flows,
        height=980,
        scope_note=SCOPE,
    )


def training_page(mxfile: ET.Element) -> None:
    nodes = [
        Node("a_staff", "external", "مسؤول تدريب", 80, 360, 280, 80),
        Node("bus", "interface", "RabbitMQ", 80, 560, 280, 80),
        Node("p_fil", "process", "11.3 RegisterReference (File.Storage)", 80, 760, 300, 90),
        Node("p1", "process", "7.1 ترشيح وتدريب وشهادة", 460, 320, 400, 100),
        Node("d1", "store", "hr_training_db", 460, 680, 360, 80),
        Node("d_f", "store", "file_storage_db (مرجع)", 960, 680, 360, 80),
        Node("d_core", "store", "hr_core_db (مرجع)", 960, 820, 360, 70),
    ]
    flows = [
        data("a_staff", "p1", "خطة وترشيح", (1.0, 0.5), (0.0, 0.5)),
        data("p1", "d1", "Nomination / TrainingResult", (0.38, 1.0), (0.38, 0.0)),
        event("p1", "bus", "ITrainingCompleted / IMissionApproved", (0.0, 0.6), (1.0, 0.4)),
        http("p1", "p_fil", "RegisterReference CertificateFileId", (1.0, 0.5), (0.0, 0.4)),
        ref("p1", "d_f", "CertificateFileId / ContentFileId", (1.0, 0.8), (0.2, 0.0)),
        ref("p1", "d_core", "EmployeeId / OrgUnitId", (0.0, 0.3), (1.0, 0.2)),
    ]
    add_page(
        mxfile,
        9,
        "09 - التدريب",
        "تفكيك 7.0 — HR.Training وhr_training_db",
        "HTTP file فقط لربط الشهادة. Outbox موجود.",
        nodes,
        flows,
        height=1000,
        scope_note=SCOPE,
    )


def idcard_page(mxfile: ET.Element) -> None:
    nodes = [
        Node("a_staff", "external", "موظف إصدار بطاقة", 80, 360, 280, 80),
        Node("bus", "interface", "RabbitMQ", 80, 560, 280, 80),
        Node("p_core", "process", "3.3 سياسة ورموز (HR.Core)", 80, 760, 280, 90),
        Node("p_fil", "process", "11.3 RegisterReference (File.Storage)", 80, 920, 300, 90),
        Node("p1", "process", "8.1 طلب بطاقة وإصدارها", 460, 320, 400, 100),
        Node("d1", "store", "idcard_db", 460, 680, 360, 80),
        Node("d_f", "store", "file_storage_db (مرجع)", 960, 680, 360, 80),
    ]
    flows = [
        data("a_staff", "p1", "طلب بطاقة", (1.0, 0.5), (0.0, 0.5)),
        http("p1", "p_core", "EnforceAsync + GetSnapshot + IdCardSerial", (0.0, 0.7), (1.0, 0.4)),
        data("p1", "d1", "CardRequest / IssuedCard", (0.38, 1.0), (0.38, 0.0)),
        event("p1", "bus", "ICardIssued (إنشاء/تجديد)", (0.0, 0.5), (1.0, 0.6)),
        http("p1", "p_fil", "RegisterReference إيصال/ختم", (1.0, 0.5), (0.0, 0.4)),
        ref("p1", "d_f", "ReceiptFileId / SealFileId", (1.0, 0.8), (0.2, 0.0)),
        event("bus", "p1", "IExecuteHireParty / party ACK", (1.0, 0.5), (0.0, 0.85)),
    ]
    add_page(
        mxfile,
        10,
        "10 - البطاقة",
        "تفكيك 8.0 — IDCard وidcard_db",
        "ICardIssued من الإصدار لا ProcessRequest. Outbox موجود.",
        nodes,
        flows,
        height=1080,
        scope_note=SCOPE,
    )


def payroll_page(mxfile: ET.Element) -> None:
    nodes = [
        Node("a_op", "external", "مشغل رواتب ومعتمد", 80, 320, 280, 80),
        Node("bus", "interface", "RabbitMQ", 80, 540, 280, 80),
        Node("p_core", "process", "3.5 lookup (HR.Core)", 80, 760, 280, 80),
        Node("p1", "process", "9.1 تجميد لقطة المدخلات", 460, 280, 340, 96),
        Node("p2", "process", "9.2 حساب مقسّم قابل للاستئناف", 900, 280, 360, 96),
        Node("p3", "process", "9.3 اعتماد وترحيل وصرف", 1360, 280, 340, 96),
        Node("p4", "process", "9.4 أمر دفع وتسوية داخل النموذج", 1800, 280, 400, 96),
        Node("d1", "store", "PayrollConfiguration / InputSnapshot", 460, 680, 360, 80),
        Node("d2", "store", "RunJob / Partitions / SalaryLines", 920, 680, 360, 80),
        Node("d3", "store", "PayrollRun / PaymentBatch", 1380, 680, 320, 80),
        Node("d4", "store", "PaymentOrder / Arrears / Hold", 1800, 680, 360, 80),
        Node("d_out", "store", "Outbox", 2260, 680, 240, 80),
    ]
    flows = [
        event("bus", "p1", "عقد/إجازة/تدريب/حركة/حضور/طبي/فصل طرف", (1.0, 0.4), (0.0, 0.4)),
        http("p1", "p_core", "GetEmployeesByIds", (0.0, 0.7), (1.0, 0.4)),
        data("a_op", "p1", "بدء دورة", (1.0, 0.4), (0.0, 0.55)),
        data("p1", "d1", "بصمة مدخلات", (0.38, 1.0), (0.38, 0.0)),
        data("p1", "p2", "لقطة معتمدة", (1.0, 0.5), (0.0, 0.5)),
        data("p2", "d2", "وظائف وأجزاء وبنود", (0.38, 1.0), (0.38, 0.0)),
        data("p2", "p3", "نتيجة حساب", (1.0, 0.5), (0.0, 0.5)),
        data("a_op", "p3", "اعتماد وترحيل", (1.0, 0.7), (0.0, 0.35)),
        data("p3", "d3", "مسير ودفعة", (0.38, 1.0), (0.38, 0.0)),
        event("p3", "bus", "IPayrollProcessed", (0.0, 0.5), (1.0, 0.65)),
        data("p3", "p4", "صرف داخل المنصة", (1.0, 0.5), (0.0, 0.5)),
        data("p4", "d4", "أمر دفع ومتأخرات وحجز", (0.38, 1.0), (0.38, 0.0)),
        data("p3", "d_out", "Outbox", (1.0, 0.8), (0.2, 0.0)),
        ref("p1", "p_core", "EmployeeId / OrgUnitId", (0.0, 0.3), (1.0, 0.2)),
    ]
    add_page(
        mxfile,
        11,
        "11 - الرواتب",
        "تفكيك 9.0 — Payroll وpayroll_db",
        "قناة المصرف الحية غير متكاملة (CAP-PAY-05 / CAP-EXT-01). أوامر الدفع والتسوية كيانات داخل القاعدة. ISeparationCompleted المستهلك فارغ؛ الفصل عبر IExecuteSeparationParty فقط.",
        nodes,
        flows,
        height=1000,
        scope_note=SCOPE,
    )


def logistics_page(mxfile: ET.Element) -> None:
    nodes = [
        Node("a_sup", "external", "مورد / أمين مستودع", 80, 300, 280, 80),
        Node("a_hr", "external", "مستلم العهدة", 80, 500, 280, 80),
        Node("bus", "interface", "RabbitMQ", 80, 700, 280, 80),
        Node("p_core", "process", "3.5 lookup/workflow/policy/codes (HR.Core)", 80, 880, 280, 90),
        Node("p1", "process", "10.1 أمر توريد واستلام", 460, 280, 340, 96),
        Node("p2", "process", "10.2 مخزون أو أصل ورمز وسلسلة", 900, 280, 360, 96),
        Node("p3", "process", "10.3 عهدة واعتماد وتسليم", 1360, 280, 340, 96),
        Node("p4", "process", "10.4 إرجاع أو فقد", 1800, 280, 320, 96),
        Node("d1", "store", "SupplyOrders / Receipts", 460, 680, 320, 80),
        Node("d2", "store", "Inventory / Assets", 900, 680, 300, 80),
        Node("d3", "store", "AssetAssignments", 1360, 680, 300, 80),
        Node("d4", "store", "AssetLossReports", 1800, 680, 300, 80),
        Node("p_fil", "process", "11.3 RegisterReference (File.Storage)", 2240, 280, 280, 96),
        Node("d_f", "store", "file_storage_db", 2240, 680, 280, 80),
    ]
    flows = [
        data("a_sup", "p1", "شحنة وفاتورة", (1.0, 0.4), (0.0, 0.4)),
        data("p1", "d1", "أمر وبنود واستلام", (0.38, 1.0), (0.38, 0.0)),
        data("p1", "p2", "بند مقبول", (1.0, 0.5), (0.0, 0.5)),
        http("p2", "p_core", "توليد رمز أصل", (0.0, 0.8), (1.0, 0.3)),
        data("p2", "d2", "مخزون/أصل وLineage", (0.38, 1.0), (0.38, 0.0)),
        data("a_hr", "p3", "طلب عهدة", (1.0, 0.5), (0.0, 0.4)),
        http("p3", "p_core", "lookup موظف + بدء اعتماد + تصنيف مميز", (0.0, 0.7), (1.0, 0.5)),
        data("p3", "d3", "تعيين", (0.38, 1.0), (0.38, 0.0)),
        event("p3", "bus", "IAssetAssignmentApproved", (0.0, 0.5), (1.0, 0.4)),
        http("p3", "p_fil", "RegisterReference HandoverFileId", (1.0, 0.5), (0.0, 0.4)),
        data("p3", "p4", "أصل مسلّم", (1.0, 0.5), (0.0, 0.5)),
        data("p4", "d4", "فقد عند الحاجة", (0.38, 1.0), (0.38, 0.0)),
        event("p4", "bus", "IAssetLossApproved", (0.0, 0.5), (1.0, 0.65)),
        http("p4", "p_fil", "LossFileId / ReturnFileId", (1.0, 0.5), (0.0, 0.65)),
        ref("p3", "p_core", "EmployeeId / RequestedBy / ReceivedBy", (0.0, 0.3), (1.0, 0.2)),
        data("p_fil", "d_f", "ملف ومرجع حائز", (0.38, 1.0), (0.38, 0.0)),
        event("bus", "p3", "فصل/نقل يعيد العهد", (1.0, 0.5), (0.0, 0.85)),
    ]
    add_page(
        mxfile,
        12,
        "12 - اللوجستيات",
        "تفكيك 10.0 — Admin.Logistics وadmin_db",
        "بلا Outbox. HTTP lookup/workflow/policy/codes والملفات مستخدمة. العهدة حائز واحد.",
        nodes,
        flows,
        height=1100,
        scope_note=SCOPE,
    )


def files_page(mxfile: ET.Element) -> None:
    nodes = [
        Node("a_minio", "external", "MinIO", 80, 360, 240, 80),
        Node("a_svc", "external", "خدمات حائزة على FileId", 80, 560, 260, 90),
        Node("p1", "process", "11.1 رفع وحجر وفحص", 420, 280, 360, 96),
        Node("p2", "process", "11.2 ترقية ومنحة وصول مؤقت", 860, 280, 380, 96),
        Node("p3", "process", "11.3 RegisterReference / حذف مرجع", 1340, 280, 420, 96),
        Node("d_f", "store", "Files / FileReferences / Grants", 720, 680, 420, 80),
    ]
    flows = [
        data("a_svc", "p1", "محتوى ملف عبر البوابة", (1.0, 0.3), (0.0, 0.4)),
        data("p1", "p2", "نتيجة فحص", (1.0, 0.5), (0.0, 0.5)),
        http("p1", "a_minio", "Put حجر", (0.0, 0.5), (1.0, 0.4)),
        http("p2", "a_minio", "نقل/قراءة كائن نظيف", (0.0, 0.5), (1.0, 0.6)),
        data("p2", "d_f", "FileMetadata وGrant", (0.3, 1.0), (0.3, 0.0)),
        http("a_svc", "p3", "POST/DELETE /api/files/{id}/references", (1.0, 0.5), (0.0, 0.4)),
        data("p3", "d_f", "FileReferences + HolderKey", (0.7, 1.0), (0.5, 0.0)),
    ]
    add_page(
        mxfile,
        13,
        "13 - الملفات",
        "تفكيك 11.0 — File.Storage وfile_storage_db",
        "بلا أحداث MassTransit. MinIO للكائنات. ClamAV/CDR الحي غير متحقق (CAP-FILE-01).",
        nodes,
        flows,
        height=980,
        scope_note=SCOPE,
    )


def archive_page(mxfile: ET.Element) -> None:
    nodes = [
        Node("bus", "interface", "RabbitMQ", 80, 360, 260, 80),
        Node("p1", "process", "12.1 أرشفة من الأحداث", 420, 280, 380, 96),
        Node("p2", "process", "12.2 احتفاظ وإتلاف وحيازة", 920, 280, 380, 96),
        Node("p_codes", "process", "3.3 توليد رمز أرشفة (HR.Core)", 1420, 280, 360, 96),
        Node("p_fil", "process", "11.3 RegisterReference (File.Storage)", 1420, 480, 360, 90),
        Node("d_a", "store", "ArchiveItems / DigitalFileRecords", 620, 680, 440, 80),
        Node("d_f", "store", "file_storage_db (مرجع)", 1420, 680, 360, 80),
    ]
    flows = [
        event("bus", "p1", "IHireCaseCompleted / ILeaveApproved / ISeparationCompleted / IComplaintResolved / IMedical*", (1.0, 0.5), (0.0, 0.4)),
        data("p1", "d_a", "ArchiveItem (EmployeeId في العنوان فقط)", (0.38, 1.0), (0.38, 0.0)),
        http("p1", "p_codes", "Generate ArchiveReference", (1.0, 0.5), (0.0, 0.4)),
        event("p1", "bus", "ITransactionArchived بعد إجازة/سلفة", (0.0, 0.7), (1.0, 0.7)),
        data("p1", "p2", "عنصر مؤرشف", (1.0, 0.5), (0.0, 0.5)),
        data("p2", "d_a", "إتلاف/حيازة/قائمة", (0.7, 1.0), (0.7, 0.0)),
        http("p2", "p_fil", "RegisterReference FileId رقمي فقط", (0.5, 1.0), (0.8, 0.0)),
        ref("p2", "d_f", "ApprovalEvidenceFileId Guid بلا RegisterReference", (0.0, 0.8), (1.0, 0.8)),
        event("bus", "p1", "IExecuteHireParty / IExecuteSeparationParty", (1.0, 0.7), (0.0, 0.85)),
    ]
    add_page(
        mxfile,
        14,
        "14 - الأرشيف",
        "تفكيك 12.0 — Records.Archive وarchive_db",
        "بلا Outbox. لا عمود EmployeeId في الأرشيف. party ACK للتعيين/الفصل.",
        nodes,
        flows,
        height=1000,
        scope_note=SCOPE,
    )


def compliance_page(mxfile: ET.Element) -> None:
    nodes = [
        Node("a_user", "external", "موظف / مواطن", 80, 360, 280, 80),
        Node("bus", "interface", "RabbitMQ", 80, 560, 280, 80),
        Node("p_fil", "process", "11.3 RegisterReference (File.Storage)", 80, 760, 300, 90),
        Node("p1", "process", "13.1 شكوى وتحقيق وإجراء", 460, 320, 400, 100),
        Node("d1", "store", "compliance_db", 460, 680, 360, 80),
        Node("d_core", "store", "hr_core_db (مرجع)", 960, 680, 360, 80),
        Node("d_out", "store", "Outbox", 960, 820, 280, 70),
    ]
    flows = [
        data("a_user", "p1", "شكوى أو مخالفة", (1.0, 0.5), (0.0, 0.5)),
        data("p1", "d1", "Complaint / Disciplinary", (0.38, 1.0), (0.38, 0.0)),
        event("p1", "bus", "IComplaintResolved / IDisciplinaryActionTaken / ILegalJudgmentIssued", (0.0, 0.6), (1.0, 0.4)),
        http("p1", "p_fil", "EvidenceFileId RegisterReference", (1.0, 0.5), (0.0, 0.4)),
        ref("p1", "d_core", "EmployeeId / OrgUnitId", (1.0, 0.8), (0.2, 0.0)),
        data("p1", "d_out", "Outbox", (0.8, 1.0), (0.2, 0.0)),
        event("p1", "bus", "IExportJobRequested", (0.0, 0.4), (1.0, 0.65)),
    ]
    add_page(
        mxfile,
        15,
        "15 - الامتثال",
        "تفكيك 13.0 — Compliance وcompliance_db",
        "Outbox موجود. DSAR موجود كنموذج ولم يكتمل القبول القانوني.",
        nodes,
        flows,
        height=1000,
        scope_note=SCOPE,
    )


def analytics_page(mxfile: ET.Element) -> None:
    nodes = [
        Node("bus", "interface", "RabbitMQ", 80, 360, 260, 80),
        Node("p1", "process", "14.1 تجميع أحداث التحليلات", 460, 320, 420, 100),
        Node("p2", "process", "14.2 تصدير IExportJobRequested", 980, 320, 400, 100),
        Node("d1", "store", "analytics_db", 460, 680, 360, 80),
        Node("d_core", "store", "hr_core_db (مرجع OrgUnitId)", 960, 680, 360, 80),
    ]
    flows = [
        event("bus", "p1", "IEmployeeCreated / ISeparationCompleted / IPayrollProcessed / IComplaintResolved / ICardIssued", (1.0, 0.5), (0.0, 0.4)),
        data("p1", "d1", "AnalyticsEvents (معرفات في JSON)", (0.38, 1.0), (0.38, 0.0)),
        ref("p1", "d_core", "OrgUnitId", (1.0, 0.8), (0.8, 0.0)),
        event("bus", "p2", "IExportJobRequested (consumer)", (1.0, 0.6), (0.0, 0.5)),
        event("p2", "bus", "IExportJobRequested (publisher)", (0.0, 0.5), (1.0, 0.75)),
    ]
    add_page(
        mxfile,
        16,
        "16 - التحليلات",
        "تفكيك 14.0 — Dev.Analytics وanalytics_db",
        "بلا HTTP S2S. بلا Outbox. يستهلك وينشر IExportJobRequested.",
        nodes,
        flows,
        height=980,
        scope_note=SCOPE,
    )


def it_page(mxfile: ET.Element) -> None:
    nodes = [
        Node("a_user", "external", "مشغّل IT", 80, 360, 260, 80),
        Node("bus", "interface", "RabbitMQ", 80, 560, 260, 80),
        Node("p_auth", "process", "2.2 حساب الدليل (Auth.Directory)", 80, 760, 300, 90),
        Node("p1", "process", "15.1 تذكرة وتوفير عند التعيين", 460, 320, 420, 100),
        Node("d1", "store", "it_infra_db", 460, 680, 360, 80),
        Node("d_core", "store", "hr_core_db (مرجع)", 960, 680, 360, 80),
    ]
    flows = [
        data("a_user", "p1", "تذكرة أو طلب صلاحية", (1.0, 0.5), (0.0, 0.5)),
        event("bus", "p1", "IEmployeeCreated / ISeparationCompleted", (1.0, 0.6), (0.0, 0.4)),
        data("p1", "d1", "تذاكر وأجهزة وصلاحيات", (0.38, 1.0), (0.38, 0.0)),
        http("p1", "p_auth", "POST /api/internal/directory/sync-employee", (1.0, 0.5), (0.0, 0.5)),
        ref("p1", "d_core", "AssignedTo / UserId=EmployeeId", (1.0, 0.8), (0.5, 0.0)),
        event("bus", "p1", "IExecuteHireParty / IExecuteSeparationParty", (1.0, 0.8), (0.0, 0.85)),
    ]
    add_page(
        mxfile,
        17,
        "17 - البنية التقنية",
        "تفكيك 15.0 — IT.Infra وit_infra_db",
        "بلا Outbox. مزامنة الدليل عبر HTTP S2S. party ACK للتعيين/الفصل.",
        nodes,
        flows,
        height=1000,
        scope_note=SCOPE,
    )


def integration_page(mxfile: ET.Element) -> None:
    outbox = EVIDENCE.get("outbox_contexts") or []
    direct = EVIDENCE.get("direct_publish_contexts") or []
    consumers_by_event = EVIDENCE.get("consumers_by_event") or {}

    outbox_labels = []
    for ctx in outbox:
        meta = SERVICE_REGISTRY.get(ctx, {})
        outbox_labels.append(f"{meta.get('process_id', ctx)} {meta.get('db', '')}")

    direct_labels = []
    for ctx in direct:
        if ctx == "GatewayAuditDbContext":
            continue
        meta = SERVICE_REGISTRY.get(ctx, {})
        direct_labels.append(f"{meta.get('process_id', ctx)}")

    # Top events by consumer count for the matrix note
    top_events = sorted(consumers_by_event.items(), key=lambda x: len(x[1]), reverse=True)[:12]
    matrix_lines = [f"{ev}: {len(cs)} consumer(s)" for ev, cs in top_events]

    nodes = [
        Node("p_prod", "process", "INT.1 خدمة منتجة (Outbox)", 200, 360, 420, 100),
        Node("d_out", "store", "OutboxMessage في قاعدة المنتج", 200, 720, 420, 80),
        Node("bus", "interface", "RabbitMQ MassTransit", 860, 360, 360, 100),
        Node("p_cons", "process", "INT.2 خدمة مستهلكة (Inbox)", 1460, 360, 520, 100),
        Node("d_in", "store", "Inbox / IntegrationMessageReceipts", 1460, 720, 420, 80),
        Node("d_saga", "store", "حالة Hire/Separation/Transfer Saga", 860, 720, 360, 80),
        Node(
            "note_outbox",
            "note",
            f"Outbox ({len(outbox_labels)}): " + "، ".join(outbox_labels[:8])
            + (" …" if len(outbox_labels) > 8 else ""),
            200,
            880,
            880,
            64,
        ),
        Node(
            "note_direct",
            "note",
            f"نشر مباشر ({len(direct_labels)}): " + "، ".join(direct_labels[:10]),
            1120,
            880,
            860,
            64,
        ),
        Node(
            "note_matrix",
            "note",
            "أكثر الأحداث استهلاكاً: " + " | ".join(matrix_lines[:6]),
            200,
            980,
            1780,
            72,
        ),
        Node(
            "note_saga",
            "note",
            "Saga: Hire/Separation في HR.Contracts — Transfer/Secondment في HR.Movements. "
            f"Consumers مسجّلة: {len(EVIDENCE.get('consumers') or [])}.",
            200,
            1080,
            1780,
            64,
        ),
    ]
    flows = [
        data("p_prod", "d_out", "تثبيت المعاملة والرسالة", (0.38, 1.0), (0.38, 0.0)),
        event("p_prod", "bus", "التقاط Outbox أو نشر مباشر", (1.0, 0.5), (0.0, 0.4)),
        event("bus", "p_cons", "IConsumer / Party ACK", (1.0, 0.5), (0.0, 0.5)),
        data("p_cons", "d_in", "idempotency وCorrelationId", (0.38, 1.0), (0.38, 0.0)),
        data("p_prod", "d_saga", "حالة ساغا عند المنتج", (0.7, 1.0), (0.3, 0.0)),
        event("p_cons", "bus", "ACK أو حدث لاحق", (0.0, 0.6), (1.0, 0.7)),
    ]
    add_page(
        mxfile,
        18,
        "18 - التكامل والأحداث",
        "موثوقية الرسائل كما في الشيفرة",
        "72 عقد رسالة تقريباً. لا تخزين مشترك. Outbox/Inbox من scan_consumers وAddPostgresOutbox.",
        nodes,
        flows,
        height=1200,
        scope_note=SCOPE,
    )


def _party_nodes(
    saga_key: str,
    prefix: str = "pp",
    *,
    base_x: int = 1480,
    base_y: int = 480,
    cols: int = 3,
    col_w: int = 260,
    row_h: int = 110,
) -> tuple[list[Node], dict[str, str]]:
    """Build named party process nodes from saga evidence."""
    parties = (EVIDENCE.get("saga_parties") or {}).get(saga_key) or []
    nodes: list[Node] = []
    id_map: dict[str, str] = {}
    for index, entry in enumerate(parties):
        node_id = f"{prefix}{index}"
        id_map[entry["party"]] = node_id
        nodes.append(
            Node(
                node_id,
                "process",
                f"{entry['sub']} {entry['party']}",
                base_x + (index % cols) * col_w,
                base_y + (index // cols) * row_h,
                260,
                90,
            )
        )
    return nodes, id_map


def hire_journey(mxfile: ET.Element) -> None:
    party_nodes, party_ids = _party_nodes("hire", "hp", base_x=1480, base_y=480, cols=3)
    nodes = [
        Node("a_c", "external", "متقدم / موارد بشرية", 80, 400, 260, 80),
        Node("p1", "process", "4.1 استقبال مرشح", 420, 320, 260, 90),
        Node("p2", "process", "3.2 إنشاء موظف في النواة", 760, 320, 280, 90),
        Node("p3", "process", "4.2 عقد وHire Saga", 1120, 320, 280, 90),
        Node("p5", "process", "12.1 أرشفة IHireCaseCompleted", 2380, 320, 320, 90),
        Node("d_con", "store", "hr_contracts_db", 420, 700, 280, 70),
        Node("d_core", "store", "hr_core_db", 760, 700, 280, 70),
        Node("bus", "interface", "RabbitMQ", 1280, 700, 240, 70),
        Node("d_arc", "store", "archive_db", 2380, 700, 280, 70),
    ]
    nodes.extend(party_nodes)
    flows = [
        data("a_c", "p1", "طلب توظيف", (1.0, 0.5), (0.0, 0.5)),
        data("p1", "d_con", "مرشح", (0.38, 1.0), (0.38, 0.0)),
        data("p1", "p2", "قبول", (1.0, 0.5), (0.0, 0.5)),
        data("p2", "d_core", "Employee", (0.38, 1.0), (0.38, 0.0)),
        event("p2", "bus", "IEmployeeCreated", (0.7, 1.0), (0.3, 0.0)),
        data("p2", "p3", "معرف الموظف", (1.0, 0.5), (0.0, 0.5)),
        data("p3", "d_con", "عقد وساغا", (0.5, 1.0), (0.7, 0.0)),
    ]
    for party in (EVIDENCE.get("saga_parties") or {}).get("hire") or []:
        pid = party_ids[party["party"]]
        flows.append(event("p3", pid, "IExecuteHireParty", (1.0, 0.5), (0.0, 0.5)))
        flows.append(event(pid, "bus", "IHirePartyCompleted", (0.5, 1.0), (0.5, 0.0)))
    flows.extend(
        [
            event("p3", "p5", "IHireCaseCompleted", (1.0, 0.5), (0.0, 0.5)),
            data("p5", "d_arc", "ArchiveItem", (0.38, 1.0), (0.38, 0.0)),
        ]
    )
    add_page(
        mxfile,
        19,
        "19 - رحلة التعيين",
        "رحلة التعيين الحالية — Hire Saga",
        "MandatoryParties من HireSagaOptions: HR.Core، Payroll، Auth.Directory، IDCard، Records.Archive، IT.Infra.",
        nodes,
        flows,
        height=1040,
        scope_note=SCOPE,
    )


def transfer_journey(mxfile: ET.Element) -> None:
    party_nodes, party_ids = _party_nodes("transfer", "tp", base_x=1360, base_y=480, cols=2)
    nodes = [
        Node("a", "external", "فرع مصدر/هدف", 80, 400, 240, 80),
        Node("p1", "process", "5.1 طلب نقل", 400, 320, 240, 90),
        Node("p2", "process", "5.2 ساغا ولقطة", 720, 320, 240, 90),
        Node("p3", "process", "5.3 ITransferExecuted", 1040, 320, 280, 90),
        Node("d", "store", "hr_movements_db", 560, 700, 320, 70),
        Node("bus", "interface", "RabbitMQ", 1200, 700, 240, 70),
    ]
    nodes.extend(party_nodes)
    flows = [
        data("a", "p1", "طلب", (1.0, 0.5), (0.0, 0.5)),
        data("p1", "p2", "قبول الهدف", (1.0, 0.5), (0.0, 0.5)),
        data("p2", "d", "لقطة حركة", (0.5, 1.0), (0.5, 0.0)),
        event("p2", "p3", "تنفيذ", (1.0, 0.5), (0.0, 0.5)),
        event("p3", "bus", "ITransferExecuted / IExecuteTransferParty", (0.5, 1.0), (0.5, 0.0)),
    ]
    for party in (EVIDENCE.get("saga_parties") or {}).get("transfer") or []:
        pid = party_ids[party["party"]]
        flows.append(event("bus", pid, "IExecuteTransferParty", (1.0, 0.5), (0.0, 0.5)))
        flows.append(event(pid, "bus", "ITransferPartyCompleted", (0.5, 1.0), (0.5, 0.0)))
    add_page(
        mxfile,
        20,
        "20 - رحلة النقل",
        "رحلة النقل الحالية",
        "MandatoryParties من TransferSagaOptions: Payroll، HR.Core، Auth.Directory، Admin.Logistics.",
        nodes,
        flows,
        height=920,
        scope_note=SCOPE,
    )


def leave_journey(mxfile: ET.Element) -> None:
    nodes = [
        Node("a", "external", "موظف ESS / مدير", 80, 400, 260, 80),
        Node("p1", "process", "4.3 طلب إجازة/سلفة", 420, 320, 260, 90),
        Node("p2", "process", "3.3 إنفاذ سياسة النواة", 760, 320, 280, 90),
        Node("p3", "process", "3.4 سير موافقة", 1120, 320, 240, 90),
        Node("p4", "process", "4.3 نشر اعتماد", 1440, 320, 240, 90),
        Node("p5", "process", "12.1 أرشفة (Archive)", 1760, 320, 260, 90),
        Node("p6", "process", "9.1 أثر راتب (Payroll)", 2080, 320, 260, 90),
        Node("d_c", "store", "hr_contracts_db", 420, 700, 280, 70),
        Node("d_core", "store", "hr_core_db سياسة/سير", 900, 700, 300, 70),
        Node("bus", "interface", "RabbitMQ", 1400, 700, 220, 70),
        Node("d_a", "store", "archive_db", 1760, 700, 280, 70),
    ]
    flows = [
        data("a", "p1", "طلب", (1.0, 0.5), (0.0, 0.5)),
        data("p1", "d_c", "LeaveRequest / Advance", (0.38, 1.0), (0.38, 0.0)),
        http("p1", "p2", "EnforceAsync", (1.0, 0.5), (0.0, 0.5)),
        data("p2", "d_core", "EnforcementLedger", (0.5, 1.0), (0.3, 0.0)),
        data("p2", "p3", "يتطلب موافقة", (1.0, 0.5), (0.0, 0.5)),
        data("p3", "d_core", "WorkflowInstance", (0.5, 1.0), (0.7, 0.0)),
        data("p3", "p4", "معتمد", (1.0, 0.5), (0.0, 0.5)),
        event("p4", "bus", "ILeaveApproved / IAdvanceApproved", (0.5, 1.0), (0.5, 0.0)),
        event("bus", "p5", "ILeaveApproved / IAdvanceApproved", (1.0, 0.4), (0.0, 0.5)),
        event("bus", "p6", "ILeaveApproved / IAdvanceApproved", (1.0, 0.6), (0.0, 0.5)),
        data("p5", "d_a", "ArchiveItem ثم ITransactionArchived", (0.38, 1.0), (0.38, 0.0)),
    ]
    add_page(
        mxfile,
        21,
        "21 - رحلة الإجازة والسلفة",
        "رحلة ESS الحالية",
        "Archive وPayroll يستهلكان ILeaveApproved/ IAdvanceApproved بشكل مستقل.",
        nodes,
        flows,
        height=960,
        scope_note=SCOPE,
    )


def separation_journey(mxfile: ET.Element) -> None:
    party_nodes, party_ids = _party_nodes("separation", "sp", base_x=1080, base_y=480, cols=4)
    nodes = [
        Node("a", "external", "موارد بشرية", 80, 400, 240, 80),
        Node("p1", "process", "4.4 تسجيل إنهاء", 400, 320, 240, 90),
        Node("p2", "process", "4.4 قائمة إغلاق + ساغا", 720, 320, 300, 90),
        Node("p4", "process", "4.4 ISeparationCompleted", 2040, 320, 300, 90),
        Node("p5", "process", "12.1 أرشيف نهائي", 2380, 320, 260, 90),
        Node("d", "store", "hr_contracts_db", 560, 700, 300, 70),
        Node("bus", "interface", "RabbitMQ", 1200, 700, 240, 70),
        Node("d_a", "store", "archive_db", 2380, 700, 280, 70),
    ]
    nodes.extend(party_nodes)
    flows = [
        data("a", "p1", "سبب الفصل", (1.0, 0.5), (0.0, 0.5)),
        data("p1", "p2", "بدء الإغلاق", (1.0, 0.5), (0.0, 0.5)),
        data("p2", "d", "SeparationRecord / Checklist", (0.5, 1.0), (0.5, 0.0)),
    ]
    for party in (EVIDENCE.get("saga_parties") or {}).get("separation") or []:
        pid = party_ids[party["party"]]
        flows.append(event("p2", pid, "IExecuteSeparationParty", (1.0, 0.5), (0.0, 0.5)))
        flows.append(event(pid, "bus", "ISeparationPartyCompleted", (0.5, 1.0), (0.5, 0.0)))
    flows.extend(
        [
            event("p2", "p4", "اكتمال الأطراف", (1.0, 0.5), (0.0, 0.5)),
            event("p4", "p5", "ISeparationCompleted", (1.0, 0.5), (0.0, 0.5)),
            data("p5", "d_a", "ArchiveItem (ليس عند أمر الطرف)", (0.38, 1.0), (0.38, 0.0)),
            ref("p2", "d_a", "Checklist.CorrelationId = SeparationRecordId", (0.7, 1.0), (0.2, 0.0)),
        ]
    )
    add_page(
        mxfile,
        22,
        "22 - رحلة إنهاء الخدمة",
        "رحلة الفصل الحالية — Separation Saga",
        "MandatoryParties من SeparationSagaOptions — 7 أطراف بما فيها Admin.Logistics.",
        nodes,
        flows,
        height=960,
        scope_note=SCOPE,
    )


def dictionary_page(mxfile: ET.Element) -> None:
    service_nodes: list[Node] = [
        Node("h", "heading", "قاموس العمليات والمخازن = الخدمات والقواعد الفعلية", 170, 22, 2660, 40),
        Node("p0", "process", "0 النظام\nالصفحة 01", 80, 200, 240, 90),
    ]
    x = 340
    y = 200
    for index, ctx in enumerate(sorted(SERVICE_REGISTRY, key=lambda c: SERVICE_REGISTRY[c]["process_num"])):  # type: ignore[arg-type, return-value]
        meta = SERVICE_REGISTRY[ctx]
        journey_refs = []
        if ctx == "ContractsDbContext":
            journey_refs = ["19", "21", "22"]
        elif ctx == "MovementsDbContext":
            journey_refs = ["20"]
        elif ctx == "ArchiveDbContext":
            journey_refs = ["19", "21", "22"]
        journey_suffix = f" و{','.join(journey_refs)}" if journey_refs else ""
        service_nodes.append(
            Node(
                f"p{meta['process_num']}",
                "process",
                f"{meta['process_id']} {ctx.replace('DbContext', '')}\n"
                f"الصفحة {meta['l2_page']}{journey_suffix}\n{meta['db']}",
                x,
                y,
                280,
                100,
            )
        )
        x += 300
        if (index + 1) % 5 == 0:
            x = 80
            y += 140

    service_nodes.extend(
        [
            Node("pint", "interface", "التكامل الصفحة 18 — Outbox/Inbox/Saga/RabbitMQ", 80, 480, 520, 100),
            Node(
                "dnote",
                "store",
                "15 مخزناً = 15 قاعدة. صفحة L2 لكل خدمة (03–17). رحلات 19–22.",
                640,
                480,
                720,
                100,
            ),
            Node(
                "limits",
                "note",
                "غير ظاهر كجهة حية: المصرف، الخزينة، الوزارات، CDR المؤسسي. "
                "Keycloak وMinIO جهتان تقنيتان حقيقيتان. ERD: docs/thesis_diagrams/source/database-erdds.drawio",
                80,
                620,
                2160,
                90,
            ),
        ]
    )
    add_page(
        mxfile,
        23,
        "23 - قاموس المطابقة",
        "مطابقة DFD الحالي مع الخدمات والقواعد",
        "مطابقة 1:1 — كل 1.0–15.0 لها صفحة L2 مستقلة.",
        service_nodes,
        [],
        height=900,
        scope_note=SCOPE,
    )


def validate_workbook(tree: ET.ElementTree) -> dict[str, object]:
    import re

    def process_label_text(raw: str) -> str:
        text = re.sub(r"<br\s*/?>", "\n", raw, flags=re.I)
        text = re.sub(r"<[^>]+>", "", text).replace("&nbsp;", " ").strip()
        return text

    process_number = re.compile(r"^(\d+(?:\.\d+)?|INT\.\d+|0\b)")
    diagrams = tree.getroot().findall("diagram")
    errors: list[str] = []
    warnings: list[str] = []
    names = [d.get("name", "") for d in diagrams]
    ids = [d.get("id", "") for d in diagrams]
    if len(names) != len(set(names)):
        errors.append("Duplicate page names")
    if len(ids) != len(set(ids)):
        errors.append("Duplicate page ids")
    if len(diagrams) != 23:
        warnings.append(f"Expected 23 pages, got {len(diagrams)}")

    # L2 page coverage: one page per service 1.0–15.0
    l2_titles = [d.get("name", "") for d in diagrams if re.match(r"^0[3-9] -|^1[0-7] -", d.get("name", ""))]
    if len(l2_titles) != 15:
        errors.append(f"L2 page count: expected 15, got {len(l2_titles)}")

    page_stats = []
    l1_yarp_routes = 0
    for diagram in diagrams:
        name = diagram.get("name", "")
        model = diagram.find("mxGraphModel")
        if model is None:
            errors.append(f"{name}: missing model")
            continue
        page_w = float(model.get("pageWidth", PAGE_WIDTH))
        page_h = float(model.get("pageHeight", PAGE_HEIGHT))
        root = model.find("root")
        if root is None:
            errors.append(f"{name}: missing root")
            continue
        cells = {c.get("id", ""): c for c in root.findall("mxCell")}
        vertices = [c for c in cells.values() if c.get("vertex") == "1"]
        edges = [c for c in cells.values() if c.get("edge") == "1"]
        kinds = {cid: c.get("dfdKind", "") for cid, c in cells.items()}

        if name.startswith("02 -"):
            for edge in edges:
                label = edge.get("value") or ""
                if "HTTP:" in label and edge.get("source") in cells:
                    src = cells[edge.get("source", "")]
                    if src.get("id") == "p_gw" or (
                        src.get("dfdKind") == "process"
                        and process_label_text(cells.get(edge.get("source", ""), {}).get("value") or "").startswith("1.0")
                    ):
                        tgt_id = edge.get("target", "")
                        if tgt_id in cells and cells[tgt_id].get("dfdKind") == "process":
                            l1_yarp_routes += 1

        for vertex in vertices:
            geometry = vertex.find("mxGeometry")
            if geometry is None:
                continue
            x = float(geometry.get("x", "0"))
            y = float(geometry.get("y", "0"))
            w = float(geometry.get("width", "0"))
            h = float(geometry.get("height", "0"))
            if x < 0 or y < 0 or x + w > page_w + 2 or y + h > page_h + 2:
                errors.append(f"{name}: vertex outside page: {vertex.get('id')}")
        for edge in edges:
            source = edge.get("source", "")
            target = edge.get("target", "")
            if source not in cells or target not in cells:
                errors.append(f"{name}: dangling {edge.get('id')}")
                continue
            if not edge.get("value", "").strip():
                errors.append(f"{name}: unnamed {edge.get('id')}")
            sk, tk = kinds.get(source, ""), kinds.get(target, "")
            if {sk, tk} == {"external", "store"}:
                errors.append(f"{name}: external-store {edge.get('id')}")
            if sk == "store" and tk == "store":
                errors.append(f"{name}: store-store {edge.get('id')}")
            if "startArrow=classic" in edge.get("style", ""):
                errors.append(f"{name}: bidirectional {edge.get('id')}")
        for vertex in vertices:
            if vertex.get("dfdKind") != "process":
                continue
            label = process_label_text(vertex.get("value") or "")
            if not process_number.match(label):
                errors.append(f"{name}: unnumbered process: {label[:40]}")
        page_stats.append({"name": name, "vertices": len(vertices), "flows": len(edges)})

    expected_yarp = EVIDENCE.get("yarp_service_count", 14)
    if l1_yarp_routes < expected_yarp:
        warnings.append(f"L1 p_gw routes: {l1_yarp_routes} < evidence {expected_yarp}")

    return {
        "valid": not errors,
        "errors": errors,
        "warnings": warnings,
        "page_count": len(diagrams),
        "l1_yarp_routes": l1_yarp_routes,
        "l2_pages": len(l2_titles),
        "pages": page_stats,
        "output": str(OUTPUT.relative_to(ROOT)).replace("\\", "/"),
    }


def build_workbook() -> ET.ElementTree:
    global EVIDENCE
    EVIDENCE = load_evidence()

    mxfile = ET.Element(
        "mxfile",
        {
            "host": "app.diagrams.net",
            "modified": datetime.now(timezone.utc).isoformat(timespec="seconds"),
            "agent": "HRManagement current-system DFD generator",
            "version": "24.7.17",
            "type": "device",
            "compressed": "false",
        },
    )
    context_page(mxfile)
    level_one_page(mxfile)
    gateway_page(mxfile)
    auth_page(mxfile)
    core_page(mxfile)
    contracts_page(mxfile)
    movements_page(mxfile)
    health_page(mxfile)
    training_page(mxfile)
    idcard_page(mxfile)
    payroll_page(mxfile)
    logistics_page(mxfile)
    files_page(mxfile)
    archive_page(mxfile)
    compliance_page(mxfile)
    analytics_page(mxfile)
    it_page(mxfile)
    integration_page(mxfile)
    hire_journey(mxfile)
    transfer_journey(mxfile)
    leave_journey(mxfile)
    separation_journey(mxfile)
    dictionary_page(mxfile)
    ET.indent(mxfile, space="  ")
    return ET.ElementTree(mxfile)


def main() -> None:
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    tree = build_workbook()
    report = validate_workbook(tree)
    VALIDATION.parent.mkdir(parents=True, exist_ok=True)
    VALIDATION.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    if not report["valid"]:
        raise SystemExit("DFD validation failed:\n" + "\n".join(report["errors"]))
    tree.write(OUTPUT, encoding="utf-8", xml_declaration=True, short_empty_elements=True)
    print(json.dumps({"output": str(OUTPUT), "pages": report["page_count"]}, ensure_ascii=False))


if __name__ == "__main__":
    main()
