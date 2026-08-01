"""Prepare, summarize, and expose the approved use-case package for rendering."""

from __future__ import annotations

import html
import json
import re
import shutil
import sys
import xml.etree.ElementTree as ET
from collections import Counter, defaultdict
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
DRAWIO_INPUT = Path(r"C:\Users\omard\OneDrive\Documents\usecase\usecase-diagrams.drawio")
MARKDOWN_INPUT = Path(r"C:\Users\omard\OneDrive\Documents\usecase\usecases-corrected.md")
SOURCE_DIR = ROOT / "docs" / "thesis_diagrams" / "source"
ORIGINAL_DRAWIO = SOURCE_DIR / "usecase-diagrams_original.drawio"
EDITABLE_DRAWIO = SOURCE_DIR / "usecase-diagrams.drawio"
MARKDOWN_COPY = ROOT / "docs" / "USE_CASES_CORRECTED.md"
ANALYSIS_JSON = ROOT / "docs" / "USE_CASES_ANALYSIS.json"
ANALYSIS_MD = ROOT / "docs" / "USE_CASES_ANALYSIS.md"
MANIFEST = ROOT / "docs" / "thesis_diagrams" / "usecase_drawio_manifest.json"
HTML = ROOT / "tmp" / "drawio_usecase_renderer.html"


BRANCH_MAPPING = {
    "فرع الخدمة والعقود": ("HR.Core / HR.Contracts / Payroll / Records.Archive", "FR-HIRE, FR-CON, FR-LEAVE"),
    "فرع الشؤون الشخصية والصحية": ("HR.Core / HR.Health", "FR-EMP, FR-HEA"),
    "فرع التنقلات والتوزيع": ("HR.Movements / HR.Core / Payroll", "FR-MOV"),
    "فرع تخطيط الدورات وتأهيل القوى البشرية": ("HR.Training", "FR-TRN"),
    "فرع السيرة الوظيفية": ("HR.Core", "FR-EMP, FR-ORG"),
    "فرع الرواتب والتعويضات": ("Payroll", "FR-PAY"),
    "فرع أرشفة الملفات والوثائق": ("Records.Archive / File.Storage", "FR-ARC"),
    "فرع بطاقات التعريف والهويات": ("IDCard", "FR-CARD"),
    "فرع الرقابة": ("Compliance", "FR-COMP"),
    "فرع التنظيم والتجهيز": ("Admin.Logistics", "FR-OPS"),
    "فرع التخطيط والإحصاء": ("Dev.Analytics", "FR-PAY-05 / FR-TRN-04"),
    "الفرع الإداري": ("Admin.Logistics", "FR-OPS"),
    "فرع الشؤون القانونية والانضباط": ("Compliance / HR.Contracts", "FR-COMP, FR-CON-03"),
    "فرع الديوان العام": ("Records.Archive / File.Storage", "FR-ARC"),
    "فرع البعثات": ("HR.Training", "FR-TRN"),
    "فرع تقنية المعلومات": ("IT.Infra / Auth.Directory", "FR-OPS, NFR-SEC"),
    "فرع التطوير البرمجي وتحليل المعلومات": ("Dev.Analytics / IT.Infra", "FR-OPS, NFR-MAINT"),
}


def repair_mojibake(value: str) -> str:
    marker_count = sum(value.count(marker) for marker in ("Ù", "Ø", "Û", "Ý", "â", "Ã", "Â"))
    if marker_count < 2:
        return value
    try:
        return value.encode("latin-1").decode("utf-8")
    except (UnicodeEncodeError, UnicodeDecodeError):
        return value


def normalize_values(root: ET.Element) -> None:
    for cell in root.findall(".//mxCell"):
        if "value" in cell.attrib:
            cell.set("value", repair_mojibake(cell.get("value", "")))


def xml_text(element: ET.Element) -> str:
    return ET.tostring(element, encoding="unicode", short_empty_elements=True)


