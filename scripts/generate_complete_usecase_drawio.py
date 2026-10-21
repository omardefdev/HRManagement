"""Generate the canonical, requirement-traceable HRManagement use-case workbook.

The workbook keeps the visual language of the approved departmental Draw.io
package while replacing duplicated departmental views with role-oriented pages.
Every current functional requirement is represented exactly once by a use-case
ellipse. Non-functional requirements remain in their proper supplementary
specification instead of being misrepresented as use cases.
"""

from __future__ import annotations

import argparse
import html
import json
import math
import re
import shutil
import sys
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]
CATALOG = ROOT / "docs" / "THESIS_REQUIREMENTS_CATALOG_CURRENT.md"
DEFAULT_OUTPUT = ROOT / "docs" / "output" / "HRManagement_UseCases_Complete.drawio"
SOURCE_COPY = ROOT / "docs" / "thesis_diagrams" / "source" / "HRManagement_UseCases_Complete.drawio"
DEFAULT_USER_COPY = Path(
    r"C:\Users\omard\OneDrive\Documents\usecase\نهائي\HRManagement_UseCases_Complete.drawio"
)
COVERAGE_OUTPUT = ROOT / "docs" / "output" / "HRManagement_UseCases_Complete_coverage.json"
MANIFEST_OUTPUT = ROOT / "tmp" / "usecase_complete_manifest.json"
RENDERER_OUTPUT = ROOT / "tmp" / "usecase_complete_renderer.html"


PAGE_WIDTH = 1294
PAGE_HEIGHT = 1228
BOUNDARY_X = 190
BOUNDARY_Y = 70
BOUNDARY_W = 920
BOUNDARY_H = 1090
USECASE_W = 330
USECASE_H = 104
LEFT_UC_X = 250
RIGHT_UC_X = 714
LEFT_ACTOR_X = 20
RIGHT_ACTOR_X = 1125
ACTOR_W = 145
ACTOR_H = 62
ROW_Y = [130, 340, 550, 760, 970]


@dataclass(frozen=True)
class Requirement:
    id: str
    domain: str
    actor: str
    action: str


@dataclass(frozen=True)
class PageSpec:
    key: str
    title: str
    prefixes: tuple[str, ...]
    relations: tuple[tuple[str, str, str], ...] = ()


