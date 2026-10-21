"""Generate the complete academic DFD in the visual style of the old workbook.

The semantic model comes from generate_approved_dfd_drawio.py. This generator
only applies the requested classic theme and spacing, then independently checks
DFD structure, balance, coverage, and native Draw.io editability.
"""

from __future__ import annotations

import json
import re
import shutil
import sys
from datetime import datetime, timezone
from pathlib import Path
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))

import generate_approved_dfd_drawio as authoritative  # noqa: E402


OUTPUT = ROOT / "docs" / "output" / "HRManagement_DFD_Complete_Academic.drawio"
SOURCE_COPY = (
    ROOT
    / "docs"
    / "thesis_diagrams"
    / "source"
    / "HRManagement_DFD_Complete_Academic.drawio"
)
USER_COPY = Path(
    r"C:\Users\omard\OneDrive\Documents\usecase\نهائي\HRManagement_DFD_Complete_Academic.drawio"
)
VALIDATION = ROOT / "docs" / "output" / "HRManagement_DFD_Complete_Academic_validation.json"
MANIFEST = ROOT / "tmp" / "complete_academic_dfd_manifest.json"
RENDERER = ROOT / "tmp" / "complete_academic_dfd_renderer.html"


SCALE = 1.15
CLASSIC_BLACK = "#000000"
CLASSIC_WHITE = "#ffffff"
CLASSIC_NOTE = "#fffbe6"
CLASSIC_NOTE_BORDER = "#666666"


def classic_vertex_style(kind: str) -> str:
    common = (
        "whiteSpace=wrap;html=1;fontFamily=Arial;align=center;"
        "verticalAlign=middle;spacing=8;overflow=visible;"
    )
    if kind == "external":
        return common + (
            f"rounded=0;fillColor={CLASSIC_WHITE};strokeColor={CLASSIC_BLACK};"
            f"strokeWidth=1;fontColor={CLASSIC_BLACK};fontSize=28;fontStyle=1;"
        )
    if kind == "process":
        return common + (
            f"rounded=1;arcSize=20;fillColor={CLASSIC_WHITE};strokeColor={CLASSIC_BLACK};"
            f"strokeWidth=1;fontColor={CLASSIC_BLACK};fontSize=28;fontStyle=1;"
        )
    if kind == "store":
        return common + (
            "shape=partialRectangle;right=0;top=0;bottom=0;"
            f"fillColor={CLASSIC_WHITE};strokeColor={CLASSIC_BLACK};"
            f"strokeWidth=1;fontColor={CLASSIC_BLACK};fontSize=28;fontStyle=1;"
        )
    if kind == "interface":
        return common + (
            f"rounded=1;arcSize=12;fillColor={CLASSIC_WHITE};strokeColor={CLASSIC_BLACK};"
            f"strokeWidth=1;fontColor={CLASSIC_BLACK};fontSize=24;fontStyle=1;"
            "dashed=1;dashPattern=7 4;"
        )
    if kind == "note":
        return common + (
            f"rounded=0;fillColor={CLASSIC_NOTE};strokeColor={CLASSIC_NOTE_BORDER};"
            f"strokeWidth=1;fontColor={CLASSIC_BLACK};fontSize=20;fontStyle=0;"
        )
    if kind == "heading":
        return common + (
            f"rounded=0;fillColor=none;strokeColor=none;fontColor={CLASSIC_BLACK};"
            "fontSize=44;fontStyle=1;"
        )
    if kind == "subheading":
        return common + (
            f"rounded=0;fillColor=none;strokeColor=none;fontColor={CLASSIC_BLACK};"
            "fontSize=24;fontStyle=1;"
        )
    return common + (
        f"rounded=0;fillColor=none;strokeColor=none;fontColor={CLASSIC_BLACK};"
        "fontSize=20;fontStyle=1;"
    )


def classic_edge_style(original_style: str) -> str:
    is_dashed = "dashed=1" in original_style
    style = (
        "edgeStyle=orthogonalEdgeStyle;rounded=0;orthogonalLoop=1;jettySize=auto;"
        "html=1;curved=0;endArrow=classic;endFill=1;startArrow=none;"
        f"strokeColor={CLASSIC_BLACK};strokeWidth=1;fontColor={CLASSIC_BLACK};"
        "fontFamily=Arial;fontSize=24;fontStyle=1;labelBackgroundColor=#FFFFFF;"
        "align=center;verticalAlign=middle;"
    )
    for key in (
        "exitX",
        "exitY",
        "exitDx",
        "exitDy",
        "exitPerimeter",
        "entryX",
        "entryY",
        "entryDx",
        "entryDy",
        "entryPerimeter",
    ):
        match = re.search(rf"(?:^|;){key}=([^;]+)", original_style)
        if match:
            style += f"{key}={match.group(1)};"
    if is_dashed:
        style += "dashed=1;dashPattern=7 4;"
    return style