def parse_cases() -> list[dict[str, object]]:
    lines = MARKDOWN_INPUT.read_text(encoding="utf-8-sig").splitlines()
    department = ""
    branch = ""
    current: dict[str, object] | None = None
    cases: list[dict[str, object]] = []

    def finish() -> None:
        nonlocal current
        if current is not None:
            cases.append(current)
            current = None

    for line in lines:
        if line.startswith("## "):
            finish()
            department = line[3:].strip()
            continue
        if line.startswith("### "):
            finish()
            branch = line[4:].strip()
            continue
        case_match = re.match(r"^####\s+(\d+)\.\s+(.+)$", line)
        if case_match:
            finish()
            current = {
                "department": department,
                "branch": branch,
                "number": int(case_match.group(1)),
                "title": case_match.group(2).strip(),
                "fields": {},
                "include": [],
                "extend": [],
            }
            continue
        if current is None:
            continue
        table_match = re.match(r"^\|\s*([^|]+?)\s*\|\s*([^|]+?)\s*\|$", line)
        if table_match and table_match.group(1).strip() not in {"الحقل", "---"}:
            current["fields"][table_match.group(1).strip()] = table_match.group(2).strip()
            continue
        relation_match = re.match(r"^\s*-\s*(Include|Extend):\s*(.+)$", line)
        if relation_match:
            current[relation_match.group(1).lower()].append(relation_match.group(2).strip())
    finish()
    return cases


def build_analysis(cases: list[dict[str, object]], pages: list[dict[str, object]]) -> dict[str, object]:
    grouped: dict[tuple[str, str], list[dict[str, object]]] = defaultdict(list)
    for case in cases:
        grouped[(str(case["department"]), str(case["branch"]))].append(case)
    groups = []
    for page in pages:
        page_name = str(page["name"])
        match = re.match(r"^\d+\s*-\s*(.+?)\s*-\s*(.+)$", page_name)
        department = match.group(1).strip() if match else ""
        branch = match.group(2).strip() if match else page_name
        records = grouped.get((department, branch), [])
        actors = Counter(str(case["fields"].get("المنفذ", "غير محدد")) for case in records)
        types = Counter(str(case["fields"].get("نوع الحالة", "غير محدد")) for case in records)
        service, requirements = BRANCH_MAPPING.get(branch, ("خدمات متعددة", "يحدد بالتتبع التفصيلي"))
        groups.append({
            "index": page["index"],
            "diagram": page_name,
            "department": department,
            "branch": branch,
            "case_count": len(records),
            "actors": [name for name, _ in actors.most_common()],
            "type_counts": dict(types),
            "include_count": sum(len(case["include"]) for case in records),
            "extend_count": sum(len(case["extend"]) for case in records),
            "services": service,
            "requirements": requirements,
        })
    return {
        "source_drawio": str(DRAWIO_INPUT),
        "source_markdown": str(MARKDOWN_INPUT),
        "diagram_count": len(pages),
        "case_count": len(cases),
        "department_count": len({case["department"] for case in cases}),
        "groups": groups,
        "cases": cases,
    }


def write_analysis_markdown(analysis: dict[str, object]) -> None:
    lines = [
        "# تحليل حزمة حالات الاستخدام المعتمدة",
        "",
        "هذا التحليل مولد من `usecase-diagrams.drawio` و`usecases-corrected.md`. وجود الحالة في الحزمة يثبت نطاق التحليل، ولا يعني وحده أنها منفذة أو مختبرة في الإصدار v1.",
        "",
        f"- عدد المخططات القطاعية: {analysis['diagram_count']}",
        f"- عدد حالات الاستخدام الموثقة: {analysis['case_count']}",
        f"- عدد الإدارات العليا: {analysis['department_count']}",
        "",
        "| المخطط | الإدارة | الفرع | الحالات | Include | Extend | الخدمات | عائلات المتطلبات |",
        "|---:|---|---|---:|---:|---:|---|---|",
    ]
    for item in analysis["groups"]:
        lines.append(
            f"| {item['index']} | {item['department']} | {item['branch']} | {item['case_count']} | {item['include_count']} | {item['extend_count']} | {item['services']} | {item['requirements']} |"
        )
    ANALYSIS_MD.write_text("\n".join(lines) + "\n", encoding="utf-8")