PAGE_SPECS = (
    PageSpec(
        "tenant",
        "حوكمة المنصة وتعدد الجهات والإسناد والحجر",
        ("TEN",),
        (
            ("FR-TEN-04", "FR-TEN-03", "include"),
            ("FR-TEN-05", "FR-TEN-03", "extend"),
            ("FR-TEN-07", "FR-TEN-06", "extend"),
        ),
    ),
    PageSpec(
        "organization",
        "الهيكل التنظيمي ونطاق المركز والفروع والملاك",
        ("ORG",),
        (
            ("FR-ORG-04", "FR-ORG-05", "include"),
            ("FR-ORG-06", "FR-ORG-01", "extend"),
            ("FR-ORG-08", "FR-ORG-07", "include"),
        ),
    ),
    PageSpec(
        "policy",
        "محرك السياسات والإصدارات والمحاكاة والإنفاذ",
        ("POL",),
        (
            ("FR-POL-01", "FR-POL-02", "include"),
            ("FR-POL-04", "FR-POL-06", "include"),
            ("FR-POL-05", "FR-POL-03", "extend"),
        ),
    ),
    PageSpec(
        "code-mdm",
        "توليد الرموز والبيانات المرجعية وتهيئة الجهة",
        ("CODE", "MDM"),
        (
            ("FR-CODE-02", "FR-CODE-01", "extend"),
            ("FR-CODE-03", "FR-CODE-01", "include"),
            ("FR-MDM-04", "FR-MDM-01", "include"),
            ("FR-MDM-04", "FR-CODE-01", "include"),
        ),
    ),
    PageSpec(
        "workflow",
        "سير العمل المؤرخ والموافقات والتوقيعات",
        ("WF",),
        (
            ("FR-WF-03", "FR-WF-02", "extend"),
            ("FR-WF-04", "FR-WF-02", "include"),
        ),
    ),
    PageSpec(
        "employees",
        "ملف الموظف ودورة الحياة والنطاق التنظيمي",
        ("EMP",),
        (("FR-EMP-01", "FR-EMP-05", "include"),),
    ),
    PageSpec(
        "recruitment-public",
        "التوظيف وبوابة المواطن ومسار التعيين",
        ("HIRE", "PUB"),
        (
            ("FR-PUB-02", "FR-PUB-01", "extend"),
            ("FR-HIRE-04", "FR-HIRE-05", "include"),
        ),
    ),
    PageSpec(
        "contracts-leave",
        "العقود وإنهاء الخدمة والإجازات والسلف",
        ("CON", "LEAVE"),
        (
            ("FR-CON-02", "FR-CON-01", "extend"),
            ("FR-CON-02", "FR-CON-04", "include"),
            ("FR-CON-03", "FR-CON-04", "include"),
            ("FR-LEAVE-02", "FR-LEAVE-01", "extend"),
            ("FR-LEAVE-02", "FR-LEAVE-04", "include"),
        ),
    ),
    PageSpec(
        "movements",
        "النقل والانتداب والحركات الوظيفية",
        ("MOV",),
        (
            ("FR-MOV-02", "FR-MOV-01", "extend"),
            ("FR-MOV-02", "FR-MOV-03", "include"),
        ),
    ),
    PageSpec(
        "payroll",
        "الرواتب والتمويل والدفع والتسوية ومكافحة الاحتيال",
        ("PAY",),
        (
            ("FR-PAY-03", "FR-PAY-02", "include"),
            ("FR-PAY-04", "FR-PAY-02", "extend"),
            ("FR-PAY-07", "FR-PAY-06", "include"),
            ("FR-PAY-09", "FR-PAY-08", "include"),
            ("FR-PAY-10", "FR-PAY-09", "extend"),
        ),
    ),
    PageSpec(
        "training-health",
        "التدريب والتأهيل والصحة والسلامة المهنية",
        ("TRN", "HEA"),
        (
            ("FR-TRN-02", "FR-TRN-01", "extend"),
            ("FR-TRN-03", "FR-TRN-02", "extend"),
            ("FR-HEA-02", "FR-HEA-01", "extend"),
            ("FR-HEA-03", "FR-HEA-01", "extend"),
        ),
    ),
    PageSpec(
        "cards-archive",
        "البطاقات والأرشفة والوثائق",
        ("CARD", "ARC"),
        (
            ("FR-CARD-02", "FR-CARD-01", "extend"),
            ("FR-CARD-03", "FR-CARD-01", "extend"),
            ("FR-ARC-03", "FR-ARC-02", "extend"),
        ),
    ),
    PageSpec(
        "logistics",
        "التوريد والمخزون والأصول والعهد والفقد",
        ("LOG",),
        (
            ("FR-LOG-03", "FR-LOG-02", "extend"),
            ("FR-LOG-04", "FR-LOG-03", "include"),
            ("FR-LOG-05", "FR-LOG-04", "extend"),
            ("FR-LOG-07", "FR-LOG-06", "include"),
        ),
    ),
    PageSpec(
        "files-privacy",
        "أمن الملفات والحجر والخصوصية ومنح الوصول",
        ("FILE",),
        (
            ("FR-FILE-02", "FR-FILE-01", "include"),
            ("FR-FILE-04", "FR-FILE-02", "include"),
        ),
    ),
    PageSpec(
        "compliance",
        "الشكاوى والتحقيق والامتثال والتدقيق",
        ("COMP",),
        (
            ("FR-COMP-02", "FR-COMP-01", "extend"),
            ("FR-COMP-03", "FR-COMP-02", "include"),
        ),
    ),
    PageSpec("ess", "الخدمة الذاتية للموظف", ("ESS",)),
    PageSpec("reports-export", "التقارير العربية والتصدير طويل التشغيل", ("REP", "EXP")),
    PageSpec(
        "operations",
        "التشغيل والمراقبة وهوية الخدمات وثقة الاتصال",
        ("OPS",),
        (
            ("FR-OPS-04", "FR-OPS-03", "include"),
            ("FR-OPS-06", "FR-OPS-05", "include"),
        ),
    ),
)


MASTER_CASES = (
    ("FR-TEN / FR-MDM", "حوكمة المنصة والجهات والبيانات المرجعية", "مسؤول المنصة"),
    ("FR-ORG", "الهيكل المؤرخ والملاك ونطاق المركز والفروع", "الإدارة المركزية / مدير الفرع"),
    (
        "FR-EMP / FR-HIRE / FR-CON / FR-LEAVE / FR-MOV",
        "دورة حياة الموظف والتوظيف والعقود والحركات",
        "موظف الموارد البشرية / المواطن",
    ),
    ("FR-POL / FR-WF / FR-CODE", "السياسات وسير العمل والتوقيع وتوليد الرموز", "مسؤول السياسات / المعتمد"),
    ("FR-PAY", "الرواتب والتمويل والدفع والتسوية", "الرواتب / الخزينة / المدقق"),
    ("FR-TRN / FR-HEA / FR-CARD / FR-ARC", "التدريب والصحة والهوية والأرشفة", "موظفو الخدمات المساندة"),
    ("FR-LOG / FR-FILE / FR-COMP", "اللوجستيات والملفات والخصوصية والامتثال", "اللوجستيات / الأمن / الخصوصية"),
    ("FR-ESS / FR-PUB / FR-REP / FR-EXP / FR-OPS", "الخدمة الذاتية والبوابة والتقارير والتشغيل", "الموظف / المواطن / مشغل النظام"),
)


