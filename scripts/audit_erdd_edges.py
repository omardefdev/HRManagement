"""Audit ERD page edges vs snapshot HasOne counts."""
from __future__ import annotations

import re
from pathlib import Path

from database_schema_parser import load_all_database_models

ROOT = Path(__file__).resolve().parents[1]
XML = ROOT / "docs" / "thesis_diagrams" / "source" / "database-erdds.drawio"


def main() -> None:
    xml = XML.read_text(encoding="utf-8")
    models = load_all_database_models()
    for model in models:
        marker = f'id="erd-{model.index:02d}"'
        chunk = xml.split(marker, 1)[1].split("</diagram>", 1)[0] if marker in xml else ""
        er_edges = len(re.findall(r"startArrow=ERone", chunk))
        dashed = len(re.findall(r"dashed=1", chunk))
        missing = len(model.relations) - er_edges
        flag = " OK" if missing == 0 else f" MISSING {missing}"
        print(f"{model.database}: HasOne={len(model.relations)} ER={er_edges}{flag}")


if __name__ == "__main__":
    main()
