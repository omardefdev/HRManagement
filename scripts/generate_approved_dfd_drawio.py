"""Generate the single authoritative, editable Draw.io DFD workbook.

The workbook is derived from the accepted business flows in the original DFD
and reconciled with the current implementation truth register. Every symbol,
label, and connector is a native mxGraph object; no page contains an embedded
image. The thesis build is intentionally not touched by this script.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime, timezone
from html import escape
from pathlib import Path
import json
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "docs" / "output" / "HRManagement_DFD_Approved.drawio"
RENDER_HTML = ROOT / "tmp" / "approved_dfd_renderer.html"
RENDER_MANIFEST = ROOT / "tmp" / "approved_dfd_manifest.json"
VALIDATION_REPORT = ROOT / "tmp" / "approved_dfd_validation.json"

PAGE_WIDTH = 3000
PAGE_HEIGHT = 1700

INK = "#243B4A"
BLUE = "#315F8C"
TEAL = "#0B6F70"
GREEN_FILL = "#EAF6F3"
PROCESS_FILL = "#F7FAFC"
STORE_FILL = "#FFF7E8"
STORE_BORDER = "#A66516"
NOTE_FILL = "#F3F6F7"
GRAY = "#5B6972"
RED = "#A33A3A"

REQUIRED_DFD_TERMS = (
    "TenantId",
    "scope_org_unit_id",
    "RLS",
    "Agency Manifest",
    "Onboarding",
    "MDM",
    "TenantAssignmentRule",
    "TenantQuarantineEntry",
    "OrgUnitVersion",
    "FundedPosition",
    "PolicyTemplateVersion",
    "SimulationRun",
    "EnforcementLedger",
    "WorkflowInstance",
    "Signature",
    "Delegation",
    "CodeIssuance",
    "Outbox",
    "Inbox",
    "Saga",
    "DSAR",
    "ServiceTrust",
    "RetentionPolicy",
    "FraudAlert",
    "ArrearsLedger",
    "PaymentOrder",
    "BankAccount",
    "SupplyOrderLine",
    "Receipt",
    "AssetAssignment",
    "AssetLossReport",
    "Lineage",
    "CorrelationId",
)


@dataclass(frozen=True)
class Node:
    key: str
    kind: str
    label: str
    x: int
    y: int
    width: int = 300
    height: int = 96


@dataclass(frozen=True)
class Flow:
    source: str
    target: str
    label: str
    source_port: tuple[float, float] = (1.0, 0.5)
    target_port: tuple[float, float] = (0.0, 0.5)
    points: tuple[tuple[int, int], ...] = field(default_factory=tuple)
    dashed: bool = False
    color: str = BLUE
    balance_key: str = ""
    label_position: float = 0.0
    label_offset: int = 0


def rich(text: str, *, bold: bool = False, align: str = "center") -> str:
    safe = escape(text).replace("\n", "<br>")
    weight = "font-weight:700;" if bold else ""
    return f'<div dir="rtl" style="text-align:{align};{weight}">{safe}</div>'


def vertex_style(kind: str) -> str:
    common = (
        "whiteSpace=wrap;html=1;fontFamily=Arial;align=center;"
        "verticalAlign=middle;spacing=8;overflow=hidden;"
    )
    if kind == "external":
        return common + (
            f"rounded=0;fillColor={GREEN_FILL};strokeColor={TEAL};"
            "strokeWidth=2.2;fontSize=18;fontStyle=1;"
        )
    if kind == "process":
        return common + (
            f"rounded=1;arcSize=12;fillColor={PROCESS_FILL};strokeColor={INK};"
            "strokeWidth=2.2;fontSize=18;fontStyle=1;"
        )
    if kind == "store":
        return common + (
            "shape=partialRectangle;left=0;right=0;top=1;bottom=1;"
            f"fillColor={STORE_FILL};strokeColor={STORE_BORDER};"
            "strokeWidth=2.2;fontSize=16;fontStyle=1;"
        )
    if kind == "interface":
        return common + (
            f"rounded=1;arcSize=8;fillColor=#EEF4FA;strokeColor={BLUE};"
            "strokeWidth=2;fontSize=16;fontStyle=1;dashed=1;"
        )
    if kind == "note":
        return common + (
            f"rounded=1;arcSize=6;fillColor={NOTE_FILL};strokeColor=#9AAAB3;"
            f"fontColor={GRAY};fontSize=14;"
        )
    if kind == "heading":
        return common + (
            f"rounded=0;fillColor=none;strokeColor=none;fontColor={INK};"
            "fontSize=28;fontStyle=1;"
        )
    if kind == "subheading":
        return common + (
            f"rounded=0;fillColor=none;strokeColor=none;fontColor={BLUE};"
            "fontSize=18;fontStyle=1;"
        )
    return common + (
        f"rounded=0;fillColor=none;strokeColor=none;fontColor={INK};fontSize=16;"
    )


def edge_style(flow: Flow) -> str:
    sx, sy = flow.source_port
    tx, ty = flow.target_port
    style = (
        "edgeStyle=orthogonalEdgeStyle;rounded=0;orthogonalLoop=1;"
        "jettySize=22;html=1;"
        f"exitX={sx};exitY={sy};exitDx=0;exitDy=0;exitPerimeter=1;"
        f"entryX={tx};entryY={ty};entryDx=0;entryDy=0;entryPerimeter=1;"
        f"strokeColor={flow.color};strokeWidth=1.8;endArrow=classic;endFill=1;"
        "startArrow=none;fontFamily=Arial;fontSize=15;fontColor=#294A67;"
        "labelBackgroundColor=#FFFFFF;align=center;verticalAlign=middle;"
    )
    if flow.dashed:
        style += "dashed=1;dashPattern=7 4;"
    return style


def add_vertex(root: ET.Element, page_id: str, node: Node) -> None:
    cell = ET.SubElement(
        root,
        "mxCell",
        {
            "id": f"{page_id}-{node.key}",
            "value": rich(
                node.label,
                bold=node.kind in {"external", "process", "store", "interface"},
            ),
            "style": vertex_style(node.kind),
            "vertex": "1",
            "parent": "1",
            "dfdKind": node.kind,
        },
    )
    ET.SubElement(
        cell,
        "mxGeometry",
        {
            "x": str(node.x),
            "y": str(node.y),
            "width": str(node.width),
            "height": str(node.height),
            "as": "geometry",
        },
    )


def add_flow(root: ET.Element, page_id: str, index: int, flow: Flow) -> None:
    attributes = {
        "id": f"{page_id}-flow-{index:03d}",
        "value": rich(flow.label),
        "style": edge_style(flow),
        "edge": "1",
        "parent": "1",
        "source": f"{page_id}-{flow.source}",
        "target": f"{page_id}-{flow.target}",
        "dfdKind": "flow",
    }
    if flow.balance_key:
        attributes["balanceKey"] = flow.balance_key
    cell = ET.SubElement(
        root,
        "mxCell",
        attributes,
    )
    geometry_attributes = {"relative": "1", "as": "geometry"}
    if flow.label_position:
        geometry_attributes["x"] = str(flow.label_position)
    if flow.label_offset:
        geometry_attributes["y"] = str(flow.label_offset)
    geometry = ET.SubElement(cell, "mxGeometry", geometry_attributes)
    if flow.points:
        array = ET.SubElement(geometry, "Array", {"as": "points"})
        for x, y in flow.points:
            ET.SubElement(array, "mxPoint", {"x": str(x), "y": str(y)})


def add_page(
    mxfile: ET.Element,
    number: int,
    name: str,
    title: str,
    subtitle: str,
    nodes: list[Node],
    flows: list[Flow],
    *,
    width: int = PAGE_WIDTH,
    height: int = PAGE_HEIGHT,
    scope_note: str | None = (
        "بيانات التحكم المصاحبة: TenantId وscope_org_unit_id وCorrelationId. "
        "تتحقق كل خدمة من الجهة والنطاق التنظيمي، ويقتصر الفرع على وحدته وتفرعاتها."
    ),
) -> None:
    page_id = f"dfd-{number:02d}"
    keys = {node.key for node in nodes}
    if len(keys) != len(nodes):
        raise ValueError(f"Duplicate node key on page {number}: {name}")
    for flow in flows:
        if flow.source not in keys or flow.target not in keys:
            raise ValueError(
                f"Unknown endpoint on page {number}: {flow.source} -> {flow.target}"
            )
        if not flow.label.strip():
            raise ValueError(f"Unnamed flow on page {number}")

    diagram = ET.SubElement(mxfile, "diagram", {"id": page_id, "name": name})
    model = ET.SubElement(
        diagram,
        "mxGraphModel",
        {
            "dx": "1422",
            "dy": "794",
            "grid": "1",
            "gridSize": "10",
            "guides": "1",
            "tooltips": "1",
            "connect": "1",
            "arrows": "1",
            "fold": "1",
            "page": "1",
            "pageScale": "1",
            "pageWidth": str(width),
            "pageHeight": str(height),
            "math": "0",
            "shadow": "0",
            "background": "#FFFFFF",
        },
    )
    graph_root = ET.SubElement(model, "root")
    ET.SubElement(graph_root, "mxCell", {"id": "0"})
    ET.SubElement(graph_root, "mxCell", {"id": "1", "parent": "0"})
    add_vertex(
        graph_root,
        page_id,
        Node("page-title", "heading", title, 170, 22, width - 340, 52),
    )
    add_vertex(
        graph_root,
        page_id,
        Node("page-subtitle", "subheading", subtitle, 170, 80, width - 340, 46),
    )
    for node in nodes:
        add_vertex(graph_root, page_id, node)
    for index, flow in enumerate(flows, 1):
        add_flow(graph_root, page_id, index, flow)
    if scope_note:
        add_vertex(
            graph_root,
            page_id,
            Node(
                "scope-note",
                "note",
                scope_note,
                430,
                height - 108,
                width - 860,
                58,
            ),
        )
    add_vertex(
        graph_root,
        page_id,
        Node(
            "page-number",
            "text",
            f"DFD-{number:02d}",
            width - 230,
            height - 82,
            150,
            36,
        ),
    )


def store_write(process: str, store: str, label: str) -> Flow:
    return Flow(process, store, label, (0.38, 1.0), (0.38, 0.0))


def store_read(store: str, process: str, label: str) -> Flow:
    return Flow(store, process, label, (0.66, 0.0), (0.66, 1.0))


PIPE_X = (2450, 2010, 1570, 1130, 690, 250)


def pipeline_page(
    mxfile: ET.Element,
    number: int,
    name: str,
    title: str,
    subtitle: str,
    processes: tuple[str, str, str, str, str, str],
    stores: tuple[str, str, str, str, str, str],
    sequence_labels: tuple[str, str, str, str, str],
    actors: list[Node],
    external_flows: list[Flow],
    store_writes: tuple[str, str, str, str, str, str],
    *,
    store_reads: tuple[tuple[int, str], ...] = (),
    extra_nodes: list[Node] | None = None,
    extra_flows: list[Flow] | None = None,
    scope_note: str | None = (
        "بيانات التحكم المصاحبة: TenantId وscope_org_unit_id وCorrelationId. "
        "تخضع القراءة والكتابة لمرشح الجهة ولنطاق الوحدة التنظيمية."
    ),
) -> None:
    nodes = list(actors)
    for index, (x, label) in enumerate(zip(PIPE_X, processes), 1):
        nodes.append(Node(f"p{index}", "process", label, x, 390, 300, 112))
    for index, (x, label) in enumerate(zip(PIPE_X, stores), 1):
        nodes.append(Node(f"d{index}", "store", label, x + 20, 720, 260, 90))
    if extra_nodes:
        nodes.extend(extra_nodes)

    flows = list(external_flows)
    for index, label in enumerate(sequence_labels, 1):
        flows.append(
            Flow(
                f"p{index}",
                f"p{index + 1}",
                label,
                (0.0, 0.46),
                (1.0, 0.46),
            )
        )
    for index, label in enumerate(store_writes, 1):
        flows.append(store_write(f"p{index}", f"d{index}", label))
    for store_index, label in store_reads:
        flows.append(store_read(f"d{store_index}", f"p{store_index}", label))
    if extra_flows:
        flows.extend(extra_flows)
    add_page(
        mxfile,
        number,
        name,
        title,
        subtitle,
        nodes,
        flows,
        scope_note=scope_note,
    )


def context_page(mxfile: ET.Element) -> None:
    nodes = [
        Node(
            "system",
            "process",
            "0\nمنصة إدارة الموارد البشرية HRManagement\n"
            "استقبال البيانات، تطبيق الصلاحيات والسياسات، إدارة العمليات، "
            "إصدار النتائج، والتدقيق والأرشفة",
            1050,
            555,
            900,
            390,
        ),
        Node("a1", "external", "مسؤول المنصة ومالك الجهة", 90, 215, 360, 95),
        Node("a2", "external", "إدارة الموارد البشرية المركزية", 90, 595, 360, 95),
        Node("a3", "external", "إدارة الموارد البشرية في الفرع", 90, 975, 360, 95),
        Node("a4", "external", "الموظف والمتقدم والمواطن", 2550, 215, 360, 95),
        Node("a5", "external", "الصحة المهنية واللجان وجهات التدريب", 2550, 595, 360, 95),
        Node("a6", "external", "الخزينة والمصرف", 2550, 905, 360, 95),
        Node("a9", "external", "الموردون", 2550, 1185, 360, 95),
        Node("a7", "external", "الإدارة العليا واللجان المعتمدة", 1320, 165, 360, 95),
        Node("a8", "external", "الوزارات والجهات المخولة بطلب الوثائق", 1270, 1250, 460, 95),
        Node(
            "rule",
            "note",
            "كل خط يمثل تدفقاً واحداً مسمى؛ لا توجد أسهم مزدوجة، ولا اتصال مباشر "
            "بين جهة خارجية ومخزن بيانات.",
            990,
            1050,
            1020,
            70,
        ),
    ]
    flows = [
        Flow("a1", "system", "بيان جهة وطلب تهيئة", (1.0, 0.40), (0.0, 0.12), balance_key="CTX-A1-IN"),
        Flow("system", "a1", "نتيجة تهيئة ومصالحة", (0.0, 0.20), (1.0, 0.64), balance_key="CTX-A1-OUT"),
        Flow("a2", "system", "قرارات مركزية واعتمادات", (1.0, 0.40), (0.0, 0.38), balance_key="CTX-A2-IN"),
        Flow("system", "a2", "مؤشرات وتقارير شاملة", (0.0, 0.46), (1.0, 0.64), balance_key="CTX-A2-OUT"),
        Flow("a3", "system", "معاملات ضمن نطاق الفرع", (1.0, 0.40), (0.0, 0.67), balance_key="CTX-A3-IN"),
        Flow("system", "a3", "نتائج وإشعارات ضمن النطاق", (0.0, 0.75), (1.0, 0.64), balance_key="CTX-A3-OUT"),
        Flow("a4", "system", "طلبات وتفاصيل ووثائق", (0.0, 0.40), (1.0, 0.12), balance_key="CTX-A4-IN"),
        Flow("system", "a4", "رقم تتبع وقرار وإشعار", (1.0, 0.20), (0.0, 0.64), balance_key="CTX-A4-OUT"),
        Flow("a5", "system", "تقارير طبية ونتائج وشهادات", (0.0, 0.40), (1.0, 0.38), balance_key="CTX-A5-IN"),
        Flow("system", "a5", "إحالات وترشيحات وقوائم", (1.0, 0.46), (0.0, 0.64), balance_key="CTX-A5-OUT"),
        Flow("a6", "system", "نتائج دفع وكشوف مصرفية", (0.0, 0.40), (1.0, 0.62), balance_key="CTX-A6-IN"),
        Flow("system", "a6", "أوامر دفع وتسوية", (1.0, 0.70), (0.0, 0.64), balance_key="CTX-A6-OUT"),
        Flow("a9", "system", "فواتير وشحنات ومحاضر استلام", (0.0, 0.40), (1.0, 0.82), balance_key="CTX-A9-IN"),
        Flow("system", "a9", "أوامر توريد ونتائج قبول", (1.0, 0.88), (0.0, 0.64), balance_key="CTX-A9-OUT"),
        Flow("a7", "system", "سياسات وقرارات واعتمادات", (0.40, 1.0), (0.40, 0.0), balance_key="CTX-A7-IN"),
        Flow("system", "a7", "تقارير وطلبات اعتماد", (0.60, 0.0), (0.64, 1.0), balance_key="CTX-A7-OUT"),
        Flow("a8", "system", "طلب وثيقة أو تحقق مخول", (0.40, 0.0), (0.40, 1.0), balance_key="CTX-A8-IN"),
        Flow("system", "a8", "وثيقة أو نتيجة تحقق", (0.60, 1.0), (0.64, 0.0), balance_key="CTX-A8-OUT"),
    ]
    add_page(
        mxfile,
        1,
        "01 - مخطط السياق",
        "مخطط سياق منصة إدارة الموارد البشرية (DFD Context)",
        "حدود النظام والجهات الخارجية والتدفقات الداخلة والخارجة المتوازنة.",
        nodes,
        flows,
        scope_note=None,
    )


def level_one_page(mxfile: ET.Element) -> None:
    positions = {
        # The wide dependency-oriented grid keeps the classic 28 pt labels readable.
        "p1": (6400, 600),
        "p2": (4600, 600),
        "p3": (2800, 600),
        "p4": (1000, 600),
        "p5": (6400, 1650),
        "p6": (4600, 1650),
        "p7": (2800, 1650),
        "p8": (1000, 1650),
        "p9": (6400, 2700),
        "p10": (4600, 2700),
        "p11": (2800, 2700),
        "p12": (1000, 2700),
    }
    labels = {
        "p1": "1.0 حوكمة المنصة والجهات والتهيئة",
        "p2": "2.0 الهيكل التنظيمي والملاك والوظائف الممولة",
        "p3": "3.0 التوظيف والموظف والعقود والخدمة",
        "p4": "4.0 الحركات والتوزيع وإنهاء الخدمة",
        "p5": "5.0 الخدمة الذاتية والإجازات والسلف",
        "p6": "6.0 التدريب والبعثات والصحة المهنية",
        "p7": "7.0 الرواتب والتمويل والدفع",
        "p8": "8.0 اللوجستيات والمخزون والأصول والعهد",
        "p9": "9.0 الهوية والملفات والأرشيف وحقوق البيانات",
        "p10": "10.0 الشكاوى والانضباط والامتثال",
        "p11": "11.0 السياسات وسير العمل والتوقيعات والرموز",
        "p12": "12.0 التكامل والأحداث والتقارير والتحليلات",
    }
    store_labels = {
        "d1": "D1 الجهات والتهيئة والإسناد والحجر وMDM",
        "d2": "D2 الهيكل المؤرخ والملاك والوظائف الممولة",
        "d3": "D3 المرشحون والموظفون والعقود والخدمة",
        "d4": "D4 الحركات والتوزيع والفصل والتسويات",
        "d5": "D5 طلبات ESS والإجازات والسلف والأرصدة",
        "d6": "D6 التدريب والبعثات والصحة والقرارات الطبية",
        "d7": "D7 الرواتب والتمويل والمتأخرات والمدفوعات",
        "d8": "D8 التوريد والمخزون والأصول والعهد والفقد",
        "d9": "D9 الهوية والملفات والمنح والأرشيف وDSAR",
        "d10": "D10 الشكاوى والتحقيق والانضباط والتدقيق",
        "d11": "D11 السياسات والسير والتوقيعات وإصدارات الرموز",
        "d12": "D12 Outbox وInbox وSaga ونماذج القراءة",
    }
    process_order = tuple(f"p{index}" for index in range(1, 13))
    store_order = tuple(f"d{index}" for index in range(1, 13))
    nodes: list[Node] = []
    for key in process_order:
        x, y = positions[key]
        nodes.append(Node(key, "process", labels[key], x, y, 900, 150))
    for pkey, dkey in zip(process_order, store_order):
        x, y = positions[pkey]
        nodes.append(Node(dkey, "store", store_labels[dkey], x + 70, y + 270, 760, 110))
    actors = [
        Node("a1", "external", "مسؤول المنصة ومالك الجهة", 6450, 260, 800, 110),
        Node("a2-in", "external", "الإدارة المركزية", 4650, 260, 800, 110),
        Node("a4-recruit", "external", "الموظف والمتقدم والمواطن", 2850, 260, 800, 110),
        Node("a3", "external", "إدارة الفرع", 1050, 260, 800, 110),
        Node("a4-ess", "external", "الموظف والمتقدم والمواطن (مكرر)", 6450, 1310, 800, 110),
        Node("a5", "external", "الصحة المهنية واللجان وجهات التدريب", 4650, 1310, 800, 110),
        Node("a6", "external", "الخزينة والمصرف", 2850, 1310, 800, 110),
        Node("a9", "external", "الموردون", 1050, 1310, 800, 110),
        Node("a4-docs", "external", "الموظف والمواطن (مكرر)", 6450, 2360, 800, 110),
        Node("a7", "external", "الإدارة العليا واللجان المعتمدة", 2850, 2360, 800, 110),
        Node("a2-out", "external", "الإدارة المركزية (مكرر)", 1050, 2360, 800, 110),
        Node("a8", "external", "الوزارات والجهات المخولة بطلب الوثائق", 6450, 3350, 800, 110),
        Node(
            "duplicate-note",
            "note",
            "تكرار رمز الجهة الخارجية لتحسين القراءة فقط؛ ويظل الممثل واحداً في مخطط السياق.",
            1000,
            3350,
            1700,
            110,
        ),
    ]
    nodes.extend(actors)
    flows: list[Flow] = []
    for pkey, dkey in zip(process_order, store_order):
        flows.append(Flow(pkey, dkey, "حفظ وتحديث", (0.24, 1.0), (0.24, 0.0)))
        flows.append(Flow(dkey, pkey, "بيانات المجال", (0.76, 0.0), (0.76, 1.0)))
    flows.extend(
        [
            Flow("a1", "p1", "بيان جهة وطلب تهيئة", (0.22, 1.0), (0.22, 0.0), balance_key="CTX-A1-IN", label_offset=-18),
            Flow("p1", "a1", "نتيجة تهيئة ومصالحة", (0.78, 0.0), (0.78, 1.0), balance_key="CTX-A1-OUT", label_offset=18),
            Flow("a2-in", "p2", "قرارات مركزية واعتمادات", (0.5, 1.0), (0.5, 0.0), balance_key="CTX-A2-IN"),
            Flow("p12", "a2-out", "مؤشرات وتقارير شاملة", (0.5, 0.0), (0.5, 1.0), balance_key="CTX-A2-OUT"),
            Flow("a3", "p4", "معاملات ضمن نطاق الفرع", (0.22, 1.0), (0.22, 0.0), balance_key="CTX-A3-IN", label_offset=-18),
            Flow("p4", "a3", "نتائج وإشعارات ضمن النطاق", (0.78, 0.0), (0.78, 1.0), balance_key="CTX-A3-OUT", label_offset=18),
            Flow("a4-recruit", "p3", "طلب توظيف وبيانات ووثائق", (0.22, 1.0), (0.22, 0.0), balance_key="CTX-A4-IN", label_offset=-18),
            Flow("p3", "a4-recruit", "رقم تتبع وقرار توظيف", (0.78, 0.0), (0.78, 1.0), balance_key="CTX-A4-OUT", label_offset=18),
            Flow("a4-ess", "p5", "طلب خدمة ذاتية", (0.22, 1.0), (0.22, 0.0), balance_key="CTX-A4-IN", label_offset=-18),
            Flow("p5", "a4-ess", "قرار خدمة وإشعار", (0.78, 0.0), (0.78, 1.0), balance_key="CTX-A4-OUT", label_offset=18),
            Flow("a4-docs", "p9", "طلب وثيقة أو حق بيانات", (0.22, 1.0), (0.22, 0.0), balance_key="CTX-A4-IN", label_offset=-18),
            Flow("p9", "a4-docs", "وثيقة أو نتيجة حق بيانات", (0.78, 0.0), (0.78, 1.0), balance_key="CTX-A4-OUT", label_offset=18),
            Flow("a5", "p6", "تقارير طبية ونتائج وشهادات", (0.22, 1.0), (0.22, 0.0), balance_key="CTX-A5-IN", label_offset=-18),
            Flow("p6", "a5", "إحالات وترشيحات وقوائم", (0.78, 0.0), (0.78, 1.0), balance_key="CTX-A5-OUT", label_offset=18),
            Flow("a6", "p7", "نتائج دفع وكشوف مصرفية", (0.22, 1.0), (0.22, 0.0), balance_key="CTX-A6-IN", label_offset=-18),
            Flow("p7", "a6", "أوامر دفع وتسوية", (0.78, 0.0), (0.78, 1.0), balance_key="CTX-A6-OUT", label_offset=18),
            Flow("a9", "p8", "فواتير وشحنات ومحاضر استلام", (0.22, 1.0), (0.22, 0.0), balance_key="CTX-A9-IN", label_offset=-18),
            Flow("p8", "a9", "أوامر توريد ونتائج قبول", (0.78, 0.0), (0.78, 1.0), balance_key="CTX-A9-OUT", label_offset=18),
            Flow("a7", "p11", "سياسات وقرارات واعتمادات", (0.22, 1.0), (0.22, 0.0), balance_key="CTX-A7-IN", label_offset=-18),
            Flow("p12", "a7", "تقارير وطلبات اعتماد", (1.0, 0.35), (0.0, 0.65), ((2100, 2752), (2100, 2520), (2800, 2520)), balance_key="CTX-A7-OUT"),
            Flow("a8", "p9", "طلب وثيقة أو تحقق مخول", (0.22, 0.0), (0.0, 0.66), ((6250, 3350), (6100, 3350), (6100, 2799)), balance_key="CTX-A8-IN", label_offset=-18),
            Flow("p9", "a8", "وثيقة أو نتيجة تحقق", (1.0, 0.34), (0.78, 0.0), ((7420, 2751), (7420, 3270), (7074, 3270)), balance_key="CTX-A8-OUT", label_offset=18),
            Flow("p1", "p2", "سياق الجهة ومرجعياتها", (0.0, 0.30), (1.0, 0.30), label_offset=-22),
            Flow("p2", "p3", "هيكل ووظيفة ممولة", (0.0, 0.68), (1.0, 0.68), label_offset=22),
            Flow("p3", "p4", "موظف وحالة خدمة", (0.0, 0.30), (1.0, 0.30), label_offset=-22),
            Flow("p3", "p5", "هوية موظف ورصيد خدمة", (1.0, 0.74), (0.0, 0.55), ((3900, 711), (3900, 1130), (6200, 1130), (6200, 1732))),
            Flow("p3", "p7", "عقد وأثر مالي", (1.0, 0.88), (1.0, 0.25), ((4000, 732), (4000, 1688)), label_position=-0.55),
            Flow("p4", "p7", "حركة ومركز كلفة أو فصل", (1.0, 0.74), (0.0, 0.75), ((2200, 711), (2200, 1190), (2600, 1190), (2600, 1762))),
            Flow("p4", "p8", "تحويل أو إغلاق عهدة", (0.0, 0.82), (0.0, 0.25), ((760, 723), (760, 1688))),
            Flow("p5", "p7", "أثر إجازة أو سلفة", (0.0, 0.36), (1.0, 0.36), ((6200, 1704), (6200, 2200), (3900, 2200), (3900, 1704)), label_offset=-22),
            Flow("p6", "p3", "كفاءة أو توصية مهنية مسموحة", (0.0, 0.28), (1.0, 0.72), ((4300, 1692), (4300, 708))),
            Flow("p6", "p7", "أثر بعثة أو تعويض مسموح", (0.0, 0.66), (1.0, 0.66), label_offset=22),
            Flow("p8", "p3", "حالة عهدة أو نتيجة إغلاق", (1.0, 0.24), (0.0, 0.40), ((2100, 1686), (2100, 660))),
            Flow("p9", "p3", "هوية ووثيقة متحققة", (0.0, 0.20), (1.0, 0.20), ((7350, 2730), (7350, 1050), (4100, 1050), (4100, 630)), label_position=-0.58),
            Flow("p10", "p3", "قرار انضباط أو امتثال", (0.0, 0.20), (1.0, 0.36), ((4400, 2730), (4400, 654)), label_position=-0.58),
            Flow("p3", "p11", "حقائق تتطلب سياسة أو مساراً", (0.0, 0.52), (0.0, 0.28), ((2400, 678), (2400, 2742)), dashed=True, label_position=0.55),
            Flow("p11", "p3", "قرار سياسة ومسار ورمز", (0.0, 0.72), (0.0, 0.78), ((2200, 2808), (2200, 717)), dashed=True, label_position=-0.55),
            Flow("p3", "p12", "أحداث أعمال موحدة", (0.0, 0.38), (1.0, 0.28), ((2050, 657), (2050, 2742), (1900, 2742)), dashed=True, label_position=0.58),
            Flow("p11", "p12", "قرارات ورموز وأحداث حوكمة", (0.0, 0.68), (1.0, 0.68), dashed=True, label_offset=22),
            Flow("p12", "p2", "نماذج قراءة ومؤشرات تخطيط", (0.0, 0.18), (0.0, 0.18), ((700, 2727), (700, 1100), (4400, 1100), (4400, 627)), dashed=True, label_position=0.52),
            Flow("p12", "p11", "حالات تسليم ومصالحة", (1.0, 0.32), (0.0, 0.32), dashed=True, label_offset=-22),
        ]
    )
    add_page(
        mxfile,
        2,
        "02 - العمليات الرئيسية",
        "مخطط تدفق البيانات - المستوى الأول",
        "تفكيك العملية 0 إلى اثنتي عشرة عملية ومخازن منطقية متوازنة، مع فصل مسارات القراءة والكتابة.",
        nodes,
        flows,
        width=7500,
        height=3700,
        scope_note=(
            "D1-D12 مخازن منطقية تقابل حدود ملكية الخدمات. يعرض هذا المستوى التدفقات الرئيسية؛ "
            "وتفصل تدفقات السياسات والأحداث والموثوقية في DFD-13 وDFD-14 وDFD-27. "
            "تحمل التدفقات العابرة TenantId وCorrelationId."
        ),
    )


def tenant_governance_page(mxfile: ET.Element) -> None:
    actors = [
        Node("a1", "external", "مسؤول المنصة", 2450, 160, 300, 88),
        Node("a2", "external", "مالك الجهة", 1570, 160, 300, 88),
        Node("a3", "external", "الإدارة المركزية", 690, 160, 300, 88),
        Node("a4", "external", "المدقق", 250, 160, 300, 88),
    ]
    external_flows = [
        Flow("a1", "p1", "Agency Manifest", (0.5, 1.0), (0.65, 0.0)),
        Flow("a2", "p3", "اعتماد الإسناد والتفعيل", (0.5, 1.0), (0.65, 0.0)),
        Flow("a3", "p4", "حزمة وطنية وبيانات MDM", (0.5, 1.0), (0.65, 0.0)),
        Flow("p6", "a4", "تقرير مصالحة وأثر", (0.35, 0.0), (0.5, 1.0)),
    ]
    extra_flows = [
        Flow(
            "d3",
            "p6",
            "سجلات محجورة وأسبابها",
            (0.45, 1.0),
            (0.55, 1.0),
            ((1710, 980), (400, 980)),
        ),
        Flow("d1", "p2", "بيان الجهة", (0.0, 0.5), (1.0, 0.72)),
        Flow("d4", "p5", "مرجعيات وإعدادات مطبقة", (0.0, 0.5), (1.0, 0.72)),
    ]
    pipeline_page(
        mxfile,
        3,
        "03 - حوكمة المنصة والجهات والتهيئة",
        "تفصيل العملية 1.0 - حوكمة المنصة والجهات والتهيئة",
        "تهيئة الجهة، الإسناد والحجر، الحزم الوطنية، MDM، العزل متعدد الجهات، والمصالحة.",
        (
            "1.1 استقبال Agency Manifest وطلب التهيئة",
            "1.2 تنفيذ المعاينة والتحقق من خطة Onboarding",
            "1.3 إسناد الجهة وتطبيق العزل والحجر",
            "1.4 تطبيق الحزم الوطنية وبيانات MDM",
            "1.5 تفعيل إعدادات Tenant وRLS وثقة الخدمات",
            "1.6 مصالحة السجلات وتفعيل الجهة",
        ),
        (
            "D1.1 بيانات الجهة وإصدارات Agency Manifest",
            "D1.2 نتائج المعاينة وخطة التهيئة",
            "D1.3 قواعد الإسناد وسجل الحجر",
            "D1.4 الحزم الوطنية والمرجعيات",
            "D1.5 TenantSettings وRLS وServiceTrust",
            "D1.6 سجل التهيئة والمصالحة والتدقيق",
        ),
        (
            "بيان جهة محقق",
            "خطة تهيئة معتمدة",
            "جهة وسجلات منسوبة",
            "مرجعيات مطبقة",
            "عزل وإعدادات مفعلة",
        ),
        actors,
        external_flows,
        (
            "حفظ البيان والنسخة",
            "حفظ نتيجة المعاينة",
            "حفظ الإسناد أو الحجر",
            "تسجيل تطبيق الحزمة",
            "حفظ إعدادات العزل والثقة",
            "حفظ نتيجة المصالحة",
        ),
        store_reads=((2, "خطة قابلة للتطبيق"), (5, "إعدادات الجهة")),
        extra_flows=extra_flows,
    )


def organization_workforce_page(mxfile: ET.Element) -> None:
    actors = [
        Node("a1", "external", "التخطيط والإدارة المركزية", 2400, 155, 400, 92),
        Node("a2", "external", "مالك الجهة", 1960, 155, 320, 92),
        Node("a3", "external", "إدارة الفرع", 1130, 155, 320, 92),
        Node("a4", "external", "المالية والتخطيط", 690, 155, 320, 92),
        Node("a5", "external", "الإدارة العليا والمدقق", 210, 155, 400, 92),
    ]
    external_flows = [
        Flow("a1", "p1", "قرار هيكلي وتاريخ نفاذ", (0.5, 1.0), (0.65, 0.0)),
        Flow("a2", "p2", "اعتماد شجرة الوحدات", (0.5, 1.0), (0.65, 0.0)),
        Flow("a3", "p3", "احتياج ووصف وظيفي", (0.5, 1.0), (0.65, 0.0)),
        Flow("a4", "p4", "سقف تمويل ومراكز كلفة", (0.5, 1.0), (0.65, 0.0)),
        Flow("p6", "a5", "لقطة هيكل وملاك وفجوة", (0.35, 0.0), (0.5, 1.0)),
    ]
    extra_flows = [
        Flow("d2", "p5", "شجرة فعالة ونطاقات", (0.0, 0.50), (1.0, 0.72)),
        Flow(
            "d4",
            "p5",
            "وظائف ممولة وإشغال",
            (0.35, 1.0),
            (0.65, 1.0),
            ((1260, 1010), (820, 1010)),
        ),
    ]
    pipeline_page(
        mxfile,
        4,
        "04 - الهيكل التنظيمي والملاك والوظائف الممولة",
        "تفصيل العملية 2.0 - الهيكل التنظيمي والملاك والوظائف الممولة",
        "هيكل مؤرخ قابل للتدقيق، دليل وظائف، وظائف ممولة، تخطيط قوى عاملة، ولقطات نافذة.",
        (
            "2.1 تسجيل التغيير الهيكلي وتاريخ النفاذ",
            "2.2 بناء OrgUnitVersion والشجرة ونطاقات الفروع",
            "2.3 إدارة دليل الوظائف والتصنيف والوصف",
            "2.4 إنشاء الوظائف الممولة وربط مراكز الكلفة",
            "2.5 إعداد خطة القوى العاملة وقياس الفجوة والإشغال",
            "2.6 نشر اللقطة النافذة والمصالحة والتدقيق",
        ),
        (
            "D2.1 قرارات الهيكل وفترات النفاذ",
            "D2.2 OrgUnitVersion والشجرة والنطاق",
            "D2.3 JobCatalogEntry والوصف والتصنيف",
            "D2.4 FundedPosition والتمويل ومركز الكلفة",
            "D2.5 WorkforcePlan والفجوات والإشغال",
            "D2.6 اللقطات النافذة والمصالحة والتدقيق",
        ),
        (
            "تغيير هيكلي محقق",
            "شجرة ونطاق نافذان",
            "وظيفة مصنفة",
            "وظائف ممولة",
            "خطة وفجوة محسوبتان",
        ),
        actors,
        external_flows,
        (
            "حفظ القرار وفترة النفاذ",
            "حفظ نسخة الوحدة والمسار",
            "حفظ الوظيفة والوصف",
            "حفظ التمويل ومركز الكلفة",
            "حفظ الخطة والفجوة",
            "حفظ اللقطة والمصالحة",
        ),
        store_reads=((1, "قرار نافذ"), (2, "شجرة حالية"), (4, "ملاك ممول"), (5, "خطة وإشغال")),
        extra_flows=extra_flows,
    )


def employee_lifecycle_page(mxfile: ET.Element) -> None:
    actors = [
        Node("a1", "external", "المتقدم وجهة الاستقطاب", 2400, 155, 400, 92),
        Node("a2", "external", "موظف الموارد البشرية", 1570, 155, 320, 92),
        Node("a3", "external", "الموظف وإدارة الفرع", 690, 155, 340, 92),
        Node("a4", "external", "الإدارة العليا والقانونية", 230, 155, 360, 92),
    ]
    interface = Node(
        "support",
        "interface",
        "عمليات مساندة داخل المنصة\nالرواتب والهوية والعهد والأرشيف",
        1050,
        1110,
        720,
        105,
    )
    external_flows = [
        Flow("a1", "p1", "طلب وبيانات مرشح", (0.5, 1.0), (0.65, 0.0)),
        Flow("a2", "p3", "بيانات واعتماد الموظف", (0.5, 1.0), (0.65, 0.0)),
        Flow("a3", "p5", "طلبات خدمة وتحديث", (0.5, 1.0), (0.65, 0.0)),
        Flow("a4", "p2", "قرار تعيين أو اعتماد عقد", (0.5, 1.0), (0.65, 0.0)),
        Flow("p6", "a3", "حالة خدمة وإشعار", (0.0, 0.20), (0.0, 0.70)),
    ]
    extra_flows = [
        Flow(
            "p3",
            "support",
            "موظف ورقم وظيفي",
            (0.45, 1.0),
            (0.82, 0.0),
            ((1700, 1010), (1640, 1010)),
            dashed=True,
        ),
        Flow(
            "p4",
            "support",
            "عقد وبدء خدمة",
            (0.45, 1.0),
            (0.58, 0.0),
            ((1280, 1040), (1470, 1040)),
            dashed=True,
        ),
        Flow(
            "p6",
            "support",
            "لقطة خدمة وأوامر تفعيل",
            (0.45, 1.0),
            (0.18, 0.0),
            ((400, 1280), (1180, 1280)),
            dashed=True,
        ),
        Flow(
            "support",
            "p6",
            "نتائج الهوية والراتب والأرشفة",
            (0.10, 0.0),
            (0.68, 1.0),
            ((1120, 1330), (455, 1330)),
            dashed=True,
        ),
    ]
    pipeline_page(
        mxfile,
        5,
        "05 - التوظيف والموظف والعقود والخدمة",
        "تفصيل العملية 3.0 - التوظيف والموظف والعقود والخدمة",
        "من التوظيف إلى إنشاء الملف والعقد والمباشرة، ثم إدارة معاملات الخدمة ونشر حالتها.",
        (
            "3.1 إدارة رحلة التعيين والتعاقد",
            "3.2 إنشاء الملف الرئيسي والرقم والسيرة المهنية",
            "3.3 إنشاء العقد وتسجيل المباشرة",
            "3.4 إدارة تعديل العقد وتجديده وتعليقه",
            "3.5 إدارة معاملات الخدمة والسيرة الوظيفية",
            "3.6 نشر لقطة حالة الخدمة وتنسيق التفعيل",
        ),
        (
            "D3.1 طلبات التوظيف والمرشحون وقرارات التعيين",
            "D3.2 Employee والرقم والسيرة المهنية",
            "D3.3 Contract وCommencement",
            "D3.4 تعديلات العقود والتجديد والتعليق",
            "D3.5 معاملات الخدمة والتاريخ الوظيفي",
            "D3.6 لقطة الخدمة ونتائج التفعيل",
        ),
        (
            "قرار تعيين وتعاقد",
            "EmployeeId ورقم وظيفي",
            "عقد فعال ومباشرة",
            "عقد محدث",
            "سجل خدمة مكتمل",
        ),
        actors,
        external_flows,
        (
            "حفظ الطلب والقرار",
            "حفظ الموظف والسيرة",
            "حفظ العقد والمباشرة",
            "حفظ تعديل العقد",
            "حفظ معاملة الخدمة",
            "حفظ اللقطة ونتائج التفعيل",
        ),
        store_reads=((1, "بيانات المرشح والقرار"), (2, "هوية الموظف"), (3, "حالة العقد"), (5, "سجل الخدمة")),
        extra_nodes=[interface],
        extra_flows=extra_flows,
    )


def movements_workforce_page(mxfile: ET.Element) -> None:
    actors = [
        Node("a1", "external", "التخطيط والإدارة المركزية", 2400, 155, 400, 92),
        Node("a2", "external", "الفرع المصدر", 1570, 155, 320, 92),
        Node("a3", "external", "الفرع الهدف", 1130, 155, 320, 92),
        Node("a4", "external", "صاحب الصلاحية", 690, 155, 320, 92),
        Node("a5", "external", "الموظف والمدقق", 230, 155, 360, 92),
    ]
    interface = Node(
        "impact",
        "interface",
        "عمليات العقود والرواتب والعهد",
        1060,
        1105,
        700,
        100,
    )
    external_flows = [
        Flow("a1", "p1", "خطة وملاك معتمد", (0.5, 1.0), (0.65, 0.0)),
        Flow("a2", "p3", "طلب نقل أو ندب", (0.5, 1.0), (0.65, 0.0)),
        Flow("a3", "p4", "قبول الوحدة الهدف", (0.5, 1.0), (0.65, 0.0)),
        Flow("a4", "p5", "اعتماد أو رفض", (0.5, 1.0), (0.65, 0.0)),
        Flow("p6", "a5", "قرار وإشعار وأثر تدقيق", (0.35, 0.0), (0.5, 1.0)),
    ]
    extra_flows = [
        Flow(
            "p6",
            "impact",
            "أثر حركة معتمد",
            (0.45, 1.0),
            (0.18, 0.0),
            ((410, 1260), (1190, 1260)),
            dashed=True,
        ),
        Flow(
            "impact",
            "p6",
            "نتائج تحديث الآثار",
            (0.10, 0.0),
            (0.68, 1.0),
            ((1130, 1320), (455, 1320)),
            dashed=True,
        ),
        Flow("d2", "p4", "وظيفة ممولة متاحة", (0.0, 0.50), (1.0, 0.75)),
    ]
    pipeline_page(
        mxfile,
        5,
        "05 - الحركات والملاك والتخطيط",
        "تفصيل العملية 3.0 - الحركات والملاك والتخطيط",
        "تخطيط القوى العاملة والوظائف الممولة والتحقق من نطاق المصدر والهدف وتنفيذ الحركة.",
        (
            "3.1 إعداد خطة القوى العاملة وقياس الفجوة",
            "3.2 إدارة الوظائف ودليل الوظائف الممولة",
            "3.3 تسجيل طلب النقل أو الندب أو الإعارة",
            "3.4 التحقق من النطاق والأهلية وقبول الهدف",
            "3.5 تنفيذ الموافقات وفصل الواجبات",
            "3.6 تنفيذ الحركة وتثبيت اللقطة والمصالحة",
        ),
        (
            "D3.1 WorkforcePlan وفجوة الملاك",
            "D3.2 JobCatalogEntry وFundedPosition",
            "D3.3 طلبات النقل والندب والإعارة",
            "D3.4 لقطة النطاق والأهلية",
            "D3.5 مسار الموافقة والتوقيعات",
            "D3.6 الحركة واللقطة ونتيجة المصالحة",
        ),
        (
            "احتياج ملاك",
            "وظيفة ممولة متاحة",
            "طلب حركة",
            "طلب مؤهل",
            "قرار نافذ",
        ),
        actors,
        external_flows,
        (
            "حفظ الخطة والفجوة",
            "حفظ الوظيفة والتمويل",
            "حفظ الطلب",
            "حفظ لقطة التحقق",
            "حفظ القرار والتوقيع",
            "حفظ الحركة والمصالحة",
        ),
        store_reads=((1, "بيانات الخطة"), (2, "الملاك المتاح"), (3, "تفاصيل الطلب"), (5, "قرار الموافقة")),
        extra_nodes=[interface],
        extra_flows=extra_flows,
    )


def employee_services_page(mxfile: ET.Element) -> None:
    nodes = [
        Node("p1", "process", "4.1 إدارة طلبات الإجازة والسلفة والخدمة الذاتية", 2250, 345, 470, 115),
        Node("p2", "process", "4.2 إدارة التدريب والدورات والبعثات", 1265, 345, 470, 115),
        Node("p3", "process", "4.3 إدارة الصحة المهنية واللجان الطبية", 280, 345, 470, 115),
        Node("d1", "store", "D4.1 طلبات ESS والأرصدة والسلف", 2320, 560, 330, 86),
        Node("d2", "store", "D4.2 الخطط والترشيحات والنتائج", 1335, 560, 330, 86),
        Node("d3", "store", "D4.3 الملفات والقرارات الطبية", 350, 560, 330, 86),
        Node("p4", "process", "4.4 تقييم السياسة وبدء مسار الموافقة", 1710, 860, 470, 115),
        Node("d4", "store", "D4.4 القرار ومثيل سير العمل", 1780, 1075, 330, 86),
        Node("p5", "process", "4.5 تطبيق الأثر الوظيفي والمالي المسموح", 960, 860, 470, 115),
        Node("d5", "store", "D4.5 آثار الخدمة والراتب", 1030, 1075, 330, 86),
        Node("p6", "process", "4.6 الإشعار وتحديث السيرة والأرشفة", 210, 860, 470, 115),
        Node("d6", "store", "D4.6 الإشعارات والسيرة والأدلة", 280, 1075, 330, 86),
        Node("a1", "external", "الموظف وإدارة الفرع", 2260, 150, 450, 92),
        Node("a2", "external", "جهات التدريب والوزارات", 1275, 150, 450, 92),
        Node("a3", "external", "الصحة المهنية واللجان الطبية", 290, 150, 450, 92),
        Node("a4", "external", "المدير أو المعتمد", 1715, 1335, 460, 92),
        Node("a5", "external", "الموظف والعمليات المستهلكة", 250, 1335, 520, 92),
    ]
    flows = [
        Flow("a1", "p1", "طلب إجازة أو سلفة", (0.5, 1.0), (0.65, 0.0)),
        Flow("a2", "p2", "نتيجة وشهادة أو متطلبات بعثة", (0.5, 1.0), (0.65, 0.0)),
        Flow("p2", "a2", "ترشيح وقائمة مرشحين", (0.35, 0.0), (0.35, 1.0)),
        Flow("a3", "p3", "تقرير أو قرار لجنة", (0.5, 1.0), (0.65, 0.0)),
        store_write("p1", "d1", "حفظ الطلب والحالة"),
        store_read("d1", "p1", "الرصيد والسجل"),
        store_write("p2", "d2", "حفظ الخطة والنتيجة"),
        store_read("d2", "p2", "الخطة والترشيحات"),
        store_write("p3", "d3", "حفظ الملف والقرار"),
        store_read("d3", "p3", "ملف صحي مخول"),
        Flow("p1", "p4", "حقائق طلب الخدمة", (0.35, 1.0), (0.82, 0.0)),
        Flow("p2", "p4", "حقائق الترشيح أو البعثة", (0.50, 1.0), (0.50, 0.0)),
        Flow("p3", "p4", "أثر لياقة مختصر", (0.65, 1.0), (0.18, 0.0)),
        Flow("a4", "p4", "موافقة أو رفض", (0.5, 0.0), (0.65, 1.0)),
        store_write("p4", "d4", "حفظ القرار والمسار"),
        store_read("d4", "p4", "الخطوة الحالية"),
        Flow("p4", "p5", "قرار معتمد", (0.0, 0.50), (1.0, 0.50)),
        store_write("p5", "d5", "حفظ الأثر"),
        store_read("d5", "p5", "نتائج التطبيق"),
        Flow("p5", "p6", "نتيجة التطبيق", (0.0, 0.50), (1.0, 0.50)),
        store_write("p6", "d6", "حفظ الإشعار والدليل"),
        Flow("p6", "a5", "قرار وإشعار وحدث", (0.5, 1.0), (0.5, 0.0)),
    ]
    add_page(
        mxfile,
        6,
        "06 - الخدمة الذاتية والتدريب والصحة",
        "تفصيل العملية 4.0 - الخدمة الذاتية والتدريب والصحة",
        "ثلاثة مسارات مستقلة تلتقي عند السياسة والموافقة، ثم يطبق الأثر المسموح فقط.",
        nodes,
        flows,
    )


def payroll_domain_page(mxfile: ET.Element) -> None:
    actors = [
        Node("a1", "external", "مشغل الرواتب", 2450, 155, 300, 92),
        Node("a2", "external", "المعتمد المالي", 1570, 155, 320, 92),
        Node("a3", "external", "الخزينة", 690, 155, 300, 92),
        Node("a4", "external", "المصرف والامتثال", 220, 155, 380, 92),
    ]
    external_flows = [
        Flow("a1", "p1", "دورة وموعد قطع", (0.5, 1.0), (0.65, 0.0)),
        Flow("a2", "p3", "اعتماد أو رفض", (0.5, 1.0), (0.65, 0.0)),
        Flow("a3", "p4", "قرار تمويل", (0.5, 1.0), (0.65, 0.0)),
        Flow("p5", "a3", "أوامر دفع", (0.35, 0.0), (0.35, 1.0)),
        Flow("a4", "p6", "كشف مصرفي أو قرار حجز", (0.5, 1.0), (0.65, 0.0)),
        Flow("p6", "a4", "تسوية وتنبيه", (0.35, 0.0), (0.35, 1.0)),
    ]
    extra_flows = [
        Flow(
            "d6",
            "p4",
            "متأخرات سابقة",
            (0.75, 0.0),
            (0.25, 1.0),
            ((470, 1010), (1250, 1010)),
        ),
        Flow(
            "p6",
            "d4",
            "قيد متأخرات أو حجز",
            (0.70, 1.0),
            (0.72, 1.0),
            ((470, 1060), (1270, 1060)),
        ),
    ]
    pipeline_page(
        mxfile,
        7,
        "07 - الرواتب والتمويل والدفع",
        "تفصيل العملية 5.0 - الرواتب والتمويل والدفع",
        "لقطة مدخلات ثابتة، حساب متواز قابل للاستئناف، ترحيل مستقل، تمويل وصرف وتسوية وحجز.",
        (
            "5.1 حل إصدار القاعدة وتجميد لقطة المدخلات",
            "5.2 تنفيذ الحساب المقسم ونقاط التحقق",
            "5.3 التحقق والاعتماد والترحيل",
            "5.4 تحديد التمويل والصرف الجزئي والمتأخرات",
            "5.5 إنشاء أوامر الدفع والإرسال",
            "5.6 التسوية والعكس والتنبيه والحجز",
        ),
        (
            "D5.1 PayrollConfiguration وInputSnapshot",
            "D5.2 RunJob وPartitions وSalaryLines",
            "D5.3 PayrollRun وPaymentBatch",
            "D5.4 FundingDecision وArrearsLedger",
            "D5.5 PaymentOrder وحالة الإرسال",
            "D5.6 BankStatement وFraudAlert وPaymentHold",
        ),
        (
            "لقطة وبصمة",
            "نتيجة حساب قابلة للاستئناف",
            "دفعة مرحلة",
            "خطة صرف ممولة",
            "أوامر مرسلة",
        ),
        actors,
        external_flows,
        (
            "حفظ الإصدار واللقطة",
            "حفظ المهام والأسطر",
            "حفظ الاعتماد والترحيل",
            "حفظ التمويل والمتأخرات",
            "حفظ أوامر الدفع",
            "حفظ التسوية والتنبيهات",
        ),
        store_reads=(
            (1, "إصدار منشور ومدخلات"),
            (2, "نقطة استئناف"),
            (3, "دفعة معتمدة"),
            (4, "أولوية ومتأخرات"),
            (5, "أوامر مفتوحة"),
        ),
        extra_flows=extra_flows,
    )


def logistics_domain_page(mxfile: ET.Element) -> None:
    actors = [
        Node("a1", "external", "موظف التوريد", 2450, 155, 300, 92),
        Node("a2", "external", "المورد", 2010, 155, 300, 92),
        Node("a3", "external", "أمين المستودع", 1570, 155, 300, 92),
        Node("a4", "external", "مسؤول الأصول", 690, 155, 300, 92),
        Node("a5", "external", "المستلم والامتثال", 210, 155, 400, 92),
    ]
    external_flows = [
        Flow("a1", "p1", "طلب وأمر توريد", (0.5, 1.0), (0.65, 0.0)),
        Flow("p1", "a2", "أمر وكميات", (0.35, 0.0), (0.35, 1.0)),
        Flow("a2", "p2", "شحنة وفاتورة", (0.5, 1.0), (0.65, 0.0)),
        Flow("a3", "p2", "محضر وفحص كمية", (0.0, 0.50), (1.0, 0.65)),
        Flow("a4", "p5", "اعتماد عهدة", (0.5, 1.0), (0.65, 0.0)),
        Flow("a5", "p6", "إثبات تسليم أو بلاغ فقد", (0.5, 1.0), (0.65, 0.0)),
        Flow("p6", "a5", "حالة عهدة وإشعار", (0.35, 0.0), (0.35, 1.0)),
    ]
    extra_flows = [
        Flow(
            "d3",
            "p5",
            "أصل متاح للعهدة",
            (0.35, 1.0),
            (0.65, 1.0),
            ((1690, 980), (820, 980)),
        ),
        Flow(
            "d4",
            "p6",
            "رمز الأصل وسلسلة المصدر",
            (0.35, 1.0),
            (0.65, 1.0),
            ((1250, 1040), (380, 1040)),
        ),
    ]
    pipeline_page(
        mxfile,
        8,
        "08 - اللوجستيات والمخزون والعهد",
        "تفصيل العملية 6.0 - اللوجستيات والمخزون والعهد",
        "تتبع خط أمر التوريد إلى الاستلام ثم المخزون أو الأصل فالترميز والعهدة والفقد.",
        (
            "6.1 إنشاء أمر التوريد وخطوطه",
            "6.2 تسجيل الاستلام والتحقق من الكمية",
            "6.3 زيادة المخزون أو إنشاء أصل",
            "6.4 توليد رمز الأصل وربط سلسلة المصدر",
            "6.5 طلب العهدة واعتمادها",
            "6.6 التسليم أو الإرجاع أو تسجيل الفقد",
        ),
        (
            "D6.1 SupplyOrder وSupplyOrderLine",
            "D6.2 Receipt والبنود المقبولة",
            "D6.3 Inventory وAsset",
            "D6.4 AssetCode وLineage",
            "D6.5 AssetAssignment والموافقات",
            "D6.6 أدلة التسليم وAssetLossReport",
        ),
        (
            "أمر مفتوح",
            "بنود مستلمة",
            "مخزون أو أصل",
            "أصل مرمز",
            "عهدة معتمدة",
        ),
        actors,
        external_flows,
        (
            "حفظ الأمر والخطوط",
            "حفظ محضر الاستلام",
            "حفظ الرصيد أو الأصل",
            "حفظ الرمز والمصدر",
            "حفظ الطلب والموافقة",
            "حفظ الدليل أو الفقد",
        ),
        store_reads=((1, "أمر مفتوح"), (3, "رصيد وأصل"), (5, "عهدة معتمدة")),
        extra_flows=extra_flows,
    )


def identity_files_compliance_page(mxfile: ET.Element) -> None:
    actors = [
        Node("a1", "external", "الموظف أو خدمة المجال", 2400, 155, 400, 92),
        Node("a2", "external", "محرك فحص الملفات", 1570, 155, 340, 92),
        Node("a3", "external", "جهة مخولة بطلب الوثيقة", 690, 155, 400, 92),
        Node("a4", "external", "مسؤول الخصوصية والمدقق", 210, 155, 400, 92),
    ]
    external_flows = [
        Flow("a1", "p1", "طلب بطاقة أو ملف أو وثيقة", (0.5, 1.0), (0.65, 0.0)),
        Flow("p2", "a2", "محتوى محجور للفحص", (0.35, 0.0), (0.35, 1.0)),
        Flow("a2", "p3", "نتيجة فحص وCDR", (0.5, 1.0), (0.65, 0.0)),
        Flow("a3", "p4", "طلب وصول مخول", (0.5, 1.0), (0.65, 0.0)),
        Flow("p4", "a3", "رابط مؤقت أو رفض", (0.35, 0.0), (0.35, 1.0)),
        Flow("a4", "p6", "طلب DSAR أو مراجعة", (0.5, 1.0), (0.65, 0.0)),
        Flow("p6", "a4", "نتيجة تنفيذ وأثر تدقيق", (0.35, 0.0), (0.35, 1.0)),
    ]
    extra_flows = [
        Flow(
            "d3",
            "p4",
            "ملف نشط وبصمة",
            (0.0, 0.50),
            (1.0, 0.72),
        ),
        Flow(
            "d5",
            "p6",
            "سجل احتفاظ وأرشفة",
            (0.35, 1.0),
            (0.65, 1.0),
            ((820, 1000), (380, 1000)),
        ),
        Flow(
            "d4",
            "p6",
            "سجل المنح والوصول",
            (0.35, 1.0),
            (0.65, 1.0),
            ((1260, 1070), (450, 1070)),
        ),
    ]
    pipeline_page(
        mxfile,
        9,
        "09 - الهوية والملفات والأرشيف والامتثال",
        "تفصيل العملية 7.0 - الهوية والملفات والأرشيف والامتثال",
        "من الطلب إلى الحجر والفحص والترقية والمنح المؤقتة والاحتفاظ وحقوق صاحب البيانات.",
        (
            "7.1 تسجيل طلب البطاقة أو الملف أو الوثيقة",
            "7.2 رفع المحتوى والتحقق الأولي والحجر",
            "7.3 الفحص والتنقية وترقية الملف النظيف",
            "7.4 إصدار البطاقة أو منحة الوصول المؤقت",
            "7.5 الأرشفة وتطبيق سياسة الاحتفاظ",
            "7.6 تنفيذ DSAR والشكاوى والتدقيق",
        ),
        (
            "D7.1 الطلبات والهوية المشفرة",
            "D7.2 FileMetadata ومحتوى الحجر",
            "D7.3 الملف النشط وSHA-256",
            "D7.4 البطاقات وAccessGrant",
            "D7.5 ArchiveItem وRetentionPolicy",
            "D7.6 DSAR والشكاوى وسجل التدقيق",
        ),
        (
            "طلب مسجل",
            "FileId محجور",
            "ملف نظيف نشط",
            "بطاقة أو منحة",
            "سجل مؤرشف",
        ),
        actors,
        external_flows,
        (
            "حفظ الطلب والهوية",
            "حفظ حالة الحجر",
            "حفظ الملف والبصمة",
            "حفظ البطاقة أو المنحة",
            "حفظ الأرشيف والاحتفاظ",
            "حفظ الطلب والنتيجة والأثر",
        ),
        store_reads=((1, "هوية وطلب"), (2, "محتوى محجور"), (4, "منح فعالة"), (6, "قضايا مفتوحة")),
        extra_flows=extra_flows,
    )


def governance_engines_page(mxfile: ET.Element) -> None:
    nodes = [
        Node("p1", "process", "8.1 إعداد إصدار السياسة وقواعدها", 2320, 330, 430, 112),
        Node("d1", "store", "D8.1 PolicyTemplateVersion وPolicyRule", 2370, 535, 330, 86),
        Node("p2", "process", "8.2 المحاكاة والنشر والتقييم والإنفاذ", 1880, 835, 430, 112),
        Node("d2", "store", "D8.2 SimulationRun وEnforcementLedger", 1930, 1040, 330, 86),
        Node("p3", "process", "8.3 تعريف سير العمل وبدء المثيل", 1320, 330, 430, 112),
        Node("d3", "store", "D8.3 WorkflowDefinition وInstance", 1370, 535, 330, 86),
        Node("p4", "process", "8.4 الموافقة والتوقيع والتفويض", 1080, 835, 430, 112),
        Node("d4", "store", "D8.4 Signature وDelegation وDecision", 1130, 1040, 330, 86),
        Node("p5", "process", "8.5 معاينة الرمز وتوليده ذرياً", 320, 330, 430, 112),
        Node("d5", "store", "D8.5 CodePolicyVersion وCounter وIssuance", 370, 535, 330, 86),
        Node("p6", "process", "8.6 نشر الأحداث وبناء النماذج والمصالحة", 280, 835, 430, 112),
        Node("d6", "store", "D8.6 Outbox وInbox وReadModel وReconciliation", 330, 1040, 330, 86),
        Node("a1", "external", "مسؤول السياسات", 2350, 145, 360, 90),
        Node("a2", "external", "خدمة المجال", 1320, 145, 430, 90),
        Node("a3", "external", "المعتمد والمفوّض", 1080, 1315, 430, 90),
        Node("a4", "external", "التحليلات والخدمات المستهلكة", 230, 1315, 540, 90),
    ]
    flows = [
        Flow("a1", "p1", "مسودة وقواعد", (0.5, 1.0), (0.65, 0.0)),
        store_write("p1", "d1", "حفظ إصدار غير قابل للتعديل"),
        store_read("d1", "p2", "إصدار مرشح أو منشور"),
        Flow("a2", "p2", "حقائق وطلب قرار", (0.80, 1.0), (0.65, 0.0)),
        store_write("p2", "d2", "حفظ المحاكاة والإنفاذ"),
        Flow("p2", "a2", "Allow أو Deny ومتطلبات موافقة", (1.0, 0.20), (1.0, 0.70)),
        Flow("p2", "p3", "ربط سياسة يتطلب موافقة", (0.0, 0.25), (1.0, 0.75), dashed=True),
        Flow("a2", "p3", "بدء مسار عمل", (0.35, 1.0), (0.65, 0.0)),
        store_write("p3", "d3", "حفظ التعريف والمثيل"),
        store_read("d3", "p4", "الخطوة الحالية"),
        Flow("a3", "p4", "موافقة أو رفض أو تفويض", (0.5, 0.0), (0.65, 1.0)),
        store_write("p4", "d4", "حفظ القرار والتوقيع"),
        Flow("p4", "a2", "قرار سير العمل", (0.80, 0.0), (0.25, 1.0)),
        Flow("a2", "p5", "نوع الكيان وسياق الرمز", (0.0, 0.40), (1.0, 0.40)),
        store_write("p5", "d5", "حفظ الإصدار والعداد والإصدار"),
        store_read("d5", "p5", "سياسة وعدّاد"),
        Flow("p5", "a2", "رمز مولد", (1.0, 0.72), (0.0, 0.72)),
        Flow("p2", "p6", "PolicyEnforced", (0.0, 0.70), (1.0, 0.18)),
        Flow("p4", "p6", "WorkflowDecided", (0.0, 0.55), (1.0, 0.50)),
        Flow("p5", "p6", "CodeIssued", (0.45, 1.0), (0.75, 0.0)),
        store_write("p6", "d6", "حفظ الرسالة والقراءة"),
        store_read("d6", "p6", "رسائل معلقة وحالات تسوية"),
        Flow("p6", "a4", "أحداث ونماذج قراءة وتقارير", (0.5, 1.0), (0.5, 0.0)),
    ]
    add_page(
        mxfile,
        10,
        "10 - السياسات وسير العمل والرموز والتكامل",
        "تفصيل العملية 8.0 - محركات الحوكمة والتكامل",
        "السياسة وسير العمل وتوليد الرموز فروع مستقلة تتبادل حقائق محددة وتتلاقى في الأحداث والمصالحة.",
        nodes,
        flows,
        scope_note=(
            "يحل كل طلب TenantId والإصدار المنشور قبل التقييم أو التوليد. "
            "تسجل قرارات الإنفاذ والتوقيعات وإصدارات الرموز والأحداث بأثر تدقيق مترابط."
        ),
    )


def movements_separation_page(mxfile: ET.Element) -> None:
    nodes = [
        Node("p1", "process", "4.1 إدارة رحلة النقل والندب والإعارة", 2250, 345, 470, 115),
        Node("p2", "process", "4.2 إدارة رحلة إنهاء الخدمة", 1265, 345, 470, 115),
        Node("p3", "process", "4.3 التحقق من النطاق والأهلية والوظيفة وقائمة الإغلاق", 280, 345, 470, 115),
        Node("d1", "store", "D4.1 طلبات الحركة ولقطة المصدر", 2320, 560, 330, 86),
        Node("d2", "store", "D4.2 طلبات الفصل والأسباب والتسويات", 1335, 560, 330, 86),
        Node("d3", "store", "D4.3 النطاق والأهلية والوظيفة وقائمة الإغلاق", 350, 560, 330, 86),
        Node("p4", "process", "4.4 تنفيذ الموافقات وفصل الواجبات والتوقيع", 1710, 860, 470, 115),
        Node("d4", "store", "D4.4 Workflow وSignature وDecision", 1780, 1075, 330, 86),
        Node("p5", "process", "4.5 تطبيق الحركة أو الفصل وتنسيق الآثار", 960, 860, 470, 115),
        Node("d5", "store", "D4.5 MovementSnapshot وSeparation ونتائج الآثار", 1030, 1075, 330, 86),
        Node("p6", "process", "4.6 المصالحة ونشر الحدث والإشعار والتدقيق", 210, 860, 470, 115),
        Node("d6", "store", "D4.6 Outbox والمصالحة والإشعارات والتدقيق", 280, 1075, 330, 86),
        Node("a1", "external", "الموظف أو الفرع المصدر", 2260, 150, 450, 92),
        Node("a2", "external", "الموارد البشرية والقانونية", 1275, 150, 450, 92),
        Node("a3", "external", "الفرع الهدف والإدارة المركزية", 290, 150, 450, 92),
        Node("a4", "external", "صاحب الصلاحية", 1715, 1335, 460, 92),
        Node("a5", "external", "الموظف والفرعان والمدقق", 250, 1335, 520, 92),
        Node("impact", "interface", "عمليات العقود والرواتب والعهد والهوية", 930, 1280, 560, 90),
    ]
    flows = [
        Flow("a1", "p1", "طلب نقل أو ندب أو إعارة", (0.5, 1.0), (0.65, 0.0)),
        Flow("a2", "p2", "طلب فصل وسبب ورأي قانوني", (0.5, 1.0), (0.65, 0.0)),
        Flow("a3", "p3", "قبول هدف أو بيانات وظيفة", (0.5, 1.0), (0.65, 0.0)),
        store_write("p1", "d1", "حفظ طلب الحركة والمصدر"),
        store_read("d1", "p1", "تفاصيل الطلب"),
        store_write("p2", "d2", "حفظ طلب الفصل والتسوية"),
        store_read("d2", "p2", "الأسباب والحالة"),
        store_write("p3", "d3", "حفظ لقطة التحقق"),
        store_read("d3", "p3", "النطاق والوظيفة والقائمة"),
        Flow("p1", "p3", "طلب حركة ولقطة مصدر", (0.35, 1.0), (0.82, 0.0)),
        Flow("p2", "p3", "طلب فصل وقائمة إغلاق", (0.50, 1.0), (0.50, 0.0)),
        Flow("p3", "p4", "معاملة مؤهلة ومسار مطلوب", (0.65, 1.0), (0.18, 0.0)),
        Flow("a4", "p4", "موافقة أو رفض أو تفويض", (0.5, 0.0), (0.65, 1.0)),
        store_write("p4", "d4", "حفظ القرار والتوقيع"),
        store_read("d4", "p4", "الخطوة والقرار"),
        Flow("p4", "p5", "قرار نافذ", (0.0, 0.50), (1.0, 0.50)),
        store_write("p5", "d5", "حفظ التنفيذ والآثار"),
        store_read("d5", "p5", "نتائج التنفيذ"),
        Flow("p5", "impact", "أوامر تحديث الآثار", (0.45, 1.0), (0.65, 0.0), dashed=True),
        Flow("impact", "p5", "نتائج تحديث الآثار", (0.35, 0.0), (0.35, 1.0), dashed=True),
        Flow("p5", "p6", "نتيجة حركة أو فصل", (0.0, 0.50), (1.0, 0.50)),
        store_write("p6", "d6", "حفظ الحدث والمصالحة"),
        store_read("d6", "p6", "حالات مفتوحة"),
        Flow("p6", "a5", "قرار وإشعار وأثر تدقيق", (0.5, 1.0), (0.5, 0.0)),
    ]
    add_page(
        mxfile,
        6,
        "06 - الحركات والتوزيع وإنهاء الخدمة",
        "تفصيل العملية 4.0 - الحركات والتوزيع وإنهاء الخدمة",
        "مساران بديلان للحركة أو الفصل يلتقيان عند التحقق والموافقة وتنسيق الآثار والمصالحة.",
        nodes,
        flows,
    )


def employee_self_service_page(mxfile: ET.Element) -> None:
    actors = [
        Node("a1", "external", "الموظف عبر الخدمة الذاتية", 2400, 155, 400, 92),
        Node("a2", "external", "مدير الفرع", 1960, 155, 340, 92),
        Node("a3", "external", "المعتمد المالي", 1130, 155, 340, 92),
        Node("a4", "external", "الموظف والمدقق", 210, 155, 360, 92),
    ]
    support = Node("impact", "interface", "عمليات الرواتب والسجل الوظيفي والأرشيف", 1040, 1120, 720, 95)
    external_flows = [
        Flow("a1", "p1", "طلب إجازة أو سلفة ومرفقات", (0.5, 1.0), (0.65, 0.0)),
        Flow("a2", "p4", "موافقة إدارية", (0.5, 1.0), (0.65, 0.0)),
        Flow("a3", "p4", "موافقة مالية عند اللزوم", (0.5, 1.0), (0.35, 0.0)),
        Flow("p6", "a4", "قرار وإشعار وأثر تدقيق", (0.35, 0.0), (0.5, 1.0)),
    ]
    extra_flows = [
        Flow("p5", "impact", "أثر خدمة أو استقطاع", (0.45, 1.0), (0.65, 0.0), dashed=True),
        Flow("impact", "p5", "نتيجة التطبيق", (0.35, 0.0), (0.35, 1.0), dashed=True),
    ]
    pipeline_page(
        mxfile,
        7,
        "07 - الخدمة الذاتية والإجازات والسلف",
        "تفصيل العملية 5.0 - الخدمة الذاتية والإجازات والسلف",
        "طلب ذاتي مقيد بالنطاق والرصيد والسياسة، يمر بالموافقة ثم يطبق أثره ويؤرشف.",
        (
            "5.1 إدارة رحلة الإجازة أو السلفة",
            "5.2 التحقق من النطاق والرصيد والأهلية",
            "5.3 تقييم القواعد والسياسة والقيود المالية",
            "5.4 تنفيذ مسار الموافقة الإدارية والمالية",
            "5.5 تطبيق الرصيد وأثر الخدمة أو الاستقطاع",
            "5.6 الإشعار والأرشفة والتدقيق",
        ),
        (
            "D5.1 الطلبات والمرفقات",
            "D5.2 الأرصدة ولقطة الأهلية",
            "D5.3 PolicyEvaluation ونتيجة القواعد",
            "D5.4 Workflow والموافقات والقرار",
            "D5.5 أثر الرصيد والخدمة والراتب",
            "D5.6 الإشعارات والأرشيف والتدقيق",
        ),
        ("طلب مكتمل", "طلب مؤهل", "قرار سياسة", "قرار نافذ", "أثر مطبق"),
        actors,
        external_flows,
        (
            "حفظ الطلب والمرفقات",
            "حفظ لقطة الرصيد والأهلية",
            "حفظ نتيجة السياسة",
            "حفظ الموافقات والقرار",
            "حفظ الأثر",
            "حفظ الإشعار والدليل",
        ),
        store_reads=((1, "تفاصيل الطلب"), (2, "الرصيد والأهلية"), (4, "الخطوة الحالية"), (5, "نتيجة التطبيق")),
        extra_nodes=[support],
        extra_flows=extra_flows,
    )


def training_health_domain_page(mxfile: ET.Element) -> None:
    nodes = [
        Node("p1", "process", "6.1 إدارة رحلة التدريب أو البعثة", 2250, 345, 470, 115),
        Node("p2", "process", "6.2 إدارة رحلة الصحة المهنية والقرار الطبي", 1265, 345, 470, 115),
        Node("p3", "process", "6.3 التحقق من الأهلية والموافقة ومنح الوصول", 280, 345, 470, 115),
        Node("d1", "store", "D6.1 الخطط والدورات والبعثات والترشيحات", 2320, 560, 330, 86),
        Node("d2", "store", "D6.2 الإحالات والزيارات والملفات الطبية", 1335, 560, 330, 86),
        Node("d3", "store", "D6.3 الأهلية والموافقات ومنح الوصول", 350, 560, 330, 86),
        Node("p4", "process", "6.4 تنفيذ الاعتماد أو قرار اللجنة", 1710, 860, 470, 115),
        Node("d4", "store", "D6.4 الموافقات وقرارات اللجان والتوقيعات", 1780, 1075, 330, 86),
        Node("p5", "process", "6.5 تسجيل الحضور والنتيجة أو التوصية المهنية", 960, 860, 470, 115),
        Node("d5", "store", "D6.5 النتائج والشهادات والتوصيات", 1030, 1075, 330, 86),
        Node("p6", "process", "6.6 تحديث الكفاءة والأثر المسموح والأرشفة", 210, 860, 470, 115),
        Node("d6", "store", "D6.6 الكفاءات وآثار الخدمة والأدلة والتدقيق", 280, 1075, 330, 86),
        Node("a1", "external", "الموظف ومسؤول التدريب", 2260, 150, 450, 92),
        Node("a2", "external", "الصحة المهنية والكادر المخول", 1275, 150, 450, 92),
        Node("a3", "external", "جهة التدريب أو اللجنة الطبية", 290, 150, 450, 92),
        Node("a4", "external", "الإدارة العليا والمعتمد", 1715, 1335, 460, 92),
        Node("a5", "external", "الموظف والموارد البشرية والمدقق", 250, 1335, 520, 92),
    ]
    flows = [
        Flow("a1", "p1", "احتياج أو ترشيح أو طلب بعثة", (0.5, 1.0), (0.65, 0.0)),
        Flow("a2", "p2", "إحالة أو زيارة أو نتيجة صحية", (0.5, 1.0), (0.65, 0.0)),
        Flow("a3", "p3", "متطلبات تنفيذ أو قرار لجنة", (0.5, 1.0), (0.65, 0.0)),
        store_write("p1", "d1", "حفظ الخطة والترشيح"),
        store_read("d1", "p1", "الخطة والفرصة"),
        store_write("p2", "d2", "حفظ الإحالة والزيارة"),
        store_read("d2", "p2", "ملف صحي مخول"),
        store_write("p3", "d3", "حفظ الأهلية والمنحة"),
        store_read("d3", "p3", "موافقة ومنحة فعالة"),
        Flow("p1", "p3", "مرشح وخطة أو بعثة", (0.35, 1.0), (0.82, 0.0)),
        Flow("p2", "p3", "إحالة وهوية وموافقة", (0.50, 1.0), (0.50, 0.0)),
        Flow("p3", "p4", "ملف مؤهل ومسار قرار", (0.65, 1.0), (0.18, 0.0)),
        Flow("a4", "p4", "اعتماد أو قرار لجنة", (0.5, 0.0), (0.65, 1.0)),
        store_write("p4", "d4", "حفظ القرار والتوقيع"),
        store_read("d4", "p4", "قرار نافذ"),
        Flow("p4", "p5", "تنفيذ معتمد", (0.0, 0.50), (1.0, 0.50)),
        store_write("p5", "d5", "حفظ النتيجة أو التوصية"),
        store_read("d5", "p5", "نتيجة مخولة"),
        Flow("p5", "p6", "شهادة أو توصية مهنية مختصرة", (0.0, 0.50), (1.0, 0.50)),
        store_write("p6", "d6", "حفظ الكفاءة والأثر والدليل"),
        store_read("d6", "p6", "أثر مسموح"),
        Flow("p6", "a5", "نتيجة وإشعار وأثر وصول", (0.5, 1.0), (0.5, 0.0)),
    ]
    add_page(
        mxfile,
        8,
        "08 - التدريب والبعثات والصحة المهنية",
        "تفصيل العملية 6.0 - التدريب والبعثات والصحة المهنية",
        "مساران مستقلان يحافظان على خصوصية الصحة ويفصلان التفاصيل السريرية عن الأثر الوظيفي.",
        nodes,
        flows,
    )


def payroll_complete_domain_page(mxfile: ET.Element) -> None:
    actors = [
        Node("a1", "external", "مشغل الرواتب", 2450, 155, 300, 92),
        Node("a2", "external", "المعتمد المالي", 1570, 155, 320, 92),
        Node("a3", "external", "الخزينة", 690, 155, 300, 92),
        Node("a4", "external", "المصرف والامتثال", 220, 155, 380, 92),
    ]
    external_flows = [
        Flow("a1", "p1", "دورة وموعد قطع", (0.5, 1.0), (0.65, 0.0)),
        Flow("a2", "p3", "اعتماد أو رفض", (0.5, 1.0), (0.65, 0.0)),
        Flow("a3", "p4", "قرار تمويل", (0.5, 1.0), (0.65, 0.0)),
        Flow("p5", "a3", "أوامر دفع", (0.35, 0.0), (0.35, 1.0)),
        Flow("a4", "p6", "كشف مصرفي أو قرار حجز", (0.5, 1.0), (0.65, 0.0)),
        Flow("p6", "a4", "تسوية وتنبيه", (0.35, 0.0), (0.35, 1.0)),
    ]
    extra_flows = [
        Flow("d6", "p4", "متأخرات سابقة", (0.75, 0.0), (0.25, 1.0), ((470, 1010), (1250, 1010))),
        Flow("p6", "d4", "قيد متأخرات أو حجز", (0.70, 1.0), (0.72, 1.0), ((470, 1060), (1270, 1060))),
    ]
    pipeline_page(
        mxfile,
        9,
        "09 - الرواتب والتمويل والدفع",
        "تفصيل العملية 7.0 - الرواتب والتمويل والدفع",
        "لقطة ثابتة، تشغيل متواز قابل للاستئناف، اعتماد وترحيل، تمويل وصرف جزئي وتسوية ومكافحة احتيال.",
        (
            "7.1 إدارة رحلة تشغيل الراتب والصرف",
            "7.2 تنفيذ الحساب المقسم ونقاط الاستئناف",
            "7.3 التحقق والاعتماد والترحيل غير القابل للتغيير",
            "7.4 تحديد التمويل والصرف الجزئي والمتأخرات",
            "7.5 إنشاء أوامر الدفع والإرسال",
            "7.6 التسوية والعكس والتنبيه والحجز",
        ),
        (
            "D7.1 PayrollConfiguration وInputSnapshot",
            "D7.2 RunJob وPartitions وSalaryLines",
            "D7.3 PayrollRun وPaymentBatch وإصداراته",
            "D7.4 FundingDecision وArrearsLedger",
            "D7.5 PaymentOrder والحسابات المصرفية",
            "D7.6 BankStatement وFraudAlert وPaymentHold",
        ),
        ("لقطة وبصمة", "نتيجة قابلة للاستئناف", "دفعة مرحلة", "خطة صرف ممولة", "أوامر مرسلة"),
        actors,
        external_flows,
        (
            "حفظ الإصدار واللقطة",
            "حفظ المهام والأسطر",
            "حفظ الاعتماد والترحيل",
            "حفظ التمويل والمتأخرات",
            "حفظ الأوامر والحسابات",
            "حفظ التسوية والتنبيهات",
        ),
        store_reads=((1, "إصدار ومدخلات"), (2, "نقطة استئناف"), (3, "دفعة معتمدة"), (4, "أولوية ومتأخرات"), (5, "أوامر مفتوحة")),
        extra_flows=extra_flows,
    )


def logistics_complete_domain_page(mxfile: ET.Element) -> None:
    actors = [
        Node("a1", "external", "موظف التوريد", 2450, 155, 300, 92),
        Node("a2", "external", "المورد", 2010, 155, 300, 92),
        Node("a3", "external", "أمين المستودع", 1570, 155, 300, 92),
        Node("a4", "external", "مسؤول الأصول", 690, 155, 300, 92),
        Node("a5", "external", "المستلم والامتثال", 210, 155, 400, 92),
    ]
    external_flows = [
        Flow("a1", "p1", "طلب وأمر توريد", (0.5, 1.0), (0.65, 0.0)),
        Flow("p1", "a2", "أمر وكميات", (0.35, 0.0), (0.35, 1.0)),
        Flow("a2", "p2", "شحنة وفاتورة", (0.5, 1.0), (0.65, 0.0)),
        Flow("a3", "p2", "محضر وفحص كمية", (0.0, 0.50), (1.0, 0.65)),
        Flow("a4", "p5", "اعتماد عهدة", (0.5, 1.0), (0.65, 0.0)),
        Flow("a5", "p6", "إثبات تسليم أو بلاغ فقد", (0.5, 1.0), (0.65, 0.0)),
        Flow("p6", "a5", "حالة عهدة وإشعار", (0.35, 0.0), (0.35, 1.0)),
    ]
    extra_flows = [
        Flow("d3", "p5", "أصل متاح للعهدة", (0.35, 1.0), (0.65, 1.0), ((1690, 980), (820, 980))),
        Flow("d4", "p6", "رمز الأصل وسلسلة المصدر", (0.35, 1.0), (0.65, 1.0), ((1250, 1040), (380, 1040))),
    ]
    pipeline_page(
        mxfile,
        10,
        "10 - اللوجستيات والمخزون والأصول والعهد",
        "تفصيل العملية 8.0 - اللوجستيات والمخزون والأصول والعهد",
        "من أسطر أمر التوريد إلى الاستلام والمخزون أو الأصل والترميز والعهدة والفقد وسلسلة المصدر.",
        (
            "8.1 إدارة رحلة الأصل والعهدة والفقد",
            "8.2 تسجيل الاستلام والتحقق من الكمية والفاتورة",
            "8.3 زيادة المخزون أو إنشاء أصل وربط المصدر",
            "8.4 توليد رمز الأصل وتثبيت Lineage",
            "8.5 طلب العهدة واعتماد التسليم",
            "8.6 الإرجاع أو النقل أو تسجيل الفقد والتحقيق",
        ),
        (
            "D8.1 SupplyOrder وSupplyOrderLine",
            "D8.2 Receipt والفواتير والبنود المقبولة",
            "D8.3 Inventory وAsset وLineage",
            "D8.4 AssetCode وCodeIssuance",
            "D8.5 AssetAssignment والموافقات والأدلة",
            "D8.6 AssetReturn وAssetLossReport والتحقيق",
        ),
        ("أمر مفتوح", "بنود مستلمة", "مخزون أو أصل", "أصل مرمز", "عهدة معتمدة"),
        actors,
        external_flows,
        (
            "حفظ الأمر والخطوط",
            "حفظ الاستلام والفاتورة",
            "حفظ الرصيد أو الأصل والمصدر",
            "حفظ الرمز والإصدار",
            "حفظ الطلب والموافقة والدليل",
            "حفظ الإرجاع أو الفقد والتحقيق",
        ),
        store_reads=((1, "أمر مفتوح"), (3, "رصيد وأصل"), (5, "عهدة معتمدة")),
        extra_flows=extra_flows,
    )


def identity_files_rights_page(mxfile: ET.Element) -> None:
    actors = [
        Node("a1", "external", "الموظف أو خدمة المجال", 2400, 155, 400, 92),
        Node("a2", "external", "محرك فحص الملفات وCDR", 1960, 155, 400, 92),
        Node("a3", "external", "جهة مخولة بطلب الوثيقة", 1070, 155, 400, 92),
        Node("a4", "external", "صاحب البيانات ومسؤول الخصوصية", 590, 155, 450, 92),
        Node("a5", "external", "المدقق", 210, 155, 320, 92),
    ]
    external_flows = [
        Flow("a1", "p1", "طلب بطاقة أو ملف أو حق بيانات", (0.5, 1.0), (0.65, 0.0)),
        Flow("p2", "a2", "محتوى محجور للفحص", (0.35, 0.0), (0.35, 1.0)),
        Flow("a2", "p3", "نتيجة فحص وتنقية", (0.5, 1.0), (0.65, 0.0)),
        Flow("a3", "p4", "طلب وصول أو تحقق مخول", (0.5, 1.0), (0.65, 0.0)),
        Flow("p4", "a3", "رابط مؤقت أو نتيجة تحقق", (0.35, 0.0), (0.35, 1.0)),
        Flow("a4", "p5", "طلب وصول أو تصحيح أو حذف", (0.5, 1.0), (0.65, 0.0)),
        Flow("p6", "a4", "حزمة نتيجة أو قرار مسبب", (0.35, 0.0), (0.35, 1.0)),
        Flow("p6", "a5", "أثر تدقيق كامل", (0.0, 0.50), (0.5, 1.0)),
    ]
    pipeline_page(
        mxfile,
        11,
        "11 - الهوية والملفات والأرشيف وحقوق البيانات",
        "تفصيل العملية 9.0 - الهوية والملفات والأرشيف وحقوق البيانات",
        "تشفير الهوية، حجر وفحص الملفات، منح مؤقتة، أرشفة واحتفاظ، وتنفيذ حقوق صاحب البيانات.",
        (
            "9.1 إدارة رحلة الملف والوصول وحقوق البيانات",
            "9.2 تسجيل الهوية المشفرة والبطاقة وMetadata والحجر",
            "9.3 الفحص وCDR والتحقق من البصمة والترقية",
            "9.4 إصدار البطاقة أو منحة الوصول أو نتيجة التحقق",
            "9.5 الأرشفة وتقييم الاحتفاظ والاستثناء",
            "9.6 تنفيذ DSAR وتجميع النتيجة والتدقيق",
        ),
        (
            "D9.1 الطلب والهوية المشفرة والسياق",
            "D9.2 IDCard وFileMetadata ومحتوى الحجر",
            "D9.3 الملف النشط وSHA-256 وحالة الفحص",
            "D9.4 AccessGrant وDocumentVerification",
            "D9.5 ArchiveItem وRetentionPolicy",
            "D9.6 DSAR ونتيجة التنفيذ وسجل التدقيق",
        ),
        ("طلب وهوية محققان", "FileId محجور", "ملف نظيف نشط", "بطاقة أو منحة", "طلب حقوق مقرر"),
        actors,
        external_flows,
        (
            "حفظ الطلب والهوية",
            "حفظ البطاقة وMetadata والحجر",
            "حفظ الملف والبصمة",
            "حفظ المنحة أو التحقق",
            "حفظ الأرشيف والاحتفاظ",
            "حفظ النتيجة والأثر",
        ),
        store_reads=((1, "هوية وسياق"), (2, "محتوى محجور"), (3, "ملف نشط"), (4, "منح فعالة"), (5, "قرار احتفاظ")),
    )


def complaints_compliance_page(mxfile: ET.Element) -> None:
    actors = [
        Node("a1", "external", "الموظف أو مقدم الشكوى", 2400, 155, 400, 92),
        Node("a2", "external", "مسؤول الشكاوى والامتثال", 1960, 155, 400, 92),
        Node("a3", "external", "المحقق أو اللجنة", 1130, 155, 340, 92),
        Node("a4", "external", "الإدارة العليا والقانونية", 690, 155, 400, 92),
        Node("a5", "external", "المدقق والأطراف المخولة", 210, 155, 400, 92),
    ]
    external_flows = [
        Flow("a1", "p1", "شكوى أو تظلم وأدلة", (0.5, 1.0), (0.65, 0.0)),
        Flow("a2", "p2", "تصنيف واختصاص وأولوية", (0.5, 1.0), (0.65, 0.0)),
        Flow("a3", "p4", "محضر تحقيق وتوصية", (0.5, 1.0), (0.65, 0.0)),
        Flow("a4", "p5", "قرار أو اعتماد أو نتيجة تظلم", (0.5, 1.0), (0.65, 0.0)),
        Flow("p6", "a1", "رقم متابعة وقرار مسبب", (0.35, 0.0), (0.35, 1.0)),
        Flow("p6", "a5", "تقرير امتثال وأثر تدقيق", (0.35, 0.0), (0.5, 1.0)),
    ]
    pipeline_page(
        mxfile,
        12,
        "12 - الشكاوى والانضباط والامتثال",
        "تفصيل العملية 10.0 - الشكاوى والانضباط والامتثال",
        "شكوى قابلة للتتبع، أدلة مضبوطة، فصل واجبات، تحقيق وقرار وتظلم وإغلاق مدقق.",
        (
            "10.1 إدارة رحلة الشكوى والانضباط",
            "10.2 التصنيف والاختصاص والنطاق وتعارض المصالح",
            "10.3 تجميد الأدلة ومنح الوصول المسبب",
            "10.4 التحقيق وفصل الواجبات وتسجيل المحاضر",
            "10.5 إصدار القرار أو الجزاء أو نتيجة التظلم",
            "10.6 الإغلاق والإشعار وتقرير الامتثال والتدقيق",
        ),
        (
            "D10.1 الشكاوى والتظلمات والمرفقات",
            "D10.2 التصنيف والاختصاص وتعارض المصالح",
            "D10.3 الأدلة والحفظ القانوني ومنح الوصول",
            "D10.4 التحقيق والمحاضر والتوصيات",
            "D10.5 القرار والجزاء والتظلم",
            "D10.6 الإغلاق والإشعارات والتقارير والتدقيق",
        ),
        ("شكوى مسجلة", "قضية مختصة", "أدلة مضبوطة", "توصية تحقيق", "قرار نافذ"),
        actors,
        external_flows,
        (
            "حفظ الشكوى والأدلة",
            "حفظ التصنيف والاختصاص",
            "حفظ التجميد والمنح",
            "حفظ التحقيق والمحاضر",
            "حفظ القرار والتظلم",
            "حفظ الإغلاق والتقرير",
        ),
        store_reads=((1, "شكوى وأدلة"), (2, "اختصاص وتعارض"), (3, "أدلة مخولة"), (4, "توصية"), (5, "قرار")),
    )


def governance_engines_complete_page(mxfile: ET.Element) -> None:
    actors = [
        Node("a1", "external", "مسؤول السياسات", 2400, 155, 360, 92),
        Node("a2", "external", "خدمة المجال", 1960, 155, 360, 92),
        Node("a3", "external", "المعتمد أو المفوض", 1070, 155, 360, 92),
        Node("a4", "external", "مسؤول الترميز", 610, 155, 340, 92),
        Node("a5", "external", "التكامل والمدقق", 210, 155, 340, 92),
    ]
    external_flows = [
        Flow("a1", "p1", "قالب وقواعد وإصدار", (0.5, 1.0), (0.65, 0.0)),
        Flow("a2", "p2", "حقائق وطلب قرار", (0.5, 1.0), (0.65, 0.0)),
        Flow("a3", "p4", "موافقة أو رفض أو تفويض", (0.5, 1.0), (0.65, 0.0)),
        Flow("a4", "p5", "سياسة رمز ونوع كيان", (0.5, 1.0), (0.65, 0.0)),
        Flow("p6", "a2", "قرار ورمز ونتيجة سير", (0.35, 0.0), (0.35, 1.0)),
        Flow("p6", "a5", "أحداث حوكمة وأثر إنفاذ", (0.35, 0.0), (0.5, 1.0)),
    ]
    pipeline_page(
        mxfile,
        13,
        "13 - السياسات وسير العمل والتوقيعات والرموز",
        "تفصيل العملية 11.0 - السياسات وسير العمل والتوقيعات والرموز",
        "إصدارات غير قابلة للتعديل، محاكاة وإنفاذ، سير وتوقيع وتفويض، وتوليد رموز ذري قابل للتدقيق.",
        (
            "11.1 إدارة رحلة السياسة وسير العمل والرمز",
            "11.2 محاكاة الإصدار ونشره وتقييمه وتسجيل الإنفاذ",
            "11.3 تعريف سير العمل وبدء المثيل والخطوات",
            "11.4 الموافقة والتوقيع والتفويض وإصدار القرار",
            "11.5 معاينة الرمز وتوليده ذرياً حسب الإصدار",
            "11.6 نشر النتيجة وتسجيل دفتر الحوكمة والتدقيق",
        ),
        (
            "D11.1 PolicyTemplateVersion وPolicyRule",
            "D11.2 SimulationRun وEvaluation وEnforcementLedger",
            "D11.3 WorkflowDefinition وInstance وSteps",
            "D11.4 Signature وDelegation وDecision",
            "D11.5 CodePolicyVersion وCounter وIssuance",
            "D11.6 GovernanceOutbox وAuditLedger",
        ),
        ("إصدار مرشح", "قرار أولي", "مثيل وخطوة", "قرار موقع", "رمز مولد"),
        actors,
        external_flows,
        (
            "حفظ القالب والقواعد",
            "حفظ المحاكاة والتقييم والإنفاذ",
            "حفظ التعريف والمثيل",
            "حفظ التوقيع والقرار",
            "حفظ الإصدار والعداد والرمز",
            "حفظ النتيجة والأثر",
        ),
        store_reads=((1, "إصدار منشور"), (2, "قرار أولي"), (3, "الخطوة الحالية"), (4, "قرار نافذ"), (5, "سياسة وعدّاد")),
    )


def integration_reporting_domain_page(mxfile: ET.Element) -> None:
    actors = [
        Node("a1", "external", "خدمة مجال منتجة", 2400, 155, 380, 92),
        Node("a2", "external", "RabbitMQ / MassTransit", 1960, 155, 380, 92),
        Node("a3", "external", "خدمة مجال مستهلكة", 1130, 155, 380, 92),
        Node("a4", "external", "التشغيل والمراقبة", 690, 155, 340, 92),
        Node("a5", "external", "الإدارة والتقارير", 210, 155, 340, 92),
    ]
    external_flows = [
        Flow("a1", "p1", "معاملة محلية وحدث مجال", (0.5, 1.0), (0.65, 0.0)),
        Flow("p2", "a2", "عقد تكامل منشور", (0.35, 0.0), (0.35, 1.0)),
        Flow("a2", "p3", "حدث مسلم", (0.5, 1.0), (0.65, 0.0)),
        Flow("p4", "a3", "أمر أو نتيجة تطبيق", (0.35, 0.0), (0.35, 1.0)),
        Flow("a4", "p6", "طلب مصالحة أو إعادة تشغيل", (0.5, 1.0), (0.65, 0.0)),
        Flow("p6", "a4", "تنبيه ونتيجة مصالحة", (0.35, 0.0), (0.35, 1.0)),
        Flow("p5", "a5", "مؤشرات وتقارير ونموذج قراءة", (0.35, 0.0), (0.5, 1.0)),
    ]
    pipeline_page(
        mxfile,
        14,
        "14 - التكامل والأحداث والتقارير والتحليلات",
        "تفصيل العملية 12.0 - التكامل والأحداث والتقارير والتحليلات",
        "ملكية بيانات مستقلة، Outbox وInbox وSaga، عقود مترابطة، نماذج قراءة، ومصالحة قابلة لإعادة التشغيل.",
        (
            "12.1 إدارة رحلة الحدث والتكامل الموثوق",
            "12.2 تثبيت Outbox والتقاط الرسالة ونشر العقد",
            "12.3 التحقق من العقد وInbox ومنع التكرار",
            "12.4 تطبيق المستهلك وتنسيق Saga والتعويض",
            "12.5 تحديث نماذج القراءة والتقارير والتحليلات",
            "12.6 كشف التعطل والمصالحة وإعادة التشغيل والتدقيق",
        ),
        (
            "D12.1 بيانات المجال وOutboxMessage",
            "D12.2 OutboxState وContractVersion",
            "D12.3 InboxReceipt وConsumerState",
            "D12.4 SagaState وProcessIntervention",
            "D12.5 ReadModel وReportSnapshot",
            "D12.6 Reconciliation وDeadLetter وAudit",
        ),
        ("رسالة مثبتة", "عقد منشور", "حدث غير مكرر", "نتيجة أو تعويض", "نموذج قراءة محدث"),
        actors,
        external_flows,
        (
            "حفظ المعاملة والرسالة",
            "حفظ حالة النشر والعقد",
            "حفظ إيصال المستهلك",
            "حفظ Saga والتدخل",
            "حفظ النموذج واللقطة",
            "حفظ المصالحة والتدقيق",
        ),
        store_reads=((1, "رسالة معلقة"), (2, "عقد منشور"), (3, "MessageId"), (4, "حالة Saga"), (5, "لقطة حالية")),
    )


def hire_journey_page(mxfile: ET.Element) -> None:
    actors = [
        Node("a1", "external", "المتقدم وجهة الاستقطاب", 2400, 155, 400, 92),
        Node("a2", "external", "موظف الموارد البشرية والمعتمد", 1960, 155, 400, 92),
        Node("a3", "external", "خدمات الرواتب والهوية والدليل", 610, 155, 470, 92),
        Node("a4", "external", "الموظف والإدارة الطالبة", 210, 155, 400, 92),
    ]
    external_flows = [
        Flow("a1", "p1", "بيانات مرشح ووثائق", (0.5, 1.0), (0.65, 0.0)),
        Flow("a2", "p2", "اعتماد التعيين", (0.5, 1.0), (0.65, 0.0)),
        Flow("p5", "a3", "أوامر إنشاء الأطراف", (0.35, 0.0), (0.35, 1.0)),
        Flow("a3", "p5", "نتائج الأطراف", (0.65, 1.0), (0.65, 0.0)),
        Flow("p6", "a4", "بطاقة وإشعار وقرار مباشرة", (0.35, 0.0), (0.5, 1.0)),
    ]
    extra_flows = [
        Flow(
            "d5",
            "p6",
            "اكتمال الأطراف أو تدخل مطلوب",
            (0.35, 1.0),
            (0.65, 1.0),
            ((820, 1010), (380, 1010)),
        ),
        Flow(
            "p5",
            "p4",
            "تعويض عند فشل طرف",
            (1.0, 0.82),
            (0.0, 0.82),
            ((1080, 570), (1420, 570)),
            dashed=True,
            color=RED,
        ),
    ]
    pipeline_page(
        mxfile,
        15,
        "15 - رحلة التعيين والتعاقد",
        "مخطط تفصيلي - رحلة تعيين موظف جديد",
        "يحافظ على تدفق DFD المعتمد للتعيين ويضيف الجهة والسياسة والرمز وSaga والتعويض.",
        (
            "3.1.1 استقبال المرشح والتحقق من اكتمال الطلب",
            "3.1.2 تحديد الجهة والنطاق وتقييم سياسة التعيين",
            "3.1.3 إنشاء الموظف والملف والرقم الوظيفي",
            "3.1.4 إنشاء العقد وتسجيل المباشرة",
            "3.1.5 تنسيق أطراف التعيين عبر Hire Saga",
            "3.1.6 استكمال السيرة والأرشفة والإشعار",
        ),
        (
            "D3.1.1 طلبات التوظيف والمرشحون",
            "D3.1.2 قرار الجهة والسياسة والموافقة",
            "D3.1.3 Employee والسيرة والرقم",
            "D3.1.4 Contract وCommencement",
            "D3.1.5 HireCaseState ونتائج الأطراف",
            "D3.1.6 السيرة والوثائق والأرشيف",
        ),
        (
            "طلب صالح",
            "قرار تعيين معتمد",
            "EmployeeId ورقم وظيفي",
            "عقد ومباشرة",
            "نتيجة Saga",
        ),
        actors,
        external_flows,
        (
            "حفظ الطلب والوثائق",
            "حفظ القرار والأثر",
            "حفظ الموظف والرقم",
            "حفظ العقد والمباشرة",
            "حفظ حالة Saga",
            "حفظ السيرة والدليل",
        ),
        store_reads=((1, "بيانات المرشح"), (2, "قرار نافذ"), (5, "حالة الأطراف")),
        extra_flows=extra_flows,
    )


def transfer_journey_page(mxfile: ET.Element) -> None:
    actors = [
        Node("a1", "external", "الموظف أو الفرع المصدر", 2400, 155, 400, 92),
        Node("a2", "external", "الفرع الهدف", 1570, 155, 320, 92),
        Node("a3", "external", "الإدارة المركزية والمعتمد", 1070, 155, 430, 92),
        Node("a4", "external", "العقود والرواتب والعهد", 610, 155, 430, 92),
        Node("a5", "external", "الفرعان والموظف والمدقق", 190, 155, 430, 92),
    ]
    external_flows = [
        Flow("a1", "p1", "طلب نقل أو ندب أو إعارة", (0.5, 1.0), (0.65, 0.0)),
        Flow("a2", "p3", "قبول الوحدة الهدف", (0.5, 1.0), (0.65, 0.0)),
        Flow("a3", "p4", "اعتماد مركزي", (0.5, 1.0), (0.65, 0.0)),
        Flow("p5", "a4", "أثر حركة معتمد", (0.35, 0.0), (0.35, 1.0)),
        Flow("a4", "p5", "نتائج تحديث الآثار", (0.65, 1.0), (0.65, 0.0)),
        Flow("p6", "a5", "قرار وإشعار وأثر تدقيق", (0.35, 0.0), (0.5, 1.0)),
    ]
    extra_flows = [
        Flow(
            "d2",
            "p4",
            "لقطة النطاق والأهلية",
            (0.35, 1.0),
            (0.65, 1.0),
            ((2130, 990), (1260, 990)),
        ),
        Flow(
            "p6",
            "d1",
            "إغلاق الطلب ونتيجة المصالحة",
            (0.70, 1.0),
            (0.72, 1.0),
            ((430, 1080), (2580, 1080)),
        ),
    ]
    pipeline_page(
        mxfile,
        16,
        "16 - رحلة النقل والندب والإعارة",
        "مخطط تفصيلي - رحلة الحركة وإعادة الإسناد",
        "تحقق نطاق المصدر والهدف وقبول الهدف والموافقة والتنفيذ ثم تحديث الآثار والمصالحة.",
        (
            "4.1.1 تسجيل طلب الحركة وتثبيت المصدر",
            "4.1.2 التحقق من النطاق والأهلية والوظيفة",
            "4.1.3 قبول الوحدة الهدف وبدء المسار",
            "4.1.4 اعتماد القرار وتنفيذ الحركة",
            "4.1.5 تحديث العقد والراتب والعهد ومركز الكلفة",
            "4.1.6 نشر الحدث والمصالحة والإشعار",
        ),
        (
            "D4.1.1 طلب الحركة وحالته",
            "D4.1.2 لقطة المصدر والهدف والأهلية",
            "D4.1.3 قبول الهدف ومثيل المسار",
            "D4.1.4 MovementSnapshot والقرار",
            "D4.1.5 نتائج الآثار التابعة",
            "D4.1.6 Outbox والمصالحة والتدقيق",
        ),
        (
            "طلب مسجل",
            "طلب مؤهل",
            "هدف موافق",
            "حركة منفذة",
            "آثار محدثة",
        ),
        actors,
        external_flows,
        (
            "حفظ الطلب والمصدر",
            "حفظ لقطة التحقق",
            "حفظ القبول والمسار",
            "حفظ الحركة والقرار",
            "حفظ نتائج الآثار",
            "حفظ الحدث والمصالحة",
        ),
        store_reads=((1, "تفاصيل الطلب"), (2, "النطاق والوظيفة"), (4, "حركة نافذة"), (5, "نتائج الأطراف")),
        extra_flows=extra_flows,
    )


def leave_advance_journey_page(mxfile: ET.Element) -> None:
    actors = [
        Node("a1", "external", "الموظف عبر الخدمة الذاتية", 2400, 155, 400, 92),
        Node("a2", "external", "مدير الفرع", 1570, 155, 320, 92),
        Node("a3", "external", "المعتمد المالي", 1070, 155, 360, 92),
        Node("a4", "external", "الرواتب والسجل الوظيفي", 610, 155, 430, 92),
        Node("a5", "external", "الموظف والمدقق", 210, 155, 380, 92),
    ]
    external_flows = [
        Flow("a1", "p1", "طلب إجازة أو سلفة", (0.5, 1.0), (0.65, 0.0)),
        Flow("a2", "p3", "موافقة إدارية", (0.5, 1.0), (0.65, 0.0)),
        Flow("a3", "p4", "موافقة مالية عند اللزوم", (0.5, 1.0), (0.65, 0.0)),
        Flow("p5", "a4", "أثر خدمة أو استقطاع", (0.35, 0.0), (0.35, 1.0)),
        Flow("a4", "p5", "نتيجة التطبيق", (0.65, 1.0), (0.65, 0.0)),
        Flow("p6", "a5", "قرار وإشعار وأثر تدقيق", (0.35, 0.0), (0.5, 1.0)),
    ]
    pipeline_page(
        mxfile,
        17,
        "17 - رحلة الإجازة والسلفة",
        "مخطط تفصيلي - الإجازات والسلف والخدمة الذاتية",
        "التحقق من الرصيد والقواعد والنطاق، مسار الموافقة، ثم تطبيق الأثر والإشعار.",
        (
            "5.1.1 تسجيل الطلب والوثائق",
            "5.1.2 التحقق من الرصيد والأهلية والسياسة",
            "5.1.3 بدء سير الموافقة الإدارية",
            "5.1.4 إصدار القرار والموافقة المالية",
            "5.1.5 تحديث الرصيد والراتب والسيرة",
            "5.1.6 الإشعار والأرشفة والتدقيق",
        ),
        (
            "D5.1.1 الطلبات والمرفقات",
            "D5.1.2 الأرصدة ونتيجة السياسة",
            "D5.1.3 WorkflowInstance والموافقات",
            "D5.1.4 القرار وشروط التنفيذ",
            "D5.1.5 أثر الرصيد والراتب والسيرة",
            "D5.1.6 الإشعارات والأرشيف والتدقيق",
        ),
        (
            "طلب مكتمل",
            "طلب مؤهل",
            "مسار موافقة",
            "قرار نافذ",
            "أثر مطبق",
        ),
        actors,
        external_flows,
        (
            "حفظ الطلب والمرفقات",
            "حفظ نتيجة التحقق",
            "حفظ الخطوات والموافقات",
            "حفظ القرار",
            "حفظ الأثر",
            "حفظ الإشعار والدليل",
        ),
        store_reads=((1, "تفاصيل الطلب"), (2, "الرصيد والقواعد"), (3, "الخطوة الحالية"), (4, "قرار التنفيذ")),
    )


def medical_journey_page(mxfile: ET.Element) -> None:
    actors = [
        Node("a1", "external", "الموظف أو جهة الإحالة", 2400, 155, 400, 92),
        Node("a2", "external", "الكادر الصحي المخول", 1960, 155, 400, 92),
        Node("a3", "external", "اللجنة الطبية", 1130, 155, 320, 92),
        Node("a4", "external", "الموارد البشرية والرواتب", 610, 155, 430, 92),
        Node("a5", "external", "الأرشيف والمدقق والموظف", 170, 155, 470, 92),
    ]
    external_flows = [
        Flow("a1", "p1", "إحالة أو طلب فحص", (0.5, 1.0), (0.65, 0.0)),
        Flow("a2", "p2", "بيانات زيارة وفحص", (0.5, 1.0), (0.65, 0.0)),
        Flow("a3", "p4", "قرار لجنة", (0.5, 1.0), (0.65, 0.0)),
        Flow("p5", "a4", "أثر لياقة أو تعويض مسموح", (0.35, 0.0), (0.35, 1.0)),
        Flow("a4", "p5", "نتيجة تطبيق الأثر", (0.65, 1.0), (0.65, 0.0)),
        Flow("p6", "a5", "إشعار وأثر وصول", (0.35, 0.0), (0.5, 1.0)),
    ]
    extra_flows = [
        Flow(
            "d3",
            "p4",
            "نتائج مخولة فقط",
            (0.0, 0.50),
            (1.0, 0.72),
        ),
        Flow(
            "p4",
            "p5",
            "توصية مهنية مختصرة",
            (0.0, 0.46),
            (1.0, 0.46),
        ),
    ]
    # Replace the default sequence label between p4 and p5 with the explicit,
    # privacy-aware flow above by keeping the sequence wording intentionally short.
    pipeline_page(
        mxfile,
        18,
        "18 - رحلة الملف الطبي",
        "مخطط تفصيلي - الصحة المهنية والقرار الطبي",
        "يفصل التفاصيل السريرية عن الأثر الوظيفي المختصر ويقيد الوصول والمنح والتدقيق.",
        (
            "6.2.1 فتح الإحالة والتحقق من الهوية والموافقة",
            "6.2.2 فتح الزيارة ومنح الوصول للكادر المخول",
            "6.2.3 تسجيل الفحوص والنتائج السريرية",
            "6.2.4 إصدار التوصية أو قرار اللجنة",
            "6.2.5 تطبيق الأثر الوظيفي والمالي المسموح",
            "6.2.6 أرشفة الأدلة وتسجيل الوصول والإشعار",
        ),
        (
            "D6.2.1 الإحالات والموافقات",
            "D6.2.2 الزيارات والمنح",
            "D6.2.3 الفحوص والنتائج السريرية",
            "D6.2.4 التوصيات وقرارات اللجان",
            "D6.2.5 الأثر الوظيفي والمالي",
            "D6.2.6 الملفات وأثر الوصول والتدقيق",
        ),
        (
            "إحالة مخولة",
            "زيارة فعالة",
            "نتيجة صحية",
            "قرار قابل للتطبيق",
            "نتيجة تطبيق",
        ),
        actors,
        external_flows,
        (
            "حفظ الإحالة والموافقة",
            "حفظ الزيارة والمنحة",
            "حفظ الفحص والنتيجة",
            "حفظ التوصية والقرار",
            "حفظ الأثر المسموح",
            "حفظ الدليل وأثر الوصول",
        ),
        store_reads=((1, "إحالة وموافقة"), (2, "منحة فعالة"), (4, "قرار طبي")),
        extra_flows=[
            Flow(
                "d3",
                "p4",
                "نتائج مخولة فقط",
                (0.0, 0.50),
                (1.0, 0.72),
            )
        ],
    )


def training_journey_page(mxfile: ET.Element) -> None:
    actors = [
        Node("a1", "external", "الموظف أو الإدارة الطالبة", 2400, 155, 400, 92),
        Node("a2", "external", "مسؤول التدريب", 1960, 155, 350, 92),
        Node("a3", "external", "الإدارة العليا والوزارات", 1070, 155, 430, 92),
        Node("a4", "external", "جهة التدريب أو الجهة الأكاديمية", 590, 155, 470, 92),
        Node("a5", "external", "الموظف والسجل الوظيفي", 190, 155, 430, 92),
    ]
    external_flows = [
        Flow("a1", "p1", "احتياج أو طلب ترشيح", (0.5, 1.0), (0.65, 0.0)),
        Flow("a2", "p2", "خطة ودورة أو بعثة", (0.5, 1.0), (0.65, 0.0)),
        Flow("a3", "p4", "اعتماد وترخيص بعثة", (0.5, 1.0), (0.65, 0.0)),
        Flow("p4", "a4", "قائمة مرشحين وتفاصيل تنفيذ", (0.35, 0.0), (0.35, 1.0)),
        Flow("a4", "p5", "حضور ونتيجة وشهادة", (0.65, 1.0), (0.65, 0.0)),
        Flow("p6", "a5", "نتيجة وإشعار وتحديث كفاءة", (0.35, 0.0), (0.5, 1.0)),
    ]
    extra_flows = [
        Flow(
            "p4",
            "d6",
            "أثر بعثة مالي مبدئي",
            (0.70, 1.0),
            (0.72, 1.0),
            ((1260, 1000), (390, 1000)),
            dashed=True,
        ),
        Flow(
            "d5",
            "p6",
            "نتيجة وشهادة",
            (0.0, 0.50),
            (1.0, 0.72),
        ),
    ]
    pipeline_page(
        mxfile,
        19,
        "19 - رحلة التدريب والبعثة",
        "مخطط تفصيلي - الدورة التدريبية أو البعثة",
        "من تحليل الاحتياج إلى الخطة والترشيح والاعتماد والتنفيذ ثم تحديث الكفاءة والسيرة.",
        (
            "6.1.1 تحليل فجوة المهارة والاحتياج",
            "6.1.2 إعداد الخطة والدورة أو البعثة",
            "6.1.3 الترشيح والتحقق من الأهلية",
            "6.1.4 الاعتماد وتخصيص المقعد أو الترخيص",
            "6.1.5 متابعة الحضور والنتيجة والشهادة",
            "6.1.6 تحديث الكفاءة والسيرة والأثر المالي",
        ),
        (
            "D6.1.1 الاحتياجات وفجوات المهارة",
            "D6.1.2 الخطط والدورات والبعثات",
            "D6.1.3 الترشيحات والتسجيل",
            "D6.1.4 الموافقات والمقاعد والتراخيص",
            "D6.1.5 الحضور والنتائج والشهادات",
            "D6.1.6 الكفاءات والسيرة والأثر المالي",
        ),
        (
            "احتياج معتمد",
            "فرصة تدريب",
            "مرشح مؤهل",
            "مقعد أو بعثة معتمدة",
            "نتيجة نهائية",
        ),
        actors,
        external_flows,
        (
            "حفظ الاحتياج",
            "حفظ الخطة والفرصة",
            "حفظ الترشيح",
            "حفظ الاعتماد",
            "حفظ الحضور والنتيجة",
            "حفظ الكفاءة والأثر",
        ),
        store_reads=((1, "فجوة مهارة"), (2, "الخطة المتاحة"), (3, "بيانات المرشح"), (4, "اعتماد نافذ")),
        extra_flows=extra_flows,
    )


def payroll_journey_page(mxfile: ET.Element) -> None:
    actors = [
        Node("a1", "external", "العقود والحركات والخدمة الذاتية", 2380, 155, 440, 92),
        Node("a2", "external", "مشغل الرواتب والمعتمد", 1530, 155, 400, 92),
        Node("a3", "external", "الخزينة", 1070, 155, 320, 92),
        Node("a4", "external", "المصرف", 650, 155, 300, 92),
        Node("a5", "external", "الامتثال والمدقق", 210, 155, 360, 92),
    ]
    external_flows = [
        Flow("a1", "p1", "آثار خدمة ولقطات مرجعية", (0.5, 1.0), (0.65, 0.0)),
        Flow("a2", "p3", "اعتماد الترحيل", (0.5, 1.0), (0.65, 0.0)),
        Flow("a3", "p4", "قرار التمويل", (0.5, 1.0), (0.65, 0.0)),
        Flow("p5", "a4", "أوامر دفع", (0.35, 0.0), (0.35, 1.0)),
        Flow("a4", "p6", "إقرار دفع وكشف مصرفي", (0.65, 1.0), (0.65, 0.0)),
        Flow("p6", "a5", "فروق وتنبيهات وحجوزات", (0.35, 0.0), (0.5, 1.0)),
    ]
    extra_flows = [
        Flow(
            "p6",
            "p3",
            "عكس تعويضي معتمد",
            (1.0, 0.82),
            (0.0, 0.82),
            ((400, 1110), (1700, 1110), (1700, 560)),
            dashed=True,
            color=RED,
        ),
        Flow(
            "d6",
            "p4",
            "متأخرات وحجوزات مفتوحة",
            (0.75, 0.0),
            (0.25, 1.0),
            ((470, 1000), (1250, 1000)),
        ),
    ]
    pipeline_page(
        mxfile,
        20,
        "20 - رحلة تشغيل الراتب والصرف",
        "مخطط تفصيلي - تشغيل الراتب حتى التسوية",
        "تدفق قابل للتكرار من جمع الآثار إلى الحساب والترحيل والتمويل والدفع والتسوية.",
        (
            "7.1.1 جمع الآثار وتثبيت لقطة المدخلات",
            "7.1.2 الحساب المتوازي ونقاط الاستئناف",
            "7.1.3 التحقق والاعتماد والترحيل",
            "7.1.4 التمويل والأولوية والصرف الجزئي",
            "7.1.5 إنشاء أوامر الدفع وإرسالها",
            "7.1.6 التسوية والمتأخرات والعكس والحجز",
        ),
        (
            "D7.1.1 InputSnapshot وبصمة الآثار",
            "D7.1.2 Jobs وPartitions وSalaryLines",
            "D7.1.3 PayrollRun وPaymentBatch",
            "D7.1.4 FundingDecision وArrearsLedger",
            "D7.1.5 PaymentOrder وحالة الإرسال",
            "D7.1.6 Statement وFraudAlert وPaymentHold",
        ),
        (
            "لقطة ثابتة",
            "نتائج حساب",
            "دفعة مرحلة",
            "خطة صرف",
            "أوامر مرسلة",
        ),
        actors,
        external_flows,
        (
            "حفظ اللقطة والبصمة",
            "حفظ المهام والأسطر",
            "حفظ الاعتماد والترحيل",
            "حفظ التمويل والمتأخرات",
            "حفظ الأوامر",
            "حفظ التسوية والحجز",
        ),
        store_reads=((1, "الآثار المجمدة"), (2, "نقطة الاستئناف"), (3, "دفعة مرحلة"), (4, "أولويات ومتأخرات"), (5, "أوامر مفتوحة")),
        extra_flows=extra_flows,
    )


def complaint_journey_page(mxfile: ET.Element) -> None:
    actors = [
        Node("a1", "external", "الموظف أو المواطن أو الإدارة", 2380, 155, 440, 92),
        Node("a2", "external", "موظف الامتثال", 1960, 155, 350, 92),
        Node("a3", "external", "لجنة التحقيق والقانونية", 1070, 155, 430, 92),
        Node("a4", "external", "الإدارة العليا", 690, 155, 300, 92),
        Node("a5", "external", "مقدم الشكوى والمدقق", 190, 155, 430, 92),
    ]
    external_flows = [
        Flow("a1", "p1", "شكوى وأدلة", (0.5, 1.0), (0.65, 0.0)),
        Flow("a2", "p2", "تصنيف وإسناد", (0.5, 1.0), (0.65, 0.0)),
        Flow("a3", "p3", "نتيجة تحقيق ورأي قانوني", (0.5, 1.0), (0.65, 0.0)),
        Flow("a4", "p4", "اعتماد قرار", (0.5, 1.0), (0.65, 0.0)),
        Flow("p6", "a5", "نتيجة وإشعار وأثر تدقيق", (0.35, 0.0), (0.5, 1.0)),
    ]
    extra_flows = [
        Flow(
            "p5",
            "p2",
            "إعادة فتح عند نقص الإجراء",
            (1.0, 0.82),
            (0.0, 0.82),
            ((830, 1060), (2180, 1060), (2180, 570)),
            dashed=True,
            color=RED,
        ),
        Flow(
            "d2",
            "p3",
            "أدلة نظيفة ومحفوظة",
            (0.0, 0.50),
            (1.0, 0.72),
        ),
    ]
    pipeline_page(
        mxfile,
        21,
        "21 - رحلة الشكوى والانضباط",
        "مخطط تفصيلي - الشكوى أو المخالفة والانضباط",
        "فصل الاستقبال وحماية الأدلة والتحقيق والقرار عن التنفيذ والإشعار والأرشفة.",
        (
            "10.1.1 استقبال الشكوى والتحقق من القبول",
            "10.1.2 حماية الأدلة والتصنيف والإسناد",
            "10.1.3 التحقيق وجمع الوقائع والرأي القانوني",
            "10.1.4 مسار القرار وفصل الواجبات والتوقيع",
            "10.1.5 تنفيذ الإجراء التصحيحي أو الانضباطي",
            "10.1.6 الإشعار والإغلاق والأرشفة والتدقيق",
        ),
        (
            "D10.1.1 الشكاوى والمخالفات",
            "D10.1.2 الأدلة والملفات المحمية",
            "D10.1.3 التحقيقات والوقائع",
            "D10.1.4 المسار والتوقيعات والقرار",
            "D10.1.5 الإجراءات التصحيحية والانضباطية",
            "D10.1.6 الإشعارات والأرشيف والتدقيق",
        ),
        (
            "شكوى مقبولة",
            "قضية مصنفة",
            "نتيجة تحقيق",
            "قرار مستوفٍ",
            "إجراء منفذ",
        ),
        actors,
        external_flows,
        (
            "حفظ الشكوى",
            "حفظ الأدلة والتصنيف",
            "حفظ التحقيق",
            "حفظ القرار والتوقيع",
            "حفظ الإجراء",
            "حفظ الإغلاق والدليل",
        ),
        store_reads=((1, "تفاصيل الشكوى"), (2, "أدلة مخولة"), (3, "وقائع التحقيق"), (4, "قرار نافذ")),
        extra_flows=extra_flows,
    )


def separation_journey_page(mxfile: ET.Element) -> None:
    actors = [
        Node("a1", "external", "الموظف أو إدارة الفرع", 2380, 155, 440, 92),
        Node("a2", "external", "القانونية أو اللجنة الطبية", 1960, 155, 400, 92),
        Node("a3", "external", "الإدارة المركزية والمعتمد", 1070, 155, 430, 92),
        Node("a4", "external", "الرواتب والعهد والهوية", 610, 155, 430, 92),
        Node("a5", "external", "الموظف والمدقق", 210, 155, 380, 92),
    ]
    external_flows = [
        Flow("a1", "p1", "طلب أو سبب إنهاء", (0.5, 1.0), (0.65, 0.0)),
        Flow("a2", "p2", "رأي قانوني أو قرار طبي", (0.5, 1.0), (0.65, 0.0)),
        Flow("a3", "p4", "اعتماد نهائي", (0.5, 1.0), (0.65, 0.0)),
        Flow("p5", "a4", "أوامر إغلاق", (0.35, 0.0), (0.35, 1.0)),
        Flow("a4", "p5", "نتائج الإغلاق", (0.65, 1.0), (0.65, 0.0)),
        Flow("p6", "a5", "قرار وتسوية وإشعار", (0.35, 0.0), (0.5, 1.0)),
    ]
    extra_flows = [
        Flow(
            "p5",
            "p3",
            "استثناء أو تدخل مطلوب",
            (1.0, 0.82),
            (0.0, 0.82),
            ((830, 1080), (1710, 1080), (1710, 570)),
            dashed=True,
            color=RED,
        ),
        Flow(
            "d5",
            "p6",
            "اكتمال الأطراف",
            (0.0, 0.50),
            (1.0, 0.72),
        ),
    ]
    pipeline_page(
        mxfile,
        22,
        "22 - رحلة إنهاء الخدمة",
        "مخطط تفصيلي - إنهاء الخدمة وإغلاق الآثار",
        "لا يثبت الفصل قبل تحقق السبب والاعتماد وإغلاق الراتب والعهد والهوية أو تسجيل تدخل.",
        (
            "4.2.1 تسجيل طلب أو سبب إنهاء الخدمة",
            "4.2.2 التحقق القانوني والطبي وسياسة الفصل",
            "4.2.3 بدء قائمة الإغلاق ومسار الموافقات",
            "4.2.4 اعتماد القرار وبدء Separation Saga",
            "4.2.5 إغلاق الراتب والعهد والهوية والملفات",
            "4.2.6 تثبيت الفصل والتسوية والأرشفة والإشعار",
        ),
        (
            "D4.2.1 طلبات الإنهاء والأسباب",
            "D4.2.2 التحقق والسياسات والآراء",
            "D4.2.3 Checklist والمسار والموافقات",
            "D4.2.4 SeparationCaseState والقرار",
            "D4.2.5 نتائج إغلاق الأطراف",
            "D4.2.6 سجل الفصل والتسوية والأرشيف",
        ),
        (
            "طلب مسجل",
            "سبب صالح",
            "قائمة إغلاق",
            "أوامر إغلاق",
            "نتائج مكتملة",
        ),
        actors,
        external_flows,
        (
            "حفظ الطلب والسبب",
            "حفظ التحقق والآراء",
            "حفظ المهام والموافقات",
            "حفظ القرار وحالة Saga",
            "حفظ نتائج الأطراف",
            "حفظ الفصل والتسوية",
        ),
        store_reads=((1, "تفاصيل الطلب"), (2, "التحقق والآراء"), (3, "مهام الإغلاق"), (4, "حالة Saga"), (5, "نتائج الأطراف")),
        extra_flows=extra_flows,
    )


def asset_journey_page(mxfile: ET.Element) -> None:
    actors = [
        Node("a1", "external", "موظف التوريد والمورد", 2380, 155, 440, 92),
        Node("a2", "external", "أمين المستودع", 1960, 155, 350, 92),
        Node("a3", "external", "مسؤول الأصول والمعتمد", 1070, 155, 430, 92),
        Node("a4", "external", "الموظف المستلم", 650, 155, 320, 92),
        Node("a5", "external", "الامتثال والمدقق", 210, 155, 360, 92),
    ]
    external_flows = [
        Flow("a1", "p1", "أمر وشحنة وفاتورة", (0.5, 1.0), (0.65, 0.0)),
        Flow("a2", "p2", "محضر وفحص كمية", (0.5, 1.0), (0.65, 0.0)),
        Flow("a3", "p4", "اعتماد أصل وعهدة", (0.5, 1.0), (0.65, 0.0)),
        Flow("a4", "p5", "توقيع استلام أو إرجاع", (0.5, 1.0), (0.65, 0.0)),
        Flow("a5", "p6", "بلاغ فقد أو طلب مراجعة", (0.5, 1.0), (0.65, 0.0)),
        Flow("p6", "a5", "قضية وأدلة وأثر", (0.35, 0.0), (0.35, 1.0)),
    ]
    extra_flows = [
        Flow(
            "p6",
            "p4",
            "إيقاف العهدة عند فقد",
            (1.0, 0.82),
            (0.0, 0.82),
            ((420, 1060), (1260, 1060), (1260, 570)),
            dashed=True,
            color=RED,
        ),
        Flow(
            "d3",
            "p4",
            "أصل ورمز وسلسلة مصدر",
            (0.0, 0.50),
            (1.0, 0.72),
        ),
    ]
    pipeline_page(
        mxfile,
        23,
        "23 - رحلة الأصل والعهدة والفقد",
        "مخطط تفصيلي - دورة الأصل من التوريد إلى العهدة والفقد",
        "يربط خط التوريد والاستلام بأصل قابل للتتبع ثم الموافقة والتسليم والإرجاع أو الفقد.",
        (
            "8.1.1 تسجيل خط أمر التوريد والشحنة",
            "8.1.2 استلام البند والتحقق من الكمية والحالة",
            "8.1.3 إنشاء المخزون أو الأصل وتوليد الرمز",
            "8.1.4 طلب العهدة واعتمادها مع فصل الواجبات",
            "8.1.5 التسليم وتوقيع الدليل أو الإرجاع",
            "8.1.6 معالجة الفقد وحماية الأدلة والتدقيق",
        ),
        (
            "D8.1.1 أمر التوريد والخط والفاتورة",
            "D8.1.2 Receipt والبنود المقبولة",
            "D8.1.3 Inventory وAsset وCode وLineage",
            "D8.1.4 AssetAssignment والموافقات",
            "D8.1.5 أدلة التسليم والإرجاع",
            "D8.1.6 AssetLossReport والأدلة والتدقيق",
        ),
        (
            "خط توريد مفتوح",
            "بند مستلم",
            "أصل مرمز",
            "عهدة معتمدة",
            "تسليم أو إرجاع موثق",
        ),
        actors,
        external_flows,
        (
            "حفظ الأمر والخط",
            "حفظ الاستلام",
            "حفظ الأصل والرمز",
            "حفظ الطلب والموافقة",
            "حفظ دليل التسليم",
            "حفظ الفقد والأدلة",
        ),
        store_reads=((1, "خط مفتوح"), (2, "كمية متوقعة"), (3, "أصل متاح"), (4, "عهدة معتمدة"), (5, "دليل عهدة")),
        extra_flows=extra_flows,
    )


def tenant_assignment_journey_page(mxfile: ET.Element) -> None:
    actors = [
        Node("a1", "external", "مسؤول المنصة ومشغل البيانات", 2380, 155, 440, 92),
        Node("a2", "external", "مالك الجهة", 1570, 155, 320, 92),
        Node("a3", "external", "مراجع الحجر", 1070, 155, 340, 92),
        Node("a4", "external", "المدقق", 210, 155, 320, 92),
    ]
    external_flows = [
        Flow("a1", "p1", "دفعة سجلات ومصدر", (0.5, 1.0), (0.65, 0.0)),
        Flow("a2", "p2", "قواعد وأولوية إسناد", (0.5, 1.0), (0.65, 0.0)),
        Flow("a3", "p5", "قرار حل وتصحيح", (0.5, 1.0), (0.65, 0.0)),
        Flow("p6", "a4", "تقرير موقع قبل وبعد", (0.35, 0.0), (0.5, 1.0)),
        Flow("p6", "a1", "نتيجة الترحيل والتفعيل", (0.0, 0.20), (0.0, 0.70)),
    ]
    extra_flows = [
        Flow(
            "p3",
            "p5",
            "تطابق متعدد أو معدوم",
            (0.25, 1.0),
            (0.75, 1.0),
            ((1640, 1010), (820, 1010)),
            color=RED,
        ),
        Flow(
            "p5",
            "p2",
            "تصحيح قاعدة وإعادة معاينة",
            (1.0, 0.82),
            (0.0, 0.82),
            ((820, 1080), (2150, 1080), (2150, 570)),
            dashed=True,
        ),
        Flow(
            "d4",
            "p6",
            "إسنادات ناجحة",
            (0.35, 1.0),
            (0.65, 1.0),
            ((1260, 1160), (380, 1160)),
        ),
        Flow(
            "d5",
            "p6",
            "حالات محلولة أو باقية",
            (0.35, 1.0),
            (0.65, 1.0),
            ((820, 1230), (450, 1230)),
        ),
    ]
    pipeline_page(
        mxfile,
        24,
        "24 - رحلة إسناد الجهة والحجر",
        "مخطط تفصيلي - إسناد Tenant والحجر والمصالحة",
        "كل سجل يخرج من الدفعة منسوباً إلى جهة واحدة أو محجوراً بسبب واضح وقابلاً للمراجعة.",
        (
            "1.3.1 تحديد الدفعة والمصدر وإنشاء المعاينة",
            "1.3.2 تحميل قواعد الإسناد وتقييمها",
            "1.3.3 تصنيف تطابق واحد أو متعدد أو معدوم",
            "1.3.4 إسناد TenantId أو إنشاء قيد حجر",
            "1.3.5 مراجعة الحجر وحل السبب أو إبقاؤه",
            "1.3.6 المصالحة وتوقيع التقرير والتفعيل",
        ),
        (
            "D1.3.1 الدفعات والمصادر والمعاينات",
            "D1.3.2 TenantAssignmentRule وإصداراتها",
            "D1.3.3 نتائج المطابقة والتفسير",
            "D1.3.4 الإسنادات المنفذة",
            "D1.3.5 TenantQuarantineEntry وقرارات الحل",
            "D1.3.6 تقارير المصالحة والتدقيق",
        ),
        (
            "دفعة قابلة للتقييم",
            "نتيجة قواعد",
            "تصنيف إسناد",
            "منسوب أو محجور",
            "قرار مراجعة",
        ),
        actors,
        external_flows,
        (
            "حفظ الدفعة والمعاينة",
            "حفظ القواعد والنسخة",
            "حفظ النتيجة والتفسير",
            "حفظ TenantId أو الحجر",
            "حفظ قرار الحل",
            "حفظ التقرير والتوقيع",
        ),
        store_reads=((1, "دفعة المعاينة"), (2, "قواعد فعالة"), (3, "نتائج المطابقة"), (5, "حالات الحجر")),
        extra_flows=extra_flows,
    )


def file_dsar_journey_page(mxfile: ET.Element) -> None:
    actors = [
        Node("a1", "external", "الموظف أو خدمة المجال", 2380, 155, 440, 92),
        Node("a2", "external", "محرك مكافحة البرمجيات وCDR", 1960, 155, 430, 92),
        Node("a3", "external", "طالب وصول مخول", 1070, 155, 360, 92),
        Node("a4", "external", "صاحب البيانات ومسؤول الخصوصية", 590, 155, 470, 92),
        Node("a5", "external", "المدقق", 210, 155, 320, 92),
    ]
    external_flows = [
        Flow("a1", "p1", "ملف ونوع وسياق", (0.5, 1.0), (0.65, 0.0)),
        Flow("p2", "a2", "محتوى محجور", (0.35, 0.0), (0.35, 1.0)),
        Flow("a2", "p3", "نتيجة فحص وتنقية", (0.65, 1.0), (0.65, 0.0)),
        Flow("a3", "p4", "طلب وصول وغرض", (0.5, 1.0), (0.65, 0.0)),
        Flow("p4", "a3", "رابط مؤقت أو رفض", (0.35, 0.0), (0.35, 1.0)),
        Flow("a4", "p5", "طلب وصول أو تصحيح أو حذف", (0.5, 1.0), (0.65, 0.0)),
        Flow("p6", "a4", "حزمة نتيجة أو قرار مسبب", (0.35, 0.0), (0.35, 1.0)),
        Flow("p6", "a5", "أثر تدقيق كامل", (0.0, 0.50), (0.5, 1.0)),
    ]
    extra_flows = [
        Flow(
            "p3",
            "p2",
            "فشل فحص وإعادة محاولة",
            (1.0, 0.82),
            (0.0, 0.82),
            ((1600, 1030), (2180, 1030), (2180, 570)),
            dashed=True,
            color=RED,
        ),
        Flow(
            "d3",
            "p4",
            "ملف نشط وبصمة",
            (0.0, 0.50),
            (1.0, 0.72),
        ),
        Flow(
            "d4",
            "p6",
            "أثر منح واستخدام",
            (0.35, 1.0),
            (0.65, 1.0),
            ((1260, 1070), (380, 1070)),
        ),
    ]
    pipeline_page(
        mxfile,
        25,
        "25 - رحلة الملف والوصول وحقوق البيانات",
        "مخطط تفصيلي - رفع الملف ومنح الوصول وDSAR",
        "لا يصبح الملف متاحاً قبل نجاح الفحص؛ كل وصول مؤقت ومسبب ومسجل، وطلبات الخصوصية قابلة للتتبع.",
        (
            "9.1.1 التحقق من النوع والحجم وتسجيل Metadata",
            "9.1.2 رفع المحتوى إلى الحجر وإنشاء FileId",
            "9.1.3 الفحص وCDR والتحقق من البصمة والترقية",
            "9.1.4 تقييم المنحة وإصدار وصول مؤقت",
            "9.1.5 تسجيل DSAR وتقييم الاحتفاظ والاستثناء",
            "9.1.6 التنفيذ وتجميع النتيجة والتدقيق",
        ),
        (
            "D9.1.1 FileMetadata والسياق",
            "D9.1.2 مخزن الحجر وحالات الفحص",
            "D9.1.3 المخزن النشط وSHA-256",
            "D9.1.4 AccessGrant وAccessAudit",
            "D9.1.5 DSAR وRetentionPolicy",
            "D9.1.6 نتيجة التنفيذ وسجل التدقيق",
        ),
        (
            "Metadata صالح",
            "FileId محجور",
            "ملف نظيف نشط",
            "منحة أو رفض",
            "طلب خصوصية مقرر",
        ),
        actors,
        external_flows,
        (
            "حفظ البيانات الوصفية",
            "حفظ المحتوى والحالة",
            "حفظ الملف والبصمة",
            "حفظ المنحة والاستخدام",
            "حفظ الطلب والقرار",
            "حفظ النتيجة والأثر",
        ),
        store_reads=((1, "سياق الملف"), (2, "ملف محجور"), (3, "ملف نشط"), (4, "منحة فعالة"), (5, "طلب واحتفاظ")),
        extra_flows=extra_flows,
    )


def policy_workflow_code_journey_page(mxfile: ET.Element) -> None:
    actors = [
        Node("a1", "external", "خدمة المجال", 2450, 155, 300, 92),
        Node("a2", "external", "مسؤول السياسات", 2010, 155, 320, 92),
        Node("a3", "external", "المعتمد أو المفوض", 1070, 155, 360, 92),
        Node("a4", "external", "خدمات المجال وسجل الأحداث", 170, 155, 470, 92),
    ]
    external_flows = [
        Flow("a1", "p1", "حقائق وطلب قرار", (0.5, 1.0), (0.65, 0.0)),
        Flow("a2", "p2", "إصدار منشور وقواعد", (0.5, 1.0), (0.65, 0.0)),
        Flow("a3", "p5", "موافقة أو رفض أو تفويض", (0.5, 1.0), (0.65, 0.0)),
        Flow("p6", "a4", "قرار ورمز وحدث مترابط", (0.35, 0.0), (0.5, 1.0)),
    ]
    extra_flows = [
        Flow(
            "p3",
            "p6",
            "قرار مباشر بلا موافقة",
            (0.25, 1.0),
            (0.75, 1.0),
            ((1640, 1010), (390, 1010)),
            dashed=True,
        ),
        Flow(
            "d5",
            "p6",
            "قرار موقع",
            (0.0, 0.50),
            (1.0, 0.72),
        ),
    ]
    pipeline_page(
        mxfile,
        26,
        "26 - رحلة السياسة وسير العمل والرمز",
        "مخطط تفصيلي - من حقائق المجال إلى القرار والرمز والحدث",
        "يحل إصداراً منشوراً غير قابل للتعديل، يسجل الإنفاذ، ويبدأ سير العمل عند الحاجة فقط.",
        (
            "11.1.1 استقبال حقائق المجال وسياق الجهة",
            "11.1.2 حل إصدار السياسة المنشور",
            "11.1.3 التقييم وتسجيل دفتر الإنفاذ",
            "11.1.4 بدء مثيل سير العمل عند اشتراط الموافقة",
            "11.1.5 التوقيع والتفويض وإصدار القرار",
            "11.1.6 توليد الرمز ونشر الحدث والنتيجة",
        ),
        (
            "D11.1.1 الطلب والحقائق والسياق",
            "D11.1.2 PolicyVersion وRules",
            "D11.1.3 Evaluation وEnforcementLedger",
            "D11.1.4 WorkflowInstance والخطوات",
            "D11.1.5 Signature وDelegation وDecision",
            "D11.1.6 CodeIssuance وOutbox",
        ),
        (
            "طلب قرار",
            "إصدار محلول",
            "قرار أولي",
            "مسار موافقة",
            "قرار موقع",
        ),
        actors,
        external_flows,
        (
            "حفظ الطلب والحقائق",
            "حفظ مرجع الإصدار",
            "حفظ التقييم والإنفاذ",
            "حفظ المثيل والخطوات",
            "حفظ التوقيع والقرار",
            "حفظ الرمز والحدث",
        ),
        store_reads=((1, "حقائق الطلب"), (2, "قواعد منشورة"), (3, "قرار أولي"), (4, "الخطوة الحالية"), (5, "قرار نافذ")),
        extra_flows=extra_flows,
    )


def integration_flow_page(mxfile: ET.Element) -> None:
    nodes = [
        Node("p1", "process", "12.1.1 تثبيت معاملة المجال ورسالة Outbox", 2450, 410, 300, 112),
        Node("p2", "process", "12.1.2 التقاط الرسالة ونشر عقد التكامل", 2010, 410, 300, 112),
        Node("p3", "process", "12.1.3 التحقق من التكرار وتطبيق المستهلك", 1570, 410, 300, 112),
        Node("p4", "process", "12.1.4 تحديث Saga أو إصدار أمر تعويضي", 1100, 950, 300, 112),
        Node("p5", "process", "12.1.5 تحديث نموذج القراءة والتحليلات", 650, 950, 300, 112),
        Node("p6", "process", "12.1.6 كشف التعطل والمصالحة وإعادة التشغيل", 200, 950, 300, 112),
        Node("d1", "store", "D12.1.1 بيانات خدمة المنتج", 2470, 740, 260, 90),
        Node("d2", "store", "D12.1.2 OutboxMessage وOutboxState", 2030, 740, 260, 90),
        Node("d3", "store", "D12.1.3 بيانات المستهلك وInbox Receipt", 1590, 740, 260, 90),
        Node("d4", "store", "D12.1.4 SagaState وProcessIntervention", 1120, 1250, 260, 90),
        Node("d5", "store", "D12.1.5 ReadModel ولقطة التقارير", 670, 1250, 260, 90),
        Node("d6", "store", "D12.1.6 سجل المصالحة والتدقيق", 220, 1250, 260, 90),
        Node("producer", "external", "خدمة مجال منتجة", 2450, 155, 300, 92),
        Node("broker", "external", "RabbitMQ / MassTransit", 1710, 155, 430, 92),
        Node("ops", "external", "التشغيل والمراقبة", 200, 705, 300, 92),
        Node(
            "inventory",
            "note",
            "النطاق التنفيذي: 14 خدمة مجال، وOutbox في 8 خدمات، وSagas لرحلات "
            "التعيين والفصل والنقل والإعارة. لا توجد معاملة موزعة بين قواعد الخدمات.",
            1570,
            1000,
            1180,
            90,
        ),
        Node(
            "rule",
            "note",
            "يحمل عقد الحدث TenantId وCorrelationId ومعرفاً ثابتاً للرسالة. "
            "يمنع Inbox التكرار، وتتعامل Saga مع الاتساق النهائي والتعويض.",
            1570,
            1140,
            1180,
            90,
        ),
    ]
    flows = [
        Flow("producer", "p1", "أمر مجال وحقائق", (0.5, 1.0), (0.65, 0.0)),
        store_write("p1", "d1", "حفظ بيانات المجال"),
        Flow("p1", "d2", "حفظ رسالة ضمن المعاملة", (0.62, 1.0), (0.38, 0.0)),
        Flow("d2", "p2", "رسالة معلقة للنشر", (0.66, 0.0), (0.66, 1.0)),
        Flow("p2", "broker", "عقد حدث منشور", (0.35, 0.0), (0.65, 1.0)),
        Flow("broker", "p3", "حدث مسلم للمستهلك", (0.35, 1.0), (0.65, 0.0)),
        store_read("d3", "p3", "فحص MessageId"),
        store_write("p3", "d3", "حفظ الأثر والإيصال"),
        Flow(
            "p3",
            "p4",
            "نتيجة طرف أو حدث مسار",
            (0.20, 1.0),
            (0.80, 0.0),
            ((1630, 870), (1340, 870)),
        ),
        store_write("p4", "d4", "حفظ حالة Saga"),
        store_read("d4", "p4", "الحالة والخطوة الحالية"),
        Flow(
            "p4",
            "broker",
            "أمر متابعة أو تعويض",
            (0.75, 0.0),
            (0.55, 1.0),
            ((1325, 875), (1945, 875), (1945, 300)),
            dashed=True,
            color=RED,
        ),
        Flow(
            "p3",
            "p5",
            "حدث إسقاط تحليلي",
            (0.0, 0.68),
            (0.65, 0.0),
            ((1500, 850), (845, 850)),
        ),
        store_write("p5", "d5", "تحديث نموذج القراءة"),
        store_read("d5", "p5", "لقطة حالية"),
        Flow(
            "p3",
            "p6",
            "فشل أو تأخر أو تعارض",
            (0.0, 0.90),
            (0.65, 0.0),
            ((1480, 820), (395, 820)),
            color=RED,
        ),
        Flow(
            "p4",
            "p6",
            "تدخل مطلوب",
            (0.0, 0.72),
            (1.0, 0.72),
            ((1030, 1160), (530, 1160)),
            color=RED,
        ),
        Flow("ops", "p6", "طلب مصالحة أو إعادة", (0.5, 1.0), (0.65, 0.0)),
        store_write("p6", "d6", "حفظ النتيجة والأثر"),
        store_read("d6", "p6", "حالات مفتوحة"),
        Flow(
            "p6",
            "broker",
            "إعادة نشر موجهة",
            (0.15, 0.0),
            (0.08, 1.0),
            ((245, 650), (160, 650), (160, 290), (1740, 290)),
            dashed=True,
        ),
        Flow("p6", "ops", "تقرير مصالحة وتنبيه", (0.35, 0.0), (0.35, 1.0)),
    ]
    add_page(
        mxfile,
        27,
        "27 - التكامل والأحداث والموثوقية",
        "مخطط تدفق تقني - التكامل بين قواعد الخدمات",
        "يوضح انتقال البيانات دون مفاتيح أجنبية عابرة: معاملة محلية، Outbox، وسيط، Inbox، Saga، ومصالحة.",
        nodes,
        flows,
        height=1850,
        scope_note=(
            "قاعدة لكل خدمة تعني ملكية مستقلة للبيانات لا قاعدة لكل Tenant. "
            "العزل داخل قاعدة الخدمة يتم بواسطة TenantId ومرشحات EF وPostgreSQL RLS."
        ),
    )


def dictionary_page(mxfile: ET.Element) -> None:
    nodes = [
        Node("h1", "heading", "قاموس مخازن البيانات المنطقية", 120, 145, 1320, 50),
        Node(
            "d1",
            "store",
            "D1 الجهة والهيكل والمرجعيات\n"
            "Tenant, AssignmentRule, Quarantine, OrgUnitVersion, FundedPosition, MDM",
            150,
            235,
            1240,
            105,
        ),
        Node(
            "d2",
            "store",
            "D2 التوظيف والموظف والعقود\n"
            "Candidate, Employee, CareerRecord, Contract, Commencement, Separation",
            150,
            375,
            1240,
            105,
        ),
        Node(
            "d3",
            "store",
            "D3 الحركات والملاك والتخطيط\n"
            "WorkforcePlan, JobCatalog, MovementRequest, MovementSnapshot, Reconciliation",
            150,
            515,
            1240,
            105,
        ),
        Node(
            "d4",
            "store",
            "D4 الخدمة الذاتية والتدريب والصحة\n"
            "Leave, Advance, Training, Mission, HealthRecord, MedicalDecision",
            150,
            655,
            1240,
            105,
        ),
        Node(
            "d5",
            "store",
            "D5 الرواتب والتمويل والدفع\n"
            "Snapshot, RunJob, SalaryLine, Funding, Arrears, PaymentOrder, FraudAlert",
            150,
            795,
            1240,
            105,
        ),
        Node(
            "d6",
            "store",
            "D6 التوريد والمخزون والأصول\n"
            "SupplyOrderLine, Receipt, Inventory, Asset, Assignment, LossReport",
            150,
            935,
            1240,
            105,
        ),
        Node(
            "d7",
            "store",
            "D7 الهوية والملفات والأرشيف والامتثال\n"
            "IDCard, FileMetadata, ScanState, AccessGrant, Archive, DSAR, Complaint, Audit",
            150,
            1075,
            1240,
            105,
        ),
        Node(
            "d8",
            "store",
            "D8 السياسات وسير العمل والرموز والتكامل\n"
            "PolicyVersion, Workflow, Signature, CodeIssuance, Outbox, Inbox, Saga, ReadModel",
            150,
            1215,
            1240,
            105,
        ),
        Node("h2", "heading", "خريطة التفكيك والاتزان", 1560, 145, 1320, 50),
        Node(
            "m1",
            "process",
            "0 النظام الكامل\nالسياق: الصفحة 01\nالتفكيك الرئيسي: الصفحة 02",
            1580,
            235,
            580,
            125,
        ),
        Node(
            "m2",
            "process",
            "1.0 الجهة والهيكل\nالصفحة 03\nالإسناد والحجر: الصفحة 20",
            2240,
            235,
            580,
            125,
        ),
        Node(
            "m3",
            "process",
            "2.0 الموظف والعقود\nالصفحة 04\nالتعيين والفصل: 11 و18",
            1580,
            410,
            580,
            125,
        ),
        Node(
            "m4",
            "process",
            "3.0 الحركات والملاك\nالصفحة 05\nالنقل والندب: الصفحة 12",
            2240,
            410,
            580,
            125,
        ),
        Node(
            "m5",
            "process",
            "4.0 الخدمة والتدريب والصحة\nالصفحة 06\nالرحلات: 13 و14 و15",
            1580,
            585,
            580,
            125,
        ),
        Node(
            "m6",
            "process",
            "5.0 الرواتب والدفع\nالصفحة 07\nرحلة التشغيل: الصفحة 16",
            2240,
            585,
            580,
            125,
        ),
        Node(
            "m7",
            "process",
            "6.0 اللوجستيات والعهد\nالصفحة 08\nرحلة الأصل: الصفحة 19",
            1580,
            760,
            580,
            125,
        ),
        Node(
            "m8",
            "process",
            "7.0 الملفات والامتثال\nالصفحة 09\nالرحلات: 17 و21",
            2240,
            760,
            580,
            125,
        ),
        Node(
            "m9",
            "process",
            "8.0 محركات الحوكمة\nالصفحة 10\nالسياسة والتكامل: 22 و23",
            1580,
            935,
            1240,
            125,
        ),
        Node(
            "symbols",
            "note",
            "الدلالة: المستطيل الأخضر جهة خارجية؛ المستطيل المستدير عملية؛ "
            "الشكل المفتوح مخزن بيانات؛ المتقطع اتصال بعملية أخرى أو مسار تعويض؛ "
            "الأحمر استثناء أو فشل يحتاج معالجة.",
            1580,
            1110,
            1240,
            100,
        ),
        Node(
            "rules",
            "note",
            "قواعد السلامة: كل سهم أحادي الاتجاه ومسمى؛ لا اتصال بين جهة خارجية "
            "ومخزن؛ لا اتصال بين مخزنين؛ لا سهم قراءة/كتابة مزدوج؛ تتوازن حدود "
            "الجهات والتدفقات بين السياق والمستوى الأول والتفصيل.",
            1580,
            1250,
            1240,
            100,
        ),
        Node(
            "ownership",
            "note",
            "D1-D8 مجموعات منطقية فقط. التنفيذ الفعلي يستخدم قواعد مستقلة للخدمات، "
            "ولا توجد مفاتيح أجنبية بين قواعدها؛ التبادل عبر عقود وأحداث موثقة.",
            1580,
            1390,
            1240,
            90,
        ),
    ]
    add_page(
        mxfile,
        24,
        "24 - قاموس البيانات والاتزان",
        "قاموس البيانات وخريطة اتزان حزمة DFD",
        "تعريف المخازن المنطقية، مواضع التفكيك، ودلالات الرسم وقواعد السلامة.",
        nodes,
        [],
        height=1600,
        scope_note=None,
    )


def complete_dictionary_page(mxfile: ET.Element) -> None:
    stores = (
        ("D1 الجهات والتهيئة والإسناد والحجر وMDM", "Tenant, AgencyManifest, AssignmentRule, Quarantine, NationalPackage, ServiceTrust"),
        ("D2 الهيكل المؤرخ والملاك والوظائف الممولة", "OrgUnitVersion, JobCatalogEntry, FundedPosition, CostCenter, WorkforcePlan"),
        ("D3 المرشحون والموظفون والعقود والخدمة", "Candidate, Employee, CareerRecord, Contract, Commencement, ServiceTransaction"),
        ("D4 الحركات والتوزيع والفصل والتسويات", "MovementRequest, MovementSnapshot, SeparationCase, Checklist, Reconciliation"),
        ("D5 الخدمة الذاتية والإجازات والسلف", "ESSRequest, Leave, Balance, Advance, Approval, ServiceImpact"),
        ("D6 التدريب والبعثات والصحة المهنية", "Training, Mission, Nomination, HealthRecord, MedicalDecision, AccessGrant"),
        ("D7 الرواتب والتمويل والدفع", "InputSnapshot, PayrollRun, Arrears, Funding, PaymentOrder, BankAccount, FraudAlert"),
        ("D8 التوريد والمخزون والأصول والعهد", "SupplyOrderLine, Receipt, Inventory, Asset, Lineage, Assignment, LossReport"),
        ("D9 الهوية والملفات والأرشيف وحقوق البيانات", "IDCard, FileMetadata, ScanState, AccessGrant, Archive, Retention, DSAR"),
        ("D10 الشكاوى والانضباط والامتثال", "Complaint, Evidence, Investigation, Decision, Appeal, Discipline, ComplianceAudit"),
        ("D11 السياسات والسير والتوقيعات والرموز", "PolicyVersion, Evaluation, Enforcement, Workflow, Signature, Delegation, CodeIssuance"),
        ("D12 التكامل والأحداث والتقارير", "Outbox, Inbox, ContractVersion, Saga, ReadModel, Reconciliation, DeadLetter"),
    )
    mapping = (
        ("1.0 حوكمة المنصة والجهات", "المستوى الثاني: 03\nالإسناد والحجر: 24"),
        ("2.0 الهيكل والملاك", "المستوى الثاني: 04"),
        ("3.0 التوظيف والموظف والعقود", "المستوى الثاني: 05\nرحلة التعيين: 15"),
        ("4.0 الحركات وإنهاء الخدمة", "المستوى الثاني: 06\nالحركة: 16، الفصل: 22"),
        ("5.0 الخدمة الذاتية", "المستوى الثاني: 07\nالإجازة والسلفة: 17"),
        ("6.0 التدريب والصحة", "المستوى الثاني: 08\nالطبي: 18، التدريب: 19"),
        ("7.0 الرواتب والدفع", "المستوى الثاني: 09\nتشغيل الراتب: 20"),
        ("8.0 اللوجستيات والعهد", "المستوى الثاني: 10\nالأصل والعهدة: 23"),
        ("9.0 الهوية والملفات والحقوق", "المستوى الثاني: 11\nالملف وDSAR: 25"),
        ("10.0 الشكاوى والامتثال", "المستوى الثاني: 12\nالشكوى والانضباط: 21"),
        ("11.0 السياسات والسير والرموز", "المستوى الثاني: 13\nرحلة القرار والرمز: 26"),
        ("12.0 التكامل والتقارير", "المستوى الثاني: 14\nالموثوقية والأحداث: 27"),
    )
    nodes: list[Node] = [
        Node("h1", "heading", "قاموس مخازن المستوى الأول D1-D12", 100, 145, 1320, 50),
        Node("h2", "heading", "خريطة التفكيك والاتزان", 1540, 145, 1320, 50),
    ]
    for index, (title, detail) in enumerate(stores, 1):
        nodes.append(
            Node(
                f"d{index}",
                "store",
                f"{title}\n{detail}",
                120,
                220 + (index - 1) * 112,
                1300,
                86,
            )
        )
    for index, (title, detail) in enumerate(mapping, 1):
        column = (index - 1) % 2
        row = (index - 1) // 2
        nodes.append(
            Node(
                f"m{index}",
                "process",
                f"{title}\n{detail}",
                1540 + column * 650,
                220 + row * 160,
                600,
                120,
            )
        )
    nodes.extend(
        [
            Node(
                "balance",
                "note",
                "اتزان الحدود: تحمل تدفقات الصفحتين 01 و02 مفاتيح CTX-A1 إلى CTX-A9. "
                "يجب أن يظهر كل تدفق داخل أو خارج في المجموعتين، ويجوز تفكيك التدفق المجمع "
                "إلى عدة تدفقات في المستوى الأول مع المفتاح نفسه.",
                1540,
                1200,
                1250,
                105,
            ),
            Node(
                "numbering",
                "note",
                "قاعدة الترقيم: Pn في المستوى الأول؛ n.x في المستوى الثاني؛ n.x.y في رحلة "
                "التفصيل. يحمل كل مخزن الرقم المناظر Dn أو Dn.x أو Dn.x.y.",
                1540,
                1340,
                1250,
                90,
            ),
            Node(
                "old-map-1",
                "note",
                "مطابقة المخطط المعتمد السابق: P1 الخدمة والعقود ← P3 وP4 وP5؛ "
                "P2 الملفات الشخصية والصحية ← P6 وP9؛ P3 التنقلات ← P4؛ P4 التدريب ← P6.",
                1540,
                1480,
                1250,
                95,
            ),
            Node(
                "old-map-2",
                "note",
                "P5 السيرة الوظيفية ← P3 وP4 وP6؛ P6 الرواتب ← P7؛ "
                "P7 الأرشفة وP8 الهويات والوثائق ← P9؛ P9 الرقابة والشكاوى ← P10.",
                1540,
                1610,
                1250,
                95,
            ),
            Node(
                "old-map-3",
                "note",
                "P10 التخطيط والإحصاء ← P2 وP12؛ P11 التنظيم والتجهيز ← P2 وP8؛ "
                "P12 تقنية المعلومات والتطوير ← P11 وP12 بوصفهما عمليتي بيانات واضحتين.",
                1540,
                1740,
                1250,
                95,
            ),
            Node(
                "safety",
                "note",
                "قواعد DFD: كل سهم أحادي ومسمى؛ لا اتصال مباشر بين جهة خارجية ومخزن؛ "
                "لا اتصال بين مخزنين؛ القراءة والكتابة سهمان منفصلان؛ ولا تعد مخازن D1-D12 "
                "قواعد مشتركة بل حدود ملكية منطقية.",
                1540,
                1870,
                1250,
                105,
            ),
            Node(
                "ownership",
                "note",
                "الملكية التنفيذية: قاعدة مستقلة لكل خدمة، لا قاعدة لكل Tenant. العزل داخل "
                "الخدمة بواسطة TenantId وRLS ومرشحات النطاق، والتبادل عبر العقود والأحداث "
                "المترابطة بـCorrelationId.",
                120,
                1640,
                1300,
                105,
            ),
            Node(
                "symbols",
                "note",
                "الدلالة: الأخضر جهة خارجية؛ المستدير عملية؛ المفتوح مخزن؛ المتقطع تكامل "
                "عابر أو تعويض؛ الأحمر استثناء يحتاج معالجة؛ والمستطيل الأزرق المتقطع عملية "
                "مساندة داخل حدود المنصة.",
                120,
                1780,
                1300,
                105,
            ),
        ]
    )
    add_page(
        mxfile,
        28,
        "28 - قاموس البيانات والاتزان والمطابقة",
        "قاموس البيانات وخريطة اتزان حزمة DFD",
        "تعريف D1-D12، ربط كل عملية بتفكيكها، وتوثيق الانتقال من المخطط الوظيفي السابق.",
        nodes,
        [],
        height=2200,
        scope_note=None,
    )


def build_workbook() -> ET.ElementTree:
    mxfile = ET.Element(
        "mxfile",
        {
            "host": "app.diagrams.net",
            "modified": datetime.now(timezone.utc).isoformat(timespec="seconds"),
            "agent": "HRManagement authoritative DFD generator",
            "version": "24.7.17",
            "type": "device",
            "compressed": "false",
        },
    )
    context_page(mxfile)
    level_one_page(mxfile)
    tenant_governance_page(mxfile)
    organization_workforce_page(mxfile)
    employee_lifecycle_page(mxfile)
    movements_separation_page(mxfile)
    employee_self_service_page(mxfile)
    training_health_domain_page(mxfile)
    payroll_complete_domain_page(mxfile)
    logistics_complete_domain_page(mxfile)
    identity_files_rights_page(mxfile)
    complaints_compliance_page(mxfile)
    governance_engines_complete_page(mxfile)
    integration_reporting_domain_page(mxfile)
    hire_journey_page(mxfile)
    transfer_journey_page(mxfile)
    leave_advance_journey_page(mxfile)
    medical_journey_page(mxfile)
    training_journey_page(mxfile)
    payroll_journey_page(mxfile)
    complaint_journey_page(mxfile)
    separation_journey_page(mxfile)
    asset_journey_page(mxfile)
    tenant_assignment_journey_page(mxfile)
    file_dsar_journey_page(mxfile)
    policy_workflow_code_journey_page(mxfile)
    integration_flow_page(mxfile)
    complete_dictionary_page(mxfile)
    ET.indent(mxfile, space="  ")
    return ET.ElementTree(mxfile)


def validate_workbook(tree: ET.ElementTree) -> dict[str, object]:
    diagrams = tree.getroot().findall("diagram")
    names = [diagram.get("name", "") for diagram in diagrams]
    diagram_ids = [diagram.get("id", "") for diagram in diagrams]
    errors: list[str] = []
    page_stats: list[dict[str, object]] = []
    if len(diagrams) != 28:
        errors.append(f"Expected 28 authoritative pages, found {len(diagrams)}")
    if len(names) != len(set(names)):
        errors.append("Duplicate page names")
    if len(diagram_ids) != len(set(diagram_ids)):
        errors.append("Duplicate page ids")
    for index, name in enumerate(names, 1):
        if not name.startswith(f"{index:02d} - "):
            errors.append(f"Page {index}: name does not carry its authoritative number: {name}")
    if any("قديم" in name or "جديد" in name for name in names):
        errors.append("Page names must not split the package into old/new sets")

    for diagram in diagrams:
        name = diagram.get("name", "")
        model = diagram.find("mxGraphModel")
        if model is None:
            errors.append(f"{name}: missing mxGraphModel")
            continue
        page_width = float(model.get("pageWidth", PAGE_WIDTH))
        page_height = float(model.get("pageHeight", PAGE_HEIGHT))
        root = model.find("root")
        if root is None:
            errors.append(f"{name}: missing graph root")
            continue
        cells = {cell.get("id", ""): cell for cell in root.findall("mxCell")}
        vertices = [cell for cell in cells.values() if cell.get("vertex") == "1"]
        edges = [cell for cell in cells.values() if cell.get("edge") == "1"]
        kinds = {cell_id: cell.get("dfdKind", "") for cell_id, cell in cells.items()}
        for vertex in vertices:
            geometry = vertex.find("mxGeometry")
            if geometry is None:
                continue
            x = float(geometry.get("x", "0"))
            y = float(geometry.get("y", "0"))
            width = float(geometry.get("width", "0"))
            height = float(geometry.get("height", "0"))
            if x < 0 or y < 0 or x + width > page_width + 1 or y + height > page_height + 1:
                errors.append(f"{name}: vertex outside page: {vertex.get('id')}")
        for edge in edges:
            source = edge.get("source", "")
            target = edge.get("target", "")
            if source not in cells or target not in cells:
                errors.append(f"{name}: dangling edge {edge.get('id')}")
                continue
            if not edge.get("value", "").strip():
                errors.append(f"{name}: unnamed edge {edge.get('id')}")
            source_kind = kinds.get(source, "")
            target_kind = kinds.get(target, "")
            if {source_kind, target_kind} == {"external", "store"}:
                errors.append(f"{name}: direct external-store flow {edge.get('id')}")
            if source_kind == "store" and target_kind == "store":
                errors.append(f"{name}: direct store-store flow {edge.get('id')}")
            style = edge.get("style", "")
            if "startArrow=classic" in style or "startArrow=block" in style:
                errors.append(f"{name}: bidirectional edge style {edge.get('id')}")
        page_stats.append(
            {
                "name": name,
                "vertices": len(vertices),
                "flows": len(edges),
            }
        )

    if len(diagrams) >= 2:
        context_root = diagrams[0].find("mxGraphModel/root")
        level_one_root = diagrams[1].find("mxGraphModel/root")
        if context_root is not None and level_one_root is not None:
            context_keys = [
                cell.get("balanceKey", "")
                for cell in context_root.findall("mxCell")
                if cell.get("edge") == "1"
            ]
            level_one_keys = [
                cell.get("balanceKey", "")
                for cell in level_one_root.findall("mxCell")
                if cell.get("edge") == "1" and cell.get("balanceKey")
            ]
            if any(not key for key in context_keys):
                errors.append("Context page contains a boundary flow without balanceKey")
            if len(context_keys) != len(set(context_keys)):
                errors.append("Context balance keys must be unique aggregate boundary flows")
            missing_keys = sorted(set(context_keys) - set(level_one_keys))
            extra_keys = sorted(set(level_one_keys) - set(context_keys))
            if missing_keys:
                errors.append("Level 1 misses context balance keys: " + ", ".join(missing_keys))
            if extra_keys:
                errors.append("Level 1 has unknown context balance keys: " + ", ".join(extra_keys))

        level_one_cells = diagrams[1].findall("mxGraphModel/root/mxCell")
        level_one_processes = [cell for cell in level_one_cells if cell.get("dfdKind") == "process"]
        level_one_stores = [cell for cell in level_one_cells if cell.get("dfdKind") == "store"]
        if len(level_one_processes) != 12 or len(level_one_stores) != 12:
            errors.append(
                "Level 1 must contain exactly 12 processes and 12 logical stores "
                f"(found {len(level_one_processes)} and {len(level_one_stores)})"
            )

    for page_number in range(3, min(len(diagrams), 14) + 1):
        parent = page_number - 2
        cells = diagrams[page_number - 1].findall("mxGraphModel/root/mxCell")
        processes = [cell for cell in cells if cell.get("dfdKind") == "process"]
        stores = [cell for cell in cells if cell.get("dfdKind") == "store"]
        if len(processes) != 6 or len(stores) != 6:
            errors.append(
                f"Page {page_number}: level-2 decomposition must have 6 processes and 6 stores"
            )
        if any(f"{parent}." not in cell.get("value", "") for cell in processes):
            errors.append(f"Page {page_number}: process numbering is not under {parent}.0")
        if any(f"D{parent}." not in cell.get("value", "") for cell in stores):
            errors.append(f"Page {page_number}: store numbering is not under D{parent}")

    journey_parents = {
        15: "3.1",
        16: "4.1",
        17: "5.1",
        18: "6.2",
        19: "6.1",
        20: "7.1",
        21: "10.1",
        22: "4.2",
        23: "8.1",
        24: "1.3",
        25: "9.1",
        26: "11.1",
        27: "12.1",
    }
    for page_number, parent in journey_parents.items():
        if page_number > len(diagrams):
            continue
        cells = diagrams[page_number - 1].findall("mxGraphModel/root/mxCell")
        processes = [cell for cell in cells if cell.get("dfdKind") == "process"]
        stores = [cell for cell in cells if cell.get("dfdKind") == "store"]
        if len(processes) != 6 or len(stores) != 6:
            errors.append(
                f"Page {page_number}: journey must have 6 processes and 6 stores"
            )
        if any(f"{parent}." not in cell.get("value", "") for cell in processes):
            errors.append(f"Page {page_number}: journey process numbering is not under {parent}")
        if any(f"D{parent}." not in cell.get("value", "") for cell in stores):
            errors.append(f"Page {page_number}: journey store numbering is not under D{parent}")

    xml_text = ET.tostring(tree.getroot(), encoding="unicode")
    if "image=data:" in xml_text or "data:image/" in xml_text:
        errors.append("Workbook contains embedded raster images")
    missing_terms = [term for term in REQUIRED_DFD_TERMS if term not in xml_text]
    if missing_terms:
        errors.append("Required current-scope terms are missing: " + ", ".join(missing_terms))
    report = {
        "valid": not errors,
        "page_count": len(diagrams),
        "required_scope_terms": len(REQUIRED_DFD_TERMS),
        "missing_scope_terms": missing_terms,
        "page_stats": page_stats,
        "errors": errors,
    }
    return report


def write_renderer(tree: ET.ElementTree) -> None:
    pages: list[dict[str, object]] = []
    for index, diagram in enumerate(tree.getroot().findall("diagram"), 1):
        model = diagram.find("mxGraphModel")
        if model is None:
            continue
        pages.append(
            {
                "index": index,
                "name": diagram.get("name", f"Page {index}"),
                "filename": f"approved-dfd-{index:02d}.png",
                "model": ET.tostring(
                    model,
                    encoding="unicode",
                    short_empty_elements=True,
                ),
            }
        )
    RENDER_MANIFEST.parent.mkdir(parents=True, exist_ok=True)
    RENDER_MANIFEST.write_text(
        json.dumps(
            [
                {key: value for key, value in page.items() if key != "model"}
                for page in pages
            ],
            ensure_ascii=False,
            indent=2,
        ),
        encoding="utf-8",
    )
    page_json = json.dumps(pages, ensure_ascii=False).replace("</", "<\\/")
    RENDER_HTML.write_text(
        f"""<!doctype html>