ACTOR_REPLACEMENTS = {
    "النظام": "خدمات النظام",
    "موظف HR": "موظف الموارد البشرية",
    "مدير مركزي/فرعي": "مدير مركزي / مدير فرع",
    "مدير/مركز": "مدير مخول / الإدارة المركزية",
    "موظف HR/الموظف": "موظف الموارد البشرية / الموظف",
    "مسؤول التنظيم/المالية": "مسؤول التنظيم / مسؤول المالية",
    "موظف الرواتب/القانوني": "مسؤول الرواتب / مسؤول قانوني",
    "الخزينة/موظف الرواتب": "مسؤول الخزينة / موظف الرواتب",
    "موظف الرواتب/التسوية": "موظف الرواتب / مسؤول التسوية",
    "مسؤول منصة البيانات/المدقق": "مسؤول منصة البيانات / المدقق",
    "مسؤول السياسات/خدمة المجال": "مسؤول السياسات / خدمة المجال",
    "النظام/مسؤول الأمن": "خدمات النظام / مسؤول الأمن",
    "مسؤول أمني/مشغل النظام": "مسؤول الأمن / مشغل النظام",
    "خدمة مجال": "خدمة المجال",
}


COLORS = {
    "blue": ("#E0F2FE", "#0369A1"),
    "green": ("#ECFDF5", "#059669"),
    "orange": ("#FFF7ED", "#C2410C"),
    "red": ("#FEF2F2", "#DC2626"),
    "gray": ("#F8FAFC", "#475569"),
}


def parse_requirements(path: Path) -> list[Requirement]:
    requirements: list[Requirement] = []
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        if not re.match(r"^\|\s*FR-[A-Z]+-\d+\s*\|", line):
            continue
        parts = [part.strip() for part in line.strip().strip("|").split("|")]
        if len(parts) < 4:
            continue
        requirements.append(Requirement(parts[0], parts[1], parts[2], parts[3]))
    if not requirements:
        raise ValueError(f"No functional requirements found in {path}")
    ids = [item.id for item in requirements]
    if len(ids) != len(set(ids)):
        duplicates = sorted({item for item in ids if ids.count(item) > 1})
        raise ValueError(f"Duplicate requirement identifiers: {duplicates}")
    return requirements


def actor_label(value: str) -> str:
    normalized = ACTOR_REPLACEMENTS.get(value.strip(), value.strip().replace("/", " / "))
    return normalized.replace(" / ", " و")


def wrap_words(text: str, width: int) -> list[str]:
    words = text.split()
    lines: list[str] = []
    current = ""
    for word in words:
        candidate = word if not current else f"{current} {word}"
        if current and len(candidate) > width:
            lines.append(current)
            current = word
        else:
            current = candidate
    if current:
        lines.append(current)
    return lines or [""]


def isolate_word(word: str) -> str:
    escaped = html.escape(word)
    if re.search(r"[A-Za-z0-9]", word):
        return f'<span dir="ltr" style="unicode-bidi:isolate;display:inline-block">{escaped}</span>'
    return escaped


def html_lines(lines: list[str], *, align: str = "center") -> str:
    rendered = []
    for line in lines:
        rendered.append(" ".join(isolate_word(word) for word in line.split()))
    return f'<div dir="rtl" style="text-align:{align};line-height:1.25;">' + "<br>".join(rendered) + "</div>"


def usecase_html(identifier: str, action: str) -> str:
    identifier_lines = wrap_words(identifier, 32)
    identifier_content = "<br>".join(
        f'<span dir="ltr" style="unicode-bidi:isolate;display:inline-block">{html.escape(line)}</span>'
        for line in identifier_lines
    )
    action_lines = wrap_words(action, 34)
    action_content = "<br>".join(
        " ".join(isolate_word(word) for word in line.split()) for line in action_lines
    )
    identifier_size = 13 if len(identifier_lines) > 1 else 14
    return (
        '<div dir="rtl" style="text-align:center;line-height:1.2;">'
        f'<span style="font-size:{identifier_size}px;color:#475569;">{identifier_content}</span>'
        f'<br><span style="font-size:17px;">{action_content}</span></div>'
    )


def classify_color(action: str) -> str:
    if re.search(r"أمن|حجر|فحص|احتيال|عزل|خصوصية|ثقة|منع|حجز دفع", action):
        return "red"
    if re.search(r"اعتماد|رفض|قرار|تسوية|مراجعة|نشر|ترحيل|إقرار", action):
        return "orange"
    if re.search(r"عرض|بحث|تصفية|تتبع|استعلام|تقارير|مقارنة|معاينة", action):
        return "gray"
    if re.search(r"تهيئة|تعريف|إدارة|إصدار|تطبيق حزمة|تشغيل", action):
        return "blue"
    return "green"