def write_renderer(pages: list[dict[str, object]]) -> None:
    page_json = json.dumps(pages, ensure_ascii=False).replace("</", "<\\/")
    html_text = f"""<!doctype html>
<html lang="ar" dir="ltr">
<head>
  <meta charset="utf-8">
  <title>HRManagement Use Cases</title>
  <script src="mxClient.js"></script>
  <style>
    html, body {{ margin: 0; padding: 0; background: #ffffff; }}
    #canvas {{ display: block; background: #ffffff; overflow: hidden; }}
  </style>
</head>
<body>
  <div id="canvas"></div>
  <script>
    const pages = {page_json};
    const pageNumber = Number(new URLSearchParams(location.search).get('page') || '1');
    const page = pages.find(item => item.index === pageNumber) || pages[0];
    function renderPage() {{
      const documentXml = mxUtils.parseXml(page.model);
      const model = new mxGraphModel();
      const codec = new mxCodec(documentXml);
      codec.decode(documentXml.documentElement, model);
      const container = document.getElementById('canvas');
      const graph = new mxGraph(container, model);
      graph.setEnabled(false);
      graph.setConnectable(false);
      graph.setPanning(false);
      graph.setTooltips(false);
      graph.setHtmlLabels(true);
      graph.view.validate();
      const pageWidth = Number(documentXml.documentElement.getAttribute('pageWidth') || 1294);
      const pageHeight = Number(documentXml.documentElement.getAttribute('pageHeight') || 1228);
      const bounds = graph.getGraphBounds();
      const margin = 24;
      const width = Math.max(pageWidth, Math.ceil(bounds.width + margin * 2));
      const height = Math.max(pageHeight, Math.ceil(bounds.height + margin * 2));
      container.style.width = width + 'px';
      container.style.height = height + 'px';
      graph.view.setTranslate(margin - bounds.x, margin - bounds.y);
      graph.refresh();
      document.title = page.name;
      document.body.dataset.ready = 'true';
    }}
    if (typeof mxClient !== 'undefined') {{
      mxEvent.disableContextMenu(document.getElementById('canvas'));
      mxEvent.addListener(window, 'load', renderPage);
    }}
  </script>
</body>
</html>
"""
    HTML.write_text(html_text, encoding="utf-8")


def main() -> int:
    if not DRAWIO_INPUT.exists():
        raise FileNotFoundError(DRAWIO_INPUT)
    if not MARKDOWN_INPUT.exists():
        raise FileNotFoundError(MARKDOWN_INPUT)
    SOURCE_DIR.mkdir(parents=True, exist_ok=True)
    HTML.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(DRAWIO_INPUT, ORIGINAL_DRAWIO)
    shutil.copy2(MARKDOWN_INPUT, MARKDOWN_COPY)

    tree = ET.parse(DRAWIO_INPUT)
    root = tree.getroot()
    normalize_values(root)
    ET.indent(tree, space="  ")
    tree.write(EDITABLE_DRAWIO, encoding="utf-8", xml_declaration=True)

    pages = []
    for index, diagram in enumerate(root.findall("diagram"), 1):
        model = diagram.find("mxGraphModel")
        if model is None:
            continue
        pages.append({
            "index": index,
            "name": diagram.get("name", f"Page {index}"),
            "filename": f"{index:02d}_usecase.png",
            "model": xml_text(model),
        })
    manifest = [{key: value for key, value in page.items() if key != "model"} for page in pages]
    MANIFEST.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    cases = parse_cases()
    analysis = build_analysis(cases, pages)
    ANALYSIS_JSON.write_text(json.dumps(analysis, ensure_ascii=False, indent=2), encoding="utf-8")
    write_analysis_markdown(analysis)
    write_renderer(pages)
    print(json.dumps({"pages": len(pages), "cases": len(cases), "renderer": str(HTML)}, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    sys.exit(main())