def scale_number(value: str | None) -> str | None:
    if value is None:
        return None
    try:
        return f"{float(value) * SCALE:g}"
    except ValueError:
        return value


def apply_classic_theme(tree: ET.ElementTree) -> None:
    mxfile = tree.getroot()
    mxfile.set("host", "Electron")
    mxfile.set("agent", "Codex academic DFD classic theme")
    mxfile.set("modified", datetime.now(timezone.utc).isoformat(timespec="seconds"))
    mxfile.set("pages", str(len(mxfile.findall("diagram"))))

    for diagram in mxfile.findall("diagram"):
        model = diagram.find("mxGraphModel")
        if model is None:
            continue
        for attribute in ("pageWidth", "pageHeight", "dx", "dy"):
            scaled = scale_number(model.get(attribute))
            if scaled is not None:
                model.set(attribute, scaled)
        model.set("background", CLASSIC_WHITE)

        for cell in model.findall("root/mxCell"):
            kind = cell.get("dfdKind", "")
            if cell.get("vertex") == "1":
                cell.set("style", classic_vertex_style(kind))
                geometry = cell.find("mxGeometry")
                if geometry is not None:
                    for attribute in ("x", "y", "width", "height"):
                        scaled = scale_number(geometry.get(attribute))
                        if scaled is not None:
                            geometry.set(attribute, scaled)
            elif cell.get("edge") == "1":
                cell.set("style", classic_edge_style(cell.get("style", "")))
                geometry = cell.find("mxGeometry")
                if geometry is not None:
                    for point in geometry.findall("Array/mxPoint"):
                        for attribute in ("x", "y"):
                            scaled = scale_number(point.get(attribute))
                            if scaled is not None:
                                point.set(attribute, scaled)


def plain_label(value: str) -> str:
    return re.sub(r"\s+", " ", re.sub(r"<[^>]+>", " ", value or "")).strip()