def add_vertex(
    root: ET.Element,
    cell_id: str,
    value: str,
    style: str,
    x: float,
    y: float,
    width: float,
    height: float,
) -> ET.Element:
    cell = ET.SubElement(
        root,
        "mxCell",
        {"id": cell_id, "parent": "1", "style": style, "value": value, "vertex": "1"},
    )
    ET.SubElement(
        cell,
        "mxGeometry",
        {
            "x": f"{x:g}",
            "y": f"{y:g}",
            "width": f"{width:g}",
            "height": f"{height:g}",
            "as": "geometry",
        },
    )
    return cell


def add_edge(
    root: ET.Element,
    cell_id: str,
    source: str,
    target: str,
    style: str,
    value: str = "",
    points: list[tuple[float, float]] | None = None,
    label_x: float | None = None,
    label_y: float | None = None,
) -> ET.Element:
    edge = ET.SubElement(
        root,
        "mxCell",
        {
            "id": cell_id,
            "edge": "1",
            "parent": "1",
            "source": source,
            "target": target,
            "style": style,
            "value": value,
        },
    )
    geometry_attributes = {"relative": "1", "as": "geometry"}
    if label_x is not None:
        geometry_attributes["x"] = f"{label_x:g}"
    if label_y is not None:
        geometry_attributes["y"] = f"{label_y:g}"
    geometry = ET.SubElement(edge, "mxGeometry", geometry_attributes)
    if label_x is not None or label_y is not None:
        ET.SubElement(geometry, "mxPoint", {"x": "0", "y": "0", "as": "offset"})
    if points:
        array = ET.SubElement(geometry, "Array", {"as": "points"})
        for x, y in points:
            ET.SubElement(array, "mxPoint", {"x": f"{x:g}", "y": f"{y:g}"})
    return edge


def make_model() -> tuple[ET.Element, ET.Element]:
    model = ET.Element(
        "mxGraphModel",
        {
            "dx": "1595",
            "dy": "1183",
            "grid": "1",
            "gridSize": "10",
            "guides": "1",
            "tooltips": "1",
            "connect": "1",
            "arrows": "1",
            "fold": "1",
            "page": "1",
            "pageScale": "1",
            "pageWidth": str(PAGE_WIDTH),
            "pageHeight": str(PAGE_HEIGHT),
            "math": "0",
            "shadow": "0",
        },
    )
    root = ET.SubElement(model, "root")
    ET.SubElement(root, "mxCell", {"id": "0"})
    ET.SubElement(root, "mxCell", {"id": "1", "parent": "0"})
    return model, root


def add_title(root: ET.Element, page_key: str, title: str) -> None:
    add_vertex(
        root,
        f"{page_key}-title",
        html_lines([f"مخطط حالات الاستخدام: {title}"]),
        "shape=none;html=1;strokeColor=none;fillColor=none;fontFamily=Arial;fontSize=24;fontStyle=1;fontColor=#0F172A;align=center;verticalAlign=middle;",
        32,
        18,
        1230,
        38,
    )


def add_boundary(root: ET.Element, page_key: str, label: str = "منصة HRManagement") -> None:
    add_vertex(
        root,
        f"{page_key}-boundary",
        html_lines([label], align="right"),
        "rounded=0;whiteSpace=wrap;html=1;fillColor=none;strokeColor=#475569;strokeWidth=2;fontFamily=Arial;fontSize=18;fontStyle=1;fontColor=#0F172A;align=right;verticalAlign=top;spacingRight=12;spacingTop=10;",
        BOUNDARY_X,
        BOUNDARY_Y,
        BOUNDARY_W,
        BOUNDARY_H,
    )


def actor_style() -> str:
    return (
        "shape=umlActor;verticalLabelPosition=bottom;verticalAlign=top;html=1;"
        "fontFamily=Arial;fontSize=18;fontColor=#111827;align=center;spacingTop=4;"
        "strokeColor=#334155;fillColor=none;fontStyle=1;whiteSpace=wrap;"
    )


def usecase_style(action: str) -> str:
    fill, stroke = COLORS[classify_color(action)]
    return (
        "shape=ellipse;perimeter=ellipsePerimeter;whiteSpace=wrap;html=1;"
        f"fillColor={fill};strokeColor={stroke};strokeWidth=1.5;"
        "fontColor=#111827;fontFamily=Arial;fontSize=17;fontStyle=1;"
        "align=center;verticalAlign=middle;spacing=8;"
    )


