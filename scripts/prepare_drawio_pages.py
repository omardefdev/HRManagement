"""Prepare the approved Draw.io DFD workbook for browser rendering.

The supplied workbook contains regular uncompressed Draw.io XML. Some cell
labels are stored as UTF-8 bytes decoded once as Latin-1, so this script keeps
the original file untouched and writes a corrected editable copy plus a small
HTML renderer input for the diagrams.net mxGraph runtime.
"""

from __future__ import annotations

import copy
import html
import json
import re
import shutil
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
INPUT_CANDIDATES = (
    Path(r"C:\Users\omard\OneDrive\Documents\usecase\نهائي\dfd-hr-system.drawio"),
    Path(r"C:\Users\omard\OneDrive\Documents\usecase\dfd-hr-system.drawio"),
    ROOT / "docs" / "thesis_diagrams" / "source" / "dfd-hr-system.drawio",
)
INPUT = next((path for path in INPUT_CANDIDATES if path.exists()), INPUT_CANDIDATES[-1])
SOURCE_DIR = ROOT / "tmp" / "dfd_reference"
ORIGINAL = SOURCE_DIR / "dfd-hr-system_original.drawio"
CORRECTED = SOURCE_DIR / "dfd-hr-system_reference.drawio"
HTML = ROOT / "tmp" / "drawio_dfd_reference_renderer.html"
MANIFEST = ROOT / "tmp" / "drawio_dfd_reference_manifest.json"


def repair_mojibake(value: str) -> str:
    """Repair the common UTF-8-as-Latin-1 corruption used in cell values."""

    marker_count = sum(value.count(marker) for marker in ("Ù", "Ø", "Û", "Ý", "â", "Ã", "Â"))
    if marker_count < 2:
        return value
    try:
        repaired = value.encode("latin-1").decode("utf-8")
    except (UnicodeEncodeError, UnicodeDecodeError):
        return value
    return repaired


def normalize_values(root: ET.Element) -> None:
    for cell in root.findall(".//mxCell"):
        if "value" in cell.attrib:
            cell.set("value", repair_mojibake(cell.get("value", "")))


def xml_text(element: ET.Element) -> str:
    return ET.tostring(element, encoding="unicode", short_empty_elements=True)


def main() -> int:
    if not INPUT.exists():
        raise FileNotFoundError(INPUT)
    SOURCE_DIR.mkdir(parents=True, exist_ok=True)
    HTML.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(INPUT, ORIGINAL)

    tree = ET.parse(INPUT)
    root = tree.getroot()
    normalize_values(root)
    ET.indent(tree, space="  ")
    tree.write(CORRECTED, encoding="utf-8", xml_declaration=True)

    pages: list[dict[str, object]] = []
    for index, diagram in enumerate(root.findall("diagram"), 1):
        model = diagram.find("mxGraphModel")
        if model is None:
            continue
        pages.append(
            {
                "index": index,
                "name": diagram.get("name", f"Page {index}"),
                "filename": f"reference-dfd-{index:02d}.png",
                "model": xml_text(model),
            }
        )

    MANIFEST.write_text(
        json.dumps(
            [{key: value for key, value in page.items() if key != "model"} for page in pages],
            ensure_ascii=False,
            indent=2,
        ),
        encoding="utf-8",
    )

    page_json = json.dumps(pages, ensure_ascii=False).replace("</", "<\\/")
    html_text = f"""<!doctype html>
<html lang="ar" dir="ltr">
<head>
  <meta charset="utf-8">
  <title>HRManagement DFD</title>
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

      const pageWidth = Number(documentXml.documentElement.getAttribute('pageWidth') || 2400);
      const pageHeight = Number(documentXml.documentElement.getAttribute('pageHeight') || 1500);
      const bounds = graph.getGraphBounds();
      const margin = 30;
      const width = Math.max(pageWidth, Math.ceil(bounds.width + margin * 2));
      const height = Math.max(pageHeight, Math.ceil(bounds.height + margin * 2));
      container.style.width = width + 'px';
      container.style.height = height + 'px';
      graph.view.setTranslate(margin - bounds.x, margin - bounds.y);
      graph.refresh();
      document.body.dataset.ready = 'true';
      document.title = page.name;
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
    print(json.dumps({"pages": len(pages), "corrected": str(CORRECTED), "renderer": str(HTML), "manifest": str(MANIFEST)}, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    sys.exit(main())