<html lang="ar" dir="ltr">
<head>
  <meta charset="utf-8">
  <title>HRManagement Authoritative DFD</title>
  <script src="mxClient.js"></script>
  <style>
    html,body{{margin:0;padding:0;background:#fff}}
    #canvas{{display:block;background:#fff;overflow:hidden}}
  </style>
</head>
<body>
  <div id="canvas"></div>
  <script>
    const pages={page_json};
    const pageNumber=Number(new URLSearchParams(location.search).get('page')||'1');
    const page=pages.find(item=>item.index===pageNumber)||pages[0];
    function renderPage(){{
      const documentXml=mxUtils.parseXml(page.model);
      const model=new mxGraphModel();
      const codec=new mxCodec(documentXml);
      codec.decode(documentXml.documentElement,model);
      const container=document.getElementById('canvas');
      const graph=new mxGraph(container,model);
      graph.setEnabled(false);
      graph.setConnectable(false);
      graph.setPanning(false);
      graph.setTooltips(false);
      graph.setHtmlLabels(true);
      graph.view.validate();
      const root=documentXml.documentElement;
      container.style.width=Number(root.getAttribute('pageWidth')||'{PAGE_WIDTH}')+'px';
      container.style.height=Number(root.getAttribute('pageHeight')||'{PAGE_HEIGHT}')+'px';
      graph.refresh();
      document.body.dataset.ready='true';
      document.title=page.name;
    }}
    if(typeof mxClient!=='undefined'){{
      mxEvent.disableContextMenu(document.getElementById('canvas'));
      mxEvent.addListener(window,'load',renderPage);
    }}
  </script>
</body>
</html>""",
        encoding="utf-8",
    )


def main() -> None:
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    tree = build_workbook()
    report = validate_workbook(tree)
    VALIDATION_REPORT.parent.mkdir(parents=True, exist_ok=True)
    VALIDATION_REPORT.write_text(
        json.dumps(report, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )
    if not report["valid"]:
        raise SystemExit(
            "DFD validation failed:\n" + "\n".join(str(item) for item in report["errors"])
        )
    tree.write(
        OUTPUT,
        encoding="utf-8",
        xml_declaration=True,
        short_empty_elements=True,
    )
    write_renderer(tree)
    print(
        json.dumps(
            {
                "output": str(OUTPUT),
                "pages": report["page_count"],
                "validation": str(VALIDATION_REPORT),
                "renderer": str(RENDER_HTML),
            },
            ensure_ascii=False,
        )
    )


if __name__ == "__main__":
    main()