def association_style(side: str) -> str:
    if side == "left":
        anchors = "exitX=1;exitY=0.5;entryX=0;entryY=0.5;"
    else:
        anchors = "exitX=0;exitY=0.5;entryX=1;entryY=0.5;"
    return (
        "edgeStyle=orthogonalEdgeStyle;rounded=0;orthogonalLoop=1;jettySize=auto;"
        "html=1;endArrow=none;startArrow=none;strokeColor=#64748B;strokeWidth=1;"
        f"curved=0;{anchors}"
    )


def relation_style(kind: str, source_side: str, target_side: str) -> str:
    color = "#2563EB" if kind == "include" else "#C2410C"
    font_color = "#1D4ED8" if kind == "include" else "#9A3412"
    source_anchor = "exitX=1;exitY=0.5;" if source_side == "left" else "exitX=0;exitY=0.5;"
    target_anchor = "entryX=1;entryY=0.5;" if target_side == "left" else "entryX=0;entryY=0.5;"
    return (
        "edgeStyle=orthogonalEdgeStyle;rounded=0;orthogonalLoop=1;jettySize=auto;"
        "html=1;dashed=1;dashPattern=6 6;endArrow=open;endFill=0;curved=0;"
        f"strokeColor={color};strokeWidth=1.5;fontFamily=Arial;fontSize=12;"
        f"fontColor={font_color};labelBackgroundColor=#FFFFFF;{source_anchor}{target_anchor}"
    )


def relation_label_style(kind: str) -> str:
    font_color = "#1D4ED8" if kind == "include" else "#9A3412"
    return (
        "shape=rectangle;rounded=0;whiteSpace=wrap;html=1;strokeColor=none;fillColor=#FFFFFF;"
        f"fontFamily=Arial;fontSize=12;fontStyle=0;fontColor={font_color};"
        "align=center;verticalAlign=middle;spacing=2;"
    )


def page_positions(count: int) -> dict[int, tuple[str, float, float]]:
    left_count = math.ceil(count / 2)
    positions: dict[int, tuple[str, float, float]] = {}
    for index in range(count):
        if index < left_count:
            side = "left"
            row = index
            x = LEFT_UC_X
        else:
            side = "right"
            row = index - left_count
            x = RIGHT_UC_X
        positions[index] = (side, x, ROW_Y[row])
    return positions


def add_case_nodes(
    root: ET.Element,
    page_key: str,
    cases: list[tuple[str, str, str]],
) -> dict[str, dict[str, object]]:
    positions = page_positions(len(cases))
    nodes: dict[str, dict[str, object]] = {}
    for index, (identifier, action, actor) in enumerate(cases):
        side, uc_x, uc_y = positions[index]
        uc_id = f"{page_key}-uc-{identifier.replace('/', '-').replace(' ', '-')}"
        actor_id = f"{page_key}-actor-{index + 1:02d}"
        actor_x = LEFT_ACTOR_X if side == "left" else RIGHT_ACTOR_X
        actor_y = uc_y + (USECASE_H - ACTOR_H) / 2
        add_vertex(
            root,
            actor_id,
            html_lines(wrap_words(actor_label(actor), 18)),
            actor_style(),
            actor_x,
            actor_y,
            ACTOR_W,
            ACTOR_H,
        )
        add_vertex(
            root,
            uc_id,
            usecase_html(identifier, action),
            usecase_style(action),
            uc_x,
            uc_y,
            USECASE_W,
            USECASE_H,
        )
        add_edge(
            root,
            f"{page_key}-association-{index + 1:02d}",
            actor_id,
            uc_id,
            association_style(side),
        )
        nodes[identifier] = {
            "id": uc_id,
            "side": side,
            "x": uc_x,
            "y": uc_y,
            "center_y": uc_y + USECASE_H / 2,
        }
    return nodes


def add_relations(
    root: ET.Element,
    page_key: str,
    nodes: dict[str, dict[str, object]],
    relations: tuple[tuple[str, str, str], ...],
) -> None:
    used_label_y: list[float] = []
    for index, (source_key, target_key, kind) in enumerate(relations):
        if source_key not in nodes or target_key not in nodes:
            raise ValueError(f"Unknown relation on {page_key}: {source_key} -> {target_key}")
        source = nodes[source_key]
        target = nodes[target_key]
        lane_x = 626 + (index % 5) * 14
        source_y = float(source["center_y"])
        target_y = float(target["center_y"])
        points = [(lane_x, source_y), (lane_x, target_y)]
        preferred_label_y = (source_y + target_y) / 2 - 12
        label_y = preferred_label_y
        step = 30.0
        attempt = 0
        while any(abs(label_y - occupied) < 28 for occupied in used_label_y):
            attempt += 1
            direction = 1 if attempt % 2 else -1
            distance = math.ceil(attempt / 2) * step
            label_y = preferred_label_y + direction * distance
        label_y = min(max(label_y, 90), 1065)
        used_label_y.append(label_y)
        add_edge(
            root,
            f"{page_key}-{kind}-{index + 1:02d}",
            str(source["id"]),
            str(target["id"]),
            relation_style(kind, str(source["side"]), str(target["side"])),
            "",
            points,
        )
        add_vertex(
            root,
            f"{page_key}-{kind}-label-{index + 1:02d}",
            "&lt;&lt;include&gt;&gt;" if kind == "include" else "&lt;&lt;extend&gt;&gt;",
            relation_label_style(kind),
            lane_x - 48,
            label_y,
            96,
            24,
        )