def validate_classic(tree: ET.ElementTree) -> dict[str, object]:
    semantic = authoritative.validate_workbook(tree)
    errors = list(semantic["errors"])
    diagrams = tree.getroot().findall("diagram")
    total_processes = 0
    total_stores = 0
    total_externals = 0
    total_flows = 0
    non_orthogonal: list[str] = []
    unnamed: list[str] = []
    invalid_pairs: list[str] = []
    process_io_errors: list[str] = []

    for diagram in diagrams:
        name = diagram.get("name", "")
        cells = diagram.findall("mxGraphModel/root/mxCell")
        by_id = {cell.get("id", ""): cell for cell in cells}
        processes = [cell for cell in cells if cell.get("dfdKind") == "process"]
        stores = [cell for cell in cells if cell.get("dfdKind") == "store"]
        externals = [cell for cell in cells if cell.get("dfdKind") == "external"]
        flows = [cell for cell in cells if cell.get("edge") == "1"]
        total_processes += len(processes)
        total_stores += len(stores)
        total_externals += len(externals)
        total_flows += len(flows)

        incoming: dict[str, int] = {}
        outgoing: dict[str, int] = {}
        for edge in flows:
            edge_id = edge.get("id", "")
            style = edge.get("style", "")
            if "edgeStyle=orthogonalEdgeStyle" not in style or "curved=0" not in style:
                non_orthogonal.append(edge_id)
            if not edge.get("value", "").strip():
                unnamed.append(edge_id)
            source = edge.get("source", "")
            target = edge.get("target", "")
            outgoing[source] = outgoing.get(source, 0) + 1
            incoming[target] = incoming.get(target, 0) + 1
            source_kind = by_id.get(source, ET.Element("x")).get("dfdKind", "")
            target_kind = by_id.get(target, ET.Element("x")).get("dfdKind", "")
            if {source_kind, target_kind} == {"external", "store"}:
                invalid_pairs.append(f"{name}: {edge_id} external-store")
            if source_kind == "store" and target_kind == "store":
                invalid_pairs.append(f"{name}: {edge_id} store-store")

        # The final dictionary page reuses process-shaped cells as a visual key;
        # it intentionally contains no runtime flows and is excluded from I/O checks.
        if flows:
            for process in processes:
                process_id = process.get("id", "")
                if incoming.get(process_id, 0) == 0 or outgoing.get(process_id, 0) == 0:
                    process_io_errors.append(
                        f"{name}: {plain_label(process.get('value', ''))} "
                        f"in={incoming.get(process_id, 0)} out={outgoing.get(process_id, 0)}"
                    )

    if non_orthogonal:
        errors.append("Non-orthogonal flows: " + ", ".join(non_orthogonal))
    if unnamed:
        errors.append("Unnamed flows: " + ", ".join(unnamed))
    if invalid_pairs:
        errors.extend(invalid_pairs)
    if process_io_errors:
        errors.extend(process_io_errors)

    return {
        "valid": not errors,
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "source_model": str(ROOT / "scripts" / "generate_approved_dfd_drawio.py"),
        "page_count": len(diagrams),
        "process_count": total_processes,
        "store_count": total_stores,
        "external_entity_count": total_externals,
        "flow_count": total_flows,
        "required_scope_terms": semantic["required_scope_terms"],
        "missing_scope_terms": semantic["missing_scope_terms"],
        "non_orthogonal_flow_ids": non_orthogonal,
        "unnamed_flow_ids": unnamed,
        "invalid_flow_pairs": invalid_pairs,
        "process_input_output_errors": process_io_errors,
        "errors": errors,
    }


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
                "filename": f"academic-dfd-{index:02d}.png",
                "model": ET.tostring(model, encoding="unicode", short_empty_elements=True),
            }
        )
    MANIFEST.parent.mkdir(parents=True, exist_ok=True)
    MANIFEST.write_text(
        json.dumps(
            [{key: value for key, value in page.items() if key != "model"} for page in pages],
            ensure_ascii=False,
            indent=2,
        ),
        encoding="utf-8",
    )
    page_json = json.dumps(pages, ensure_ascii=False).replace("</", "<\\/")
    RENDERER.write_text(
        f"""<!doctype html>
<html lang="ar" dir="ltr">
<head>
  <meta charset="utf-8">
  <title>HRManagement Complete Academic DFD</title>
  <script src="mxClient.js"></script>
  <style>html,body{{margin:0;padding:0;background:#fff}}#canvas{{display:block;background:#fff;overflow:hidden}}</style>
</head>
<body>
  <div id="canvas"></div>
  <script>
    const pages={page_json};
    const pageNumber=Number(new URLSearchParams(location.search).get('page')||'1');
    const item=pages.find(p=>p.index===pageNumber)||pages[0];
    function renderPage(){{
      const documentXml=mxUtils.parseXml(item.model);
      const model=new mxGraphModel();
      const codec=new mxCodec(documentXml);codec.decode(documentXml.documentElement,model);
      const container=document.getElementById('canvas');const graph=new mxGraph(container,model);
      graph.setEnabled(false);graph.setConnectable(false);graph.setPanning(false);graph.setTooltips(false);graph.setHtmlLabels(true);
      graph.view.validate();const root=documentXml.documentElement;
      container.style.width=Number(root.getAttribute('pageWidth'))+'px';container.style.height=Number(root.getAttribute('pageHeight'))+'px';
      graph.refresh();document.title=item.name;document.body.dataset.ready='true';
    }}
    mxEvent.disableContextMenu(document.getElementById('canvas'));mxEvent.addListener(window,'load',renderPage);
  </script>
</body>
</html>""",
        encoding="utf-8",
    )


def write_outputs(tree: ET.ElementTree) -> list[Path]:
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    tree.write(OUTPUT, encoding="utf-8", xml_declaration=True, short_empty_elements=True)
    SOURCE_COPY.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(OUTPUT, SOURCE_COPY)
    USER_COPY.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(OUTPUT, USER_COPY)
    return [OUTPUT, SOURCE_COPY, USER_COPY]


def main() -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    tree = authoritative.build_workbook()
    apply_classic_theme(tree)
    report = validate_classic(tree)
    VALIDATION.parent.mkdir(parents=True, exist_ok=True)
    VALIDATION.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    if not report["valid"]:
        raise RuntimeError("DFD validation failed:\n" + "\n".join(report["errors"]))
    outputs = write_outputs(tree)
    write_renderer(tree)
    print(
        json.dumps(
            {
                "outputs": [str(path) for path in outputs],
                "validation": str(VALIDATION),
                "renderer": str(RENDERER),
                "manifest": str(MANIFEST),
                **report,
            },
            ensure_ascii=False,
            indent=2,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