def coverage_ranges(ids: list[str]) -> str:
    groups: dict[str, list[str]] = {}
    for identifier in ids:
        prefix = prefix_of(identifier) if re.fullmatch(r"FR-[A-Z]+-\d+", identifier) else ""
        groups.setdefault(prefix, []).append(identifier)
    parts = []
    for values in groups.values():
        if len(values) == 1:
            parts.append(values[0])
        else:
            parts.append(f"من {values[0]} إلى {values[-1]}")
    return "، ".join(parts)


def add_footer(root: ET.Element, page_key: str, ids: list[str], custom_text: str | None = None) -> None:
    note = custom_text or (
        f"التغطية المباشرة: {len(ids)} متطلبات وظيفية ({coverage_ranges(ids)}). "
        "تكرار رمز الممثل مقصود للحفاظ على استقامة العلاقات ومنع تقاطعها."
    )
    add_vertex(
        root,
        f"{page_key}-footer",
        html_lines(wrap_words(note, 110)),
        "shape=none;html=1;strokeColor=none;fillColor=none;fontFamily=Arial;fontSize=13;fontColor=#475569;align=center;verticalAlign=middle;whiteSpace=wrap;",
        230,
        1110,
        840,
        38,
    )


def add_usecase_page(
    mxfile: ET.Element,
    page_number: int,
    page_key: str,
    title: str,
    cases: list[tuple[str, str, str]],
    relations: tuple[tuple[str, str, str], ...] = (),
    footer_text: str | None = None,
) -> None:
    if len(cases) > 10:
        raise ValueError(f"Page {page_key} has {len(cases)} cases; the fixed layout supports 10")
    diagram = ET.SubElement(mxfile, "diagram", {"id": f"diagram-{page_number:02d}", "name": f"{page_number:02d} - {title}"})
    model, root = make_model()
    diagram.append(model)
    add_title(root, page_key, title)
    add_boundary(root, page_key)
    nodes = add_case_nodes(root, page_key, cases)
    add_relations(root, page_key, nodes, relations)
    add_footer(root, page_key, [case[0] for case in cases], footer_text)


def add_guide_page(mxfile: ET.Element, specs: tuple[PageSpec, ...], requirement_count: int) -> None:
    diagram = ET.SubElement(mxfile, "diagram", {"id": "diagram-00", "name": "00 - دليل الحزمة والتغطية"})
    model, root = make_model()
    diagram.append(model)
    add_title(root, "guide", "دليل الحزمة المعتمدة والتغطية")

    legend = (
        ("إنشاء أو تنفيذ", "green"),
        ("تعريف أو حوكمة", "blue"),
        ("قرار أو اعتماد", "orange"),
        ("أمن أو استثناء", "red"),
        ("قراءة أو استعلام", "gray"),
    )
    for index, (label, color_key) in enumerate(legend):
        fill, stroke = COLORS[color_key]
        add_vertex(
            root,
            f"guide-legend-{index + 1}",
            html_lines([label]),
            "shape=ellipse;perimeter=ellipsePerimeter;whiteSpace=wrap;html=1;"
            f"fillColor={fill};strokeColor={stroke};strokeWidth=1.5;fontColor=#111827;"
            "fontFamily=Arial;fontSize=16;fontStyle=1;align=center;verticalAlign=middle;spacing=6;",
            92 + index * 236,
            82,
            190,
            62,
        )

    entries = [(1, "المخطط الشامل", "جميع عائلات المتطلبات")]
    for number, spec in enumerate(specs, 2):
        entries.append((number, spec.title, " / ".join(f"FR-{prefix}" for prefix in spec.prefixes)))
    split = math.ceil(len(entries) / 2)
    for index, (number, title, coverage) in enumerate(entries):
        column = 0 if index < split else 1
        row = index if column == 0 else index - split
        x = 92 if column == 0 else 662
        y = 180 + row * 88
        value = (
            '<div dir="rtl" style="text-align:right;line-height:1.25;">'
            f'<span style="font-size:17px;font-weight:bold;">{number:02d}. {html.escape(title)}</span><br>'
            f'<span dir="ltr" style="unicode-bidi:isolate;display:inline-block;font-size:13px;color:#475569;">{html.escape(coverage)}</span>'
            "</div>"
        )
        add_vertex(
            root,
            f"guide-entry-{number:02d}",
            value,
            "rounded=0;whiteSpace=wrap;html=1;fillColor=#F8FAFC;strokeColor=#64748B;strokeWidth=1.2;"
            "fontFamily=Arial;fontSize=16;fontColor=#111827;align=right;verticalAlign=middle;spacing=10;",
            x,
            y,
            540,
            68,
        )

    summary = (
        f"تغطي الحزمة {requirement_count} متطلباً وظيفياً مرة واحدة في 18 مخططاً قطاعياً، إضافة إلى المخطط الشامل. "
        "المتطلبات غير الوظيفية الثلاثون توثق في مواصفات الأمن والأداء والتشغيل ولا تحول إلى حالات استخدام. "
        "الخط الرمادي الصلب ارتباط، والأزرق المتقطع Include، والبرتقالي المتقطع Extend."
    )
    add_vertex(
        root,
        "guide-summary",
        html_lines(wrap_words(summary, 118)),
        "rounded=0;whiteSpace=wrap;html=1;fillColor=#FFFFFF;strokeColor=#334155;strokeWidth=1.5;"
        "fontFamily=Arial;fontSize=15;fontColor=#0F172A;align=center;verticalAlign=middle;spacing=10;",
        92,
        1080,
        1110,
        94,
    )


def add_master_page(mxfile: ET.Element) -> None:
    cases = [(identifier, action, actor) for identifier, action, actor in MASTER_CASES]
    add_usecase_page(
        mxfile,
        1,
        "master",
        "النطاق الوظيفي الشامل للنظام",
        cases,
        footer_text=(
            "يغطي المخطط جميع عائلات المتطلبات الوظيفية، وتفصلها الصفحات القطاعية التالية. "
            "تكرار رمز الممثل مقصود للحفاظ على استقامة العلاقات ومنع تقاطعها."
        ),
    )


def prefix_of(requirement_id: str) -> str:
    return requirement_id.split("-")[1]


def build_workbook(requirements: list[Requirement]) -> ET.ElementTree:
    mxfile = ET.Element(
        "mxfile",
        {
            "host": "Electron",
            "agent": "Codex",
            "version": "24.7.17",
            "pages": str(len(PAGE_SPECS) + 2),
        },
    )
    add_guide_page(mxfile, PAGE_SPECS, len(requirements))
    add_master_page(mxfile)

    represented: list[str] = []
    for page_number, spec in enumerate(PAGE_SPECS, 2):
        selected = [item for item in requirements if prefix_of(item.id) in spec.prefixes]
        if not selected:
            raise ValueError(f"No requirements selected for page {spec.key}")
        represented.extend(item.id for item in selected)
        cases = [(item.id, item.action, item.actor) for item in selected]
        add_usecase_page(mxfile, page_number, spec.key, spec.title, cases, spec.relations)

    expected = {item.id for item in requirements}
    actual = set(represented)
    if expected != actual or len(represented) != len(actual):
        missing = sorted(expected - actual)
        extra = sorted(actual - expected)
        duplicates = sorted({item for item in represented if represented.count(item) > 1})
        raise ValueError(f"Coverage mismatch: missing={missing}, extra={extra}, duplicates={duplicates}")

    tree = ET.ElementTree(mxfile)
    ET.indent(tree, space="  ")
    return tree


def validate_workbook(tree: ET.ElementTree, requirements: list[Requirement]) -> dict[str, object]:
    root = tree.getroot()
    diagrams = root.findall("diagram")
    usecase_cells = [
        cell
        for cell in root.findall(".//mxCell")
        if "shape=ellipse" in cell.get("style", "") and "-uc-" in cell.get("id", "")
    ]
    sector_ids: list[str] = []
    for cell in usecase_cells:
        value = html.unescape(cell.get("value", ""))
        match = re.search(r"FR-[A-Z]+-\d+", value)
        if match:
            sector_ids.append(match.group(0))
    expected = {item.id for item in requirements}
    actual = set(sector_ids)
    edge_cells = [cell for cell in root.findall(".//mxCell") if cell.get("edge") == "1"]
    curved_edges = [cell.get("id") for cell in edge_cells if "curved=0" not in cell.get("style", "")]
    overflow: list[str] = []
    for cell in root.findall(".//mxCell"):
        if cell.get("vertex") != "1":
            continue
        geometry = cell.find("mxGeometry")
        if geometry is None:
            continue
        x = float(geometry.get("x", "0"))
        y = float(geometry.get("y", "0"))
        w = float(geometry.get("width", "0"))
        h = float(geometry.get("height", "0"))
        if x < 0 or y < 0 or x + w > PAGE_WIDTH or y + h > PAGE_HEIGHT:
            overflow.append(cell.get("id", ""))
    return {
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "source_catalog": str(CATALOG),
        "page_count": len(diagrams),
        "sector_page_count": len(PAGE_SPECS),
        "functional_requirement_count": len(requirements),
        "represented_requirement_count": len(sector_ids),
        "unique_represented_requirement_count": len(actual),
        "missing_requirement_ids": sorted(expected - actual),
        "unexpected_requirement_ids": sorted(actual - expected),
        "duplicate_requirement_ids": sorted({item for item in sector_ids if sector_ids.count(item) > 1}),
        "usecase_cell_count_including_master": len(usecase_cells),
        "edge_count": len(edge_cells),
        "non_orthogonal_edge_ids": curved_edges,
        "out_of_page_vertex_ids": overflow,
        "valid": expected == actual and len(sector_ids) == len(actual) and not curved_edges and not overflow,
    }


def write_renderer(tree: ET.ElementTree) -> None:
    pages = []
    for index, diagram in enumerate(tree.getroot().findall("diagram"), 1):
        model = diagram.find("mxGraphModel")
        if model is None:
            continue
        pages.append(
            {
                "index": index,
                "name": diagram.get("name", f"Page {index}"),
                "filename": f"{index:02d}_usecase_complete.png",
                "model": ET.tostring(model, encoding="unicode", short_empty_elements=True),
            }
        )
    MANIFEST_OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    MANIFEST_OUTPUT.write_text(
        json.dumps([{key: value for key, value in page.items() if key != "model"} for page in pages], ensure_ascii=False, indent=2),
        encoding="utf-8",
    )
    page_json = json.dumps(pages, ensure_ascii=False).replace("</", "<\\/")
    renderer = f"""<!doctype html>
<html lang="ar" dir="ltr">
<head>
  <meta charset="utf-8">
  <title>HRManagement Complete Use Cases</title>
  <script src="mxClient.js"></script>
  <style>html,body{{margin:0;padding:0;background:#fff}}#canvas{{display:block;background:#fff;overflow:hidden}}</style>
</head>
<body>
  <div id="canvas"></div>
  <script>
    const pages = {page_json};
    const pageNumber = Number(new URLSearchParams(location.search).get('page') || '1');
    const item = pages.find(p => p.index === pageNumber) || pages[0];
    function renderPage() {{
      const documentXml = mxUtils.parseXml(item.model);
      const model = new mxGraphModel();
      const codec = new mxCodec(documentXml);
      codec.decode(documentXml.documentElement, model);
      const container = document.getElementById('canvas');
      const graph = new mxGraph(container, model);
      graph.setEnabled(false); graph.setConnectable(false); graph.setPanning(false); graph.setTooltips(false); graph.setHtmlLabels(true);
      graph.view.validate();
      container.style.width = '{PAGE_WIDTH}px'; container.style.height = '{PAGE_HEIGHT}px';
      graph.refresh(); document.title = item.name; document.body.dataset.ready = 'true';
    }}
    mxEvent.disableContextMenu(document.getElementById('canvas'));
    mxEvent.addListener(window, 'load', renderPage);
  </script>
</body>
</html>
"""
    RENDERER_OUTPUT.write_text(renderer, encoding="utf-8")


def write_outputs(tree: ET.ElementTree, output: Path, user_copy: Path | None) -> list[Path]:
    output.parent.mkdir(parents=True, exist_ok=True)
    tree.write(output, encoding="utf-8", xml_declaration=True)
    destinations = [output]
    SOURCE_COPY.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(output, SOURCE_COPY)
    destinations.append(SOURCE_COPY)
    if user_copy is not None:
        user_copy.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(output, user_copy)
        destinations.append(user_copy)
    return destinations


def main() -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser()
    parser.add_argument("--catalog", type=Path, default=CATALOG)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--user-copy", type=Path, default=DEFAULT_USER_COPY)
    parser.add_argument("--no-user-copy", action="store_true")
    args = parser.parse_args()

    requirements = parse_requirements(args.catalog)
    tree = build_workbook(requirements)
    validation = validate_workbook(tree, requirements)
    if not validation["valid"]:
        raise RuntimeError(json.dumps(validation, ensure_ascii=False, indent=2))
    destinations = write_outputs(tree, args.output, None if args.no_user_copy else args.user_copy)
    COVERAGE_OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    COVERAGE_OUTPUT.write_text(json.dumps(validation, ensure_ascii=False, indent=2), encoding="utf-8")
    write_renderer(tree)
    print(
        json.dumps(
            {
                "outputs": [str(path) for path in destinations],
                "coverage": str(COVERAGE_OUTPUT),
                "renderer": str(RENDERER_OUTPUT),
                "manifest": str(MANIFEST_OUTPUT),
                **validation,
            },
            ensure_ascii=False,
            indent=2,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
