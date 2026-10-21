"""Generate multi-page Draw.io ERD workbook from EF Core source evidence."""

from __future__ import annotations

import json
import math
import re
from collections import defaultdict
from html import escape
from pathlib import Path

from erd_arabic_labels import bilingual_html, translate_column, translate_table
from database_schema_parser import (
    EXTERNAL_REF_TARGETS,
    EXTERNAL_REF_SKIP,
    DatabaseContextModel,
    EntityModel,
    RelationModel,
    external_ref_usage,
    infer_internal_logical_relations,
    load_all_database_models,
)


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "docs" / "thesis_diagrams" / "source" / "database-erdds.drawio"
OUTPUT_UNIFIED = ROOT / "docs" / "thesis_diagrams" / "source" / "database-erdds-unified.drawio"
VALIDATION = ROOT / "tmp" / "database_erdd_drawio_validation.json"

TEAL = "#0F6B73"
TEAL_DARK = "#0B545A"
GRAY_INFRA = "#E8ECED"
GRAY_INFRA_BORDER = "#9AA5AD"
REF_FILL = "#FFF0E6"
REF_BORDER = "#C9784A"
PK_FILL = "#FFF8DB"
FK_FILL = "#E7F0FF"
ROW_ALT = "#F7FBFC"
REGION_FILL = "#F0F9FA"
REGION_STROKE = "#0F6B73"

TABLE_W = 340
COL_KEY = 52
COL_NAME = 158
COL_TYPE = 130
ROW_H = 40
HEADER_H = 56
GAP_X = 36
GAP_Y = 48
MARGIN = 60
NOTE_W = 320

# Unified landscape layout — databases in a square grid (5×3), tables inside each cell
UNIFIED_PAGE_WIDTH = 9000
UNIFIED_GRID_COLS = 5
UNIFIED_GRID_GAP_X = 100
UNIFIED_GRID_GAP_Y = 120
UNIFIED_TABLE_W = 300
UNIFIED_COL_KEY = 44
UNIFIED_COL_NAME = 140
UNIFIED_COL_TYPE = 116
UNIFIED_ROW_H = 32
UNIFIED_HEADER_H = 46
UNIFIED_GAP_X = 80
UNIFIED_GAP_Y = 80
UNIFIED_REGION_PAD = 56
UNIFIED_REGION_HEADER = 56
UNIFIED_ROW_GAP = 180


def unified_cell_width(page_w: float) -> float:
    inner = page_w - 2 * MARGIN
    gaps = (UNIFIED_GRID_COLS - 1) * UNIFIED_GRID_GAP_X
    return (inner - gaps) / UNIFIED_GRID_COLS


def layout_unified_grid(
    models: list[DatabaseContextModel],
    page_w: float,
) -> list[tuple[DatabaseContextModel, float, float, float, float]]:
    """Place 15 database regions in a UNIFIED_GRID_COLS-wide grid (e.g. 5×3)."""
    cell_w = unified_cell_width(page_w)
    cell_inner = cell_w - 2  # small inset so frames do not touch
    dims = [estimate_region_size(model, cell_inner) for model in models]

    placements: list[tuple[DatabaseContextModel, float, float, float, float]] = []
    y_cursor = 96.0
    for grid_row in range((len(models) + UNIFIED_GRID_COLS - 1) // UNIFIED_GRID_COLS):
        start = grid_row * UNIFIED_GRID_COLS
        batch = models[start : start + UNIFIED_GRID_COLS]
        batch_dims = dims[start : start + UNIFIED_GRID_COLS]
        row_h = max((h for _, h in batch_dims), default=0.0)

        for col_idx, (model, (_cw, _ch)) in enumerate(zip(batch, batch_dims)):
            x = MARGIN + col_idx * (cell_w + UNIFIED_GRID_GAP_X)
            placements.append((model, x, y_cursor, cell_w, row_h))

        y_cursor += row_h + UNIFIED_GRID_GAP_Y

    return placements
UNIFIED_MAX_ID_ROWS = 5


class DrawioBuilder:
    def __init__(self, page_id: str, page_name: str, page_width: int = 2400, page_height: int = 1600) -> None:
        self.page_id = page_id
        self.page_name = page_name
        self.page_width = page_width
        self.page_height = page_height
        self._counter = 0
        self.cells: list[str] = []
        self.table_meta: dict[str, dict] = {}

    def _nid(self, prefix: str) -> str:
        self._counter += 1
        return f"{self.page_id}-{prefix}-{self._counter}"

    def add_text(
        self,
        value: str,
        x: float,
        y: float,
        w: float,
        h: float,
        *,
        size: int = 13,
        bold: bool = False,
        color: str = "#4A5568",
        align: str = "center",
    ) -> str:
        cid = self._nid("txt")
        style = (
            "text;html=1;strokeColor=none;fillColor=none;align="
            f"{align};verticalAlign=middle;whiteSpace=wrap;fontSize={size};"
            f"fontColor={color};fontFamily=Segoe UI;"
        )
        if bold:
            style += "fontStyle=1;"
        self.cells.append(
            f'<mxCell id="{cid}" parent="1" vertex="1" value="{escape(value)}" '
            f'style="{style}">'
            f'<mxGeometry x="{x}" y="{y}" width="{w}" height="{h}" as="geometry" />'
            f"</mxCell>"
        )
        return cid

    def add_box(
        self,
        value: str,
        x: float,
        y: float,
        w: float,
        h: float,
        *,
        fill: str = "#FFFFFF",
        stroke: str = TEAL,
        font_color: str = TEAL,
        rounded: bool = True,
        dashed: bool = False,
        align: str = "center",
        valign: str = "middle",
    ) -> str:
        cid = self._nid("box")
        dash = "dashed=1;" if dashed else ""
        arc = "arcSize=8;" if rounded else ""
        style = (
            f"rounded={1 if rounded else 0};whiteSpace=wrap;html=1;fillColor={fill};"
            f"strokeColor={stroke};fontColor={font_color};fontSize=11;fontFamily=Segoe UI;"
            f"align={align};verticalAlign={valign};spacing=6;{arc}{dash}"
        )
        self.cells.append(
            f'<mxCell id="{cid}" parent="1" vertex="1" value="{escape(value)}" '
            f'style="{style}">'
            f'<mxGeometry x="{x}" y="{y}" width="{w}" height="{h}" as="geometry" />'
            f"</mxCell>"
        )
        return cid

    def add_table(
        self,
        entity: EntityModel,
        x: float,
        y: float,
        *,
        infra: bool = False,
        unified: bool = False,
        parent: str = "1",
    ) -> str:
        if unified:
            rows = unified_display_rows(entity)
            table_w = UNIFIED_TABLE_W
            row_h = UNIFIED_ROW_H
            header_h = UNIFIED_HEADER_H
            col_key, col_name, col_type = UNIFIED_COL_KEY, UNIFIED_COL_NAME, UNIFIED_COL_TYPE
            title_en = entity.table
            title_ar = translate_table(entity.class_name, entity.table)
            title = bilingual_html(title_en, title_ar, english_size=11, arabic_color="#E8F3F4" if not infra else "#6B7280")
        else:
            rows = entity.display_columns()
            table_w = TABLE_W
            row_h = ROW_H
            header_h = HEADER_H
            col_key, col_name, col_type = COL_KEY, COL_NAME, COL_TYPE
            title_en = f"{entity.table}  ·  {entity.class_name}"
            title_ar = translate_table(entity.class_name, entity.table)
            title = bilingual_html(title_en, title_ar, english_size=12, arabic_color="#E8F3F4" if not infra else "#6B7280")

        table_id = self._nid("tbl")
        header_fill = GRAY_INFRA if infra else TEAL
        header_stroke = GRAY_INFRA_BORDER if infra else TEAL_DARK
        header_text = "#4A5568" if infra else "#FFFFFF"
        table_h = header_h + row_h * (1 + len(rows))
        self.cells.append(
            f'<mxCell id="{table_id}" parent="{parent}" vertex="1" value="{escape(title)}" '
            f'style="shape=table;startSize={header_h};container=1;collapsible=0;childLayout=tableLayout;'
            f"fixedRows=1;rowLines=1;columnLines=1;fontStyle=1;align=center;resizeLast=1;html=1;"
            f"fillColor={header_fill};strokeColor={header_stroke};fontColor={header_text};"
            f'fontSize={12 if not unified else 11};fontFamily=Segoe UI;">'
            f'<mxGeometry x="{x}" y="{y}" width="{table_w}" height="{table_h}" as="geometry" />'
            f"</mxCell>"
        )
        row_ids: dict[str, str] = {}
        hdr = self._nid("row")
        self.cells.append(
            f'<mxCell id="{hdr}" parent="{table_id}" vertex="1" '
            f'style="shape=tableRow;horizontal=0;startSize=0;swimlaneHead=0;swimlaneBody=0;'
            f'fillColor=#E8F3F4;collapsible=0;dropTarget=0;portConstraint=eastwest;">'
            f'<mxGeometry y="{header_h}" width="{table_w}" height="{row_h}" as="geometry" />'
            f"</mxCell>"
        )
        for label, width, offset in (("مفتاح", col_key, 0), ("العمود", col_name, col_key), ("النوع", col_type, col_key + col_name)):
            cell = self._nid("cell")
            self.cells.append(
                f'<mxCell id="{cell}" parent="{hdr}" vertex="1" value="{label}" '
                f'style="shape=partialRectangle;connectable=0;fillColor=none;fontStyle=1;'
                f'overflow=hidden;whiteSpace=wrap;html=1;align={"center" if offset == 0 else "left"};'
                f'spacingLeft=4;fontSize=9;fontColor=#4A5568;fontFamily=Segoe UI;">'
                f'<mxGeometry x="{offset}" width="{width}" height="{row_h}" as="geometry">'
                f'<mxRectangle width="{width}" height="{row_h}" as="alternateBounds" />'
                f"</mxGeometry></mxCell>"
            )
        y_off = header_h + row_h
        for idx, (marker, name, typ) in enumerate(rows):
            row_id = self._nid("row")
            if name and not name.startswith("…"):
                row_ids[name] = row_id
            if marker == "PK":
                fill = PK_FILL
            elif marker == "FK":
                fill = FK_FILL
            elif marker == "REF":
                fill = REF_FILL
            elif name.startswith("…"):
                fill = "#F3F4F6"
            else:
                fill = ROW_ALT if idx % 2 else "#FFFFFF"
            self.cells.append(
                f'<mxCell id="{row_id}" parent="{table_id}" vertex="1" '
                f'style="shape=tableRow;horizontal=0;startSize=0;swimlaneHead=0;swimlaneBody=0;'
                f"fillColor={fill};collapsible=0;dropTarget=0;portConstraint=eastwest;"
                f'points=[[0,0.5],[1,0.5]];">'
                f'<mxGeometry y="{y_off}" width="{table_w}" height="{row_h}" as="geometry" />'
                f"</mxCell>"
            )
            key_color = {"PK": "#8A6D00", "FK": "#1D4E89", "REF": "#A34F1A"}.get(marker, "")
            key_style = f"fontStyle=1;fontColor={key_color};" if key_color else ""
            font_size = 10 if unified else 11
            for col_idx, (val, width, offset) in enumerate(
                (
                    (marker, col_key, 0),
                    (name, col_name, col_key),
                    (typ, col_type, col_key + col_name),
                )
            ):
                cell = self._nid("cell")
                align = "center" if col_idx == 0 else "left"
                if col_idx == 1 and val and not val.startswith("…"):
                    display_val = bilingual_html(val, translate_column(val), english_size=9 if unified else 10)
                else:
                    display_val = escape(val)
                self.cells.append(
                    f'<mxCell id="{cell}" parent="{row_id}" vertex="1" value="{escape(display_val)}" '
                    f'style="shape=partialRectangle;connectable=0;fillColor=none;overflow=hidden;'
                    f"whiteSpace=wrap;html=1;align={align};spacingLeft=4;fontSize={font_size};"
                    f'fontFamily={"Consolas" if col_idx else "Segoe UI"};{key_style}">'
                    f'<mxGeometry x="{offset}" width="{width}" height="{row_h}" as="geometry">'
                    f'<mxRectangle width="{width}" height="{row_h}" as="alternateBounds" />'
                    f"</mxGeometry></mxCell>"
                )
            y_off += row_h
        self.table_meta[table_id] = {
            "fqn": entity.fqn,
            "table": entity.table,
            "x": x,
            "y": y,
            "row_ids": row_ids,
            "keys": set(entity.keys or {"Id"}),
            "parent": parent,
        }
        return table_id

    def add_relation(
        self,
        source_table: str,
        target_table: str,
        fk_field: str,
        label: str,
    ) -> None:
        src_meta = self.table_meta.get(source_table)
        tgt_meta = self.table_meta.get(target_table)
        if not src_meta or not tgt_meta:
            return
        src_row = src_meta["row_ids"].get(fk_field) or next(
            (rid for name, rid in src_meta["row_ids"].items() if name in src_meta["keys"]),
            None,
        )
        tgt_row = next(
            (rid for name, rid in tgt_meta["row_ids"].items() if name in tgt_meta["keys"]),
            None,
        )
        if not src_row or not tgt_row:
            return
        edge_id = self._nid("edge")
        self.cells.append(
            f'<mxCell id="{edge_id}" parent="1" source="{src_row}" target="{tgt_row}" edge="1" '
            f'value="{escape(label)}" '
            f'style="edgeStyle=orthogonalEdgeStyle;rounded=0;orthogonalLoop=1;jettySize=auto;html=1;'
            f"startArrow=ERone;startFill=0;endArrow=ERmany;endFill=0;strokeColor={TEAL};"
            f'strokeWidth=1;fontSize=10;fontColor={TEAL};fontFamily=Consolas;'
            f'labelBackgroundColor=#FFFFFF;exitX=1;exitY=0.5;entryX=0;entryY=0.5;">'
            f'<mxGeometry relative="1" as="geometry" /></mxCell>'
        )

    def add_logical_relation(
        self,
        source_table: str,
        target_table: str,
        fk_field: str,
        label: str,
    ) -> None:
        src_meta = self.table_meta.get(source_table)
        tgt_meta = self.table_meta.get(target_table)
        if not src_meta or not tgt_meta:
            return
        src_row = src_meta["row_ids"].get(fk_field) or next(
            (rid for name, rid in src_meta["row_ids"].items() if name in src_meta["keys"]),
            None,
        )
        tgt_row = next(
            (rid for name, rid in tgt_meta["row_ids"].items() if name in tgt_meta["keys"]),
            None,
        )
        if not src_row or not tgt_row:
            return
        edge_id = self._nid("edge")
        self.cells.append(
            f'<mxCell id="{edge_id}" parent="1" source="{src_row}" target="{tgt_row}" edge="1" '
            f'value="{escape(label)}" '
            f"style=\"edgeStyle=orthogonalEdgeStyle;rounded=0;orthogonalLoop=1;jettySize=auto;html=1;"
            f"dashed=1;startArrow=ERone;startFill=0;endArrow=ERmany;endFill=0;strokeColor=#C9784A;"
            f"strokeWidth=1;fontSize=10;fontColor=#A34F1A;fontFamily=Consolas;"
            f'labelBackgroundColor=#FFFFFF;exitX=1;exitY=0.5;entryX=0;entryY=0.5;">'
            f'<mxGeometry relative="1" as="geometry" /></mxCell>'
        )

    def add_cross_db_relation(
        self,
        source_table: str,
        target_table: str,
        fk_field: str,
        label: str,
    ) -> None:
        """Dashed edge for REF columns pointing at another PostgreSQL database."""
        src_meta = self.table_meta.get(source_table)
        tgt_meta = self.table_meta.get(target_table)
        if not src_meta or not tgt_meta:
            return
        src_row = src_meta["row_ids"].get(fk_field)
        if not src_row:
            return
        tgt_row = next(
            (rid for name, rid in tgt_meta["row_ids"].items() if name in tgt_meta["keys"]),
            None,
        )
        if not tgt_row:
            return
        edge_id = self._nid("edge")
        self.cells.append(
            f'<mxCell id="{edge_id}" parent="1" source="{src_row}" target="{tgt_row}" edge="1" '
            f'value="{escape(label)}" '
            f"style=\"edgeStyle=orthogonalEdgeStyle;rounded=0;orthogonalLoop=1;jettySize=auto;html=1;"
            f"dashed=1;startArrow=ERone;startFill=0;endArrow=ERmany;endFill=0;strokeColor=#9333EA;"
            f"strokeWidth=1;fontSize=9;fontColor=#6B21A8;fontFamily=Consolas;"
            f'labelBackgroundColor=#FFFFFF;exitX=1;exitY=0.5;entryX=0;entryY=0.5;">'
            f'<mxGeometry relative="1" as="geometry" /></mxCell>'
        )

    def add_region_frame(
        self,
        label: str,
        x: float,
        y: float,
        w: float,
        h: float,
    ) -> str:
        cid = self._nid("region")
        self.cells.append(
            f'<mxCell id="{cid}" parent="1" vertex="1" value="{escape(label)}" '
            f"style=\"rounded=1;whiteSpace=wrap;html=1;fillColor={REGION_FILL};"
            f"strokeColor={REGION_STROKE};strokeWidth=2;fontColor={TEAL_DARK};"
            f'fontSize=12;fontStyle=1;fontFamily=Segoe UI;verticalAlign=top;align=center;spacingTop=8;arcSize=4;\">'
            f'<mxGeometry x="{x}" y="{y}" width="{w}" height="{h}" as="geometry" />'
            f"</mxCell>"
        )
        return cid

    def add_region_connector(self, source_region_id: str, target_region_id: str, label: str) -> None:
        eid = self._nid("edge")
        style = (
            "edgeStyle=orthogonalEdgeStyle;rounded=1;orthogonalLoop=1;jettySize=auto;html=1;"
            "endArrow=blockThin;endFill=1;strokeColor=#9333EA;strokeWidth=1.5;dashed=1;"
            "fontSize=9;fontColor=#6B21A8;fontFamily=Segoe UI;exitX=1;exitY=0.5;entryX=0;entryY=0.5;"
        )
        self.cells.append(
            f'<mxCell id="{eid}" parent="1" edge="1" source="{source_region_id}" target="{target_region_id}" '
            f'value="{escape(label)}" style="{style}">'
            f'<mxGeometry relative="1" as="geometry" />'
            f"</mxCell>"
        )

    def render_page(self) -> str:
        body = "\n        ".join(self.cells)
        return f"""  <diagram id="{self.page_id}" name="{escape(self.page_name, quote=False)}">
    <mxGraphModel dx="1400" dy="900" grid="1" gridSize="10" guides="1" tooltips="1" connect="1" arrows="1" fold="1" page="1" pageScale="1" pageWidth="{self.page_width}" pageHeight="{self.page_height}" math="0" shadow="0">
      <root>
        <mxCell id="0" />
        <mxCell id="1" parent="0" />
        {body}
      </root>
    </mxGraphModel>
  </diagram>"""


def page_size_for(count: int) -> tuple[int, int]:
    if count >= 50:
        return 4200, 3000
    if count >= 25:
        return 3600, 2400
    if count >= 15:
        return 3000, 2000
    return 2400, 1600


def notes_by_table(model: DatabaseContextModel) -> dict[str, list[tuple[str, str, str]]]:
    usage = external_ref_usage(model)
    grouped: dict[str, list[tuple[str, str, str]]] = {}
    for ref_name, tables in usage.items():
        target_db, entity, _desc, _mechanism = EXTERNAL_REF_TARGETS[ref_name]
        for table in tables:
            grouped.setdefault(table, []).append((ref_name, target_db, entity))
    for table in grouped:
        grouped[table].sort()
    return grouped


def add_service_legend(builder: DrawioBuilder, page_w: int) -> None:
    y = 88
    builder.add_box("PK  مفتاح أساسي", page_w - 400, y, 120, 28, fill=PK_FILL, stroke="#C9A227", font_color="#8A6D00")
    builder.add_box("FK  علاقة داخل القاعدة", page_w - 270, y, 140, 28, fill=FK_FILL, stroke="#6C8EBF", font_color="#1D4E89")
    builder.add_box("REF  حقل يشير لقاعدة أخرى", page_w - 120, y, 100, 28, fill=REF_FILL, stroke=REF_BORDER, font_color="#A34F1A")


def add_external_ref_notes(
    builder: DrawioBuilder,
    model: DatabaseContextModel,
    page_w: int,
) -> int:
    grouped = notes_by_table(model)
    if not grouped:
        return 120
    x = page_w - NOTE_W - 20
    y = 120
    builder.add_text(
        "ملاحظات: حقول تشير إلى قاعدة أخرى (بلا FK)",
        x,
        y - 26,
        NOTE_W,
        22,
        size=12,
        bold=True,
        color=REF_BORDER,
        align="right",
    )
    for table in sorted(grouped):
        lines = [f"{table}\n{translate_table(table, table)}"]
        for col, target_db, entity in grouped[table]:
            col_ar = translate_column(col)
            lines.append(f"{col} → {target_db}.{entity}\n{col_ar}")
        label = "\n".join(lines)
        stub_h = 28 + 18 * len(grouped[table]) + 12
        builder.add_box(
            label,
            x,
            y,
            NOTE_W,
            stub_h,
            fill=REF_FILL,
            stroke=REF_BORDER,
            font_color="#A34F1A",
            dashed=True,
            align="right",
            valign="top",
        )
        y += stub_h + 10
    return y


def layout_tables(
    builder: DrawioBuilder,
    business: list[EntityModel],
    infra: list[EntityModel],
    relations: list[RelationModel],
    logical_relations: list[RelationModel],
    fqn_to_table: dict[str, str],
) -> None:
    all_business = business
    count = len(all_business)
    cols = max(3, min(10, math.ceil(math.sqrt(max(count, 1)))))
    for idx, entity in enumerate(all_business):
        col = idx % cols
        row = idx // cols
        x = MARGIN + col * (TABLE_W + GAP_X)
        y = 120 + row * (HEADER_H + ROW_H * 6 + GAP_Y)
        table_id = builder.add_table(entity, x, y, infra=False)
        fqn_to_table[entity.fqn] = table_id

    if infra:
        infra_y = 120 + ((count + cols - 1) // cols) * (HEADER_H + ROW_H * 6 + GAP_Y) + 40
        builder.add_text(
            "جداول البنية (تدقيق / تكامل / Outbox)",
            MARGIN,
            infra_y - 28,
            800,
            24,
            size=14,
            bold=True,
            color=TEAL,
            align="left",
        )
        infra_cols = min(5, max(2, len(infra)))
        for idx, entity in enumerate(infra):
            col = idx % infra_cols
            row = idx // infra_cols
            x = MARGIN + col * (TABLE_W + GAP_X)
            y = infra_y + row * (HEADER_H + ROW_H * 5 + GAP_Y)
            table_id = builder.add_table(entity, x, y, infra=True)
            fqn_to_table[entity.fqn] = table_id

    for relation in relations:
        src = fqn_to_table.get(relation.source_fqn)
        tgt = fqn_to_table.get(relation.target_fqn)
        if not src or not tgt:
            continue
        fk = relation.fields[0] if relation.fields else "FK"
        builder.add_relation(src, tgt, fk, ", ".join(relation.fields) or "FK")

    for relation in logical_relations:
        src = fqn_to_table.get(relation.source_fqn)
        tgt = fqn_to_table.get(relation.target_fqn)
        if not src or not tgt:
            continue
        fk = relation.fields[0] if relation.fields else "REF"
        builder.add_logical_relation(src, tgt, fk, f"منطقي: {fk}")


def build_service_page(model: DatabaseContextModel) -> str:
    entities = list(model.entities.values())
    business = [e for e in entities if not e.is_infrastructure]
    infra = [e for e in entities if e.is_infrastructure]
    logical = infer_internal_logical_relations(model)
    grouped_notes = notes_by_table(model)
    page_w, page_h = page_size_for(len(entities))
    if grouped_notes:
        page_w += NOTE_W + 40
        notes_h = 140
        for refs in grouped_notes.values():
            notes_h += 32 + 16 * len(refs) + 10
        page_h = max(page_h, notes_h)
    page_id = f"erd-{model.index:02d}"
    page_name = f"{model.index:02d} — {model.service_ar} ({model.database})"
    builder = DrawioBuilder(page_id, page_name, page_w, page_h)
    builder.add_text(
        f"ERD — {model.database}",
        MARGIN,
        20,
        page_w - 2 * MARGIN,
        32,
        size=20,
        bold=True,
        color=TEAL,
    )
    builder.add_text(
        f"{model.service} / {model.context}  ·  {len(business)} جدول أعمال"
        + (f"  ·  {len(infra)} بنية" if infra else "")
        + (f"  ·  {len(model.relations)} علاقة FK" if model.relations else "")
        + (f"  ·  {len(logical)} علاقة منطقية داخل القاعدة" if logical else ""),
        MARGIN,
        54,
        page_w - 2 * MARGIN,
        22,
        size=12,
        align="left",
    )
    add_service_legend(builder, page_w)
    fqn_to_table: dict[str, str] = {}
    layout_tables(builder, business, infra, model.relations, logical, fqn_to_table)
    add_external_ref_notes(builder, model, page_w)
    builder.add_text(
        "FK = HasOne داخل هذه القاعدة. الخط البرتقالي المتقطع = مرجع منطقي داخلي. "
        "REF + الملاحظة الجانبية = حقل يشير إلى قاعدة أخرى. السطر العربي الصغير = ترجمة الاسم.",
        MARGIN,
        page_h - 50,
        page_w - 2 * MARGIN,
        32,
        size=11,
        align="left",
        color="#6B7280",
    )
    return builder.render_page()


def table_block_height(entity: EntityModel, *, unified: bool = False) -> float:
    if unified:
        rows = len(unified_display_rows(entity))
        return UNIFIED_HEADER_H + UNIFIED_ROW_H * (1 + max(rows, 1))
    rows = len(entity.display_columns())
    return HEADER_H + ROW_H * (1 + max(rows, 1))


def unified_display_rows(entity: EntityModel) -> list[tuple[str, str, str]]:
    """Show PK + every FK/REF so relationship edges can attach to column rows."""
    cols = entity.display_columns()
    pk = [c for c in cols if c[0] == "PK"]
    rel = [c for c in cols if c[0] in ("FK", "REF")]
    return pk + rel


def region_table_cols(entity_count: int, page_inner_width: float) -> int:
    max_by_width = int(
        (page_inner_width - 2 * UNIFIED_REGION_PAD + UNIFIED_GAP_X)
        // (UNIFIED_TABLE_W + UNIFIED_GAP_X)
    )
    max_by_width = max(1, max_by_width)
    if entity_count <= 4:
        ideal = entity_count
    elif entity_count <= 12:
        ideal = 4
    elif entity_count <= 24:
        ideal = 4
    elif entity_count <= 40:
        ideal = 5
    else:
        ideal = 6
    return max(1, min(entity_count, ideal, max_by_width))


def find_target_entity(model: DatabaseContextModel, target_name: str) -> EntityModel | None:
    for entity in model.entities.values():
        if entity.class_name == target_name or entity.table == target_name:
            return entity
        if target_name == "Files" and entity.table == "Files":
            return entity
        if entity.class_name.rstrip("s") == target_name.rstrip("s"):
            return entity
    return None


def collect_cross_db_refs(
    models: list[DatabaseContextModel],
) -> list[tuple[DatabaseContextModel, EntityModel, str, str, str]]:
    """Return (source_model, source_entity, column, target_db, target_entity_class)."""
    by_db = {m.database: m for m in models}
    refs: list[tuple[DatabaseContextModel, EntityModel, str, str, str]] = []
    seen: set[tuple[str, str, str]] = set()
    for model in models:
        local_skip = EXTERNAL_REF_SKIP.get(model.database, frozenset())
        for entity in model.entities.values():
            for marker, name, _ in entity.display_columns():
                if marker != "REF" or name not in EXTERNAL_REF_TARGETS:
                    continue
                if name in local_skip:
                    continue
                target_db, target_entity, _, _ = EXTERNAL_REF_TARGETS[name]
                if target_db == model.database or target_db == "keycloak":
                    continue
                key = (model.database, entity.fqn, name)
                if key in seen:
                    continue
                seen.add(key)
                if target_db not in by_db:
                    continue
                if find_target_entity(by_db[target_db], target_entity) is None:
                    continue
                refs.append((model, entity, name, target_db, target_entity))
    return refs


def _sorted_entities(model: DatabaseContextModel) -> list[EntityModel]:
    return sorted(
        model.entities.values(),
        key=lambda e: (e.is_infrastructure, e.table),
    )


def estimate_region_size(model: DatabaseContextModel, page_inner_width: float) -> tuple[float, float]:
    entities = _sorted_entities(model)
    if not entities:
        return min(400.0, page_inner_width), UNIFIED_REGION_HEADER + 2 * UNIFIED_REGION_PAD
    cols = region_table_cols(len(entities), page_inner_width)
    total_rows = (len(entities) + cols - 1) // cols
    row_heights: dict[int, float] = {}
    for idx, entity in enumerate(entities):
        row = idx // cols
        row_heights[row] = max(row_heights.get(row, 0.0), table_block_height(entity, unified=True))
    content_h = sum(row_heights[r] for r in range(total_rows))
    content_h += UNIFIED_GAP_Y * max(0, total_rows - 1)
    region_w = UNIFIED_REGION_PAD * 2 + cols * UNIFIED_TABLE_W + (cols - 1) * UNIFIED_GAP_X
    region_w = min(region_w, page_inner_width)
    region_h = UNIFIED_REGION_HEADER + UNIFIED_REGION_PAD * 2 + content_h
    return region_w, region_h


def layout_unified_region(
    builder: DrawioBuilder,
    model: DatabaseContextModel,
    region_id: str,
    fqn_to_table: dict[str, str],
    page_inner_width: float,
) -> tuple[float, float]:
    """Place all tables inside a region frame; return (width, height)."""
    entities = _sorted_entities(model)
    if not entities:
        return 400.0, UNIFIED_REGION_HEADER + 2 * UNIFIED_REGION_PAD

    cols = region_table_cols(len(entities), page_inner_width)
    total_rows = (len(entities) + cols - 1) // cols
    row_heights: dict[int, float] = {}
    for idx, entity in enumerate(entities):
        row = idx // cols
        row_heights[row] = max(row_heights.get(row, 0.0), table_block_height(entity, unified=True))

    row_y_offsets: dict[int, float] = {}
    y_acc = 0.0
    for row in range(total_rows):
        row_y_offsets[row] = y_acc
        y_acc += row_heights[row] + UNIFIED_GAP_Y

    for idx, entity in enumerate(entities):
        col = idx % cols
        row = idx // cols
        x = UNIFIED_REGION_PAD + col * (UNIFIED_TABLE_W + UNIFIED_GAP_X)
        y = UNIFIED_REGION_HEADER + UNIFIED_REGION_PAD + row_y_offsets[row]
        table_id = builder.add_table(
            entity,
            x,
            y,
            infra=entity.is_infrastructure,
            unified=True,
            parent=region_id,
        )
        fqn_to_table[entity.fqn] = table_id

    content_h = sum(row_heights[r] for r in range(total_rows))
    content_h += UNIFIED_GAP_Y * max(0, total_rows - 1)
    region_w = UNIFIED_REGION_PAD * 2 + cols * UNIFIED_TABLE_W + (cols - 1) * UNIFIED_GAP_X
    region_h = UNIFIED_REGION_HEADER + UNIFIED_REGION_PAD * 2 + content_h
    return region_w, region_h


def cross_db_ref_counts(
    models: list[DatabaseContextModel],
) -> tuple[dict[str, int], dict[str, int], dict[tuple[str, str], int]]:
    out_counts: dict[str, int] = defaultdict(int)
    in_counts: dict[str, int] = defaultdict(int)
    pair_counts: dict[tuple[str, str], int] = defaultdict(int)
    for model, _entity, _col, target_db, _target_entity in collect_cross_db_refs(models):
        out_counts[model.database] += 1
        in_counts[target_db] += 1
        pair_counts[(model.database, target_db)] += 1
    return dict(out_counts), dict(in_counts), dict(pair_counts)


def build_unified_landscape_page(models: list[DatabaseContextModel]) -> str:
    """Single-page ERD: all tables with FK, logical, and cross-DB REF edges (table-level)."""
    page_w = UNIFIED_PAGE_WIDTH
    cell_w = unified_cell_width(page_w)
    cell_inner = cell_w - 2
    cross_refs = collect_cross_db_refs(models)
    cross_total = len(cross_refs)

    placements = layout_unified_grid(models, page_w)
    page_h = int(max(ry + rh for _, _, ry, _, rh in placements) + 100)

    builder = DrawioBuilder("erd-unified", "17 — المخطط الشامل (15 قاعدة)", page_w, page_h)
    builder.add_text(
        "ERD الشامل — شبكة 5×3 (15 قاعدة) · جداول وعلاقاتها",
        MARGIN,
        16,
        page_w - 2 * MARGIN,
        34,
        size=22,
        bold=True,
        color=TEAL,
    )
    builder.add_text(
        f"{sum(len(m.entities) for m in models)} جدول  ·  "
        "خط teal = FK  ·  خط برتقالي متقطع = مرجع منطقي داخل القاعدة  ·  "
        f"خط بنفسجي متقطع = REF عابر بين جدول وجدول ({cross_total})",
        MARGIN,
        52,
        page_w - 2 * MARGIN,
        24,
        size=11,
        align="left",
    )
    add_service_legend(builder, page_w)
    builder.add_box(
        "REF عابر",
        page_w - 520,
        88,
        100,
        28,
        fill="#F3E8FF",
        stroke="#9333EA",
        font_color="#6B21A8",
    )

    fqn_to_table: dict[str, str] = {}
    models_by_db = {m.database: m for m in models}
    for model, rx, ry, rw, rh in placements:
        title = (
            f"{model.index:02d}  {model.database}  ·  {model.service_ar}  ·  "
            f"{len(model.entities)} جدول"
        )
        region_id = builder.add_region_frame(title, rx, ry, rw, rh)
        layout_unified_region(builder, model, region_id, fqn_to_table, cell_inner)

    for model in models:
        logical = infer_internal_logical_relations(model)
        for relation in model.relations:
            src = fqn_to_table.get(relation.source_fqn)
            tgt = fqn_to_table.get(relation.target_fqn)
            if not src or not tgt:
                continue
            fk = relation.fields[0] if relation.fields else "FK"
            builder.add_relation(src, tgt, fk, ", ".join(relation.fields) or "FK")
        for relation in logical:
            src = fqn_to_table.get(relation.source_fqn)
            tgt = fqn_to_table.get(relation.target_fqn)
            if not src or not tgt:
                continue
            fk = relation.fields[0] if relation.fields else "REF"
            builder.add_logical_relation(src, tgt, fk, f"منطقي: {fk}")

    cross_count = 0
    for model, entity, col, target_db, target_entity in cross_refs:
        src_table = fqn_to_table.get(entity.fqn)
        if not src_table:
            continue
        if col in EXTERNAL_REF_SKIP.get(model.database, frozenset()):
            continue
        target_model = models_by_db.get(target_db)
        if not target_model:
            continue
        target_ent = find_target_entity(target_model, target_entity)
        if not target_ent:
            continue
        tgt_table = fqn_to_table.get(target_ent.fqn)
        if not tgt_table:
            continue
        builder.add_cross_db_relation(src_table, tgt_table, col, f"{col} → {target_db}")
        cross_count += 1

    kc_id = builder.add_box(
        "Keycloak.User\n(خارج PostgreSQL)\nKeycloakUserId",
        page_w - UNIFIED_TABLE_W - MARGIN,
        96,
        UNIFIED_TABLE_W,
        68,
        fill="#FEF3C7",
        stroke="#D97706",
        font_color="#92400E",
        dashed=True,
    )
    builder.table_meta[kc_id] = {
        "fqn": "external.keycloak.User",
        "table": "Keycloak.User",
        "x": page_w - UNIFIED_TABLE_W - MARGIN,
        "y": 96,
        "row_ids": {"Id": kc_id, "KeycloakUserId": kc_id},
        "keys": {"Id"},
        "parent": "1",
    }
    for model in models:
        local_skip = EXTERNAL_REF_SKIP.get(model.database, frozenset())
        for entity in model.entities.values():
            if "KeycloakUserId" not in entity.properties:
                continue
            if "KeycloakUserId" in local_skip:
                continue
            src_table = fqn_to_table.get(entity.fqn)
            if not src_table:
                continue
            builder.add_cross_db_relation(src_table, kc_id, "KeycloakUserId", "KeycloakUserId → Keycloak")

    builder.add_text(
        f"كل جدول مربوط بعلاقاته: FK + منطقي + {cross_count} REF عابر بين الجداول. "
        "لا أسهم HTTP/حدث.",
        MARGIN,
        page_h - 44,
        page_w - 2 * MARGIN,
        32,
        size=11,
        align="left",
        color="#6B7280",
    )
    return builder.render_page()


def build_index_page(models: list[DatabaseContextModel]) -> str:
    page_w, page_h = 2000, 1100
    builder = DrawioBuilder("erd-00", "00 — فهرس قواعد البيانات", page_w, page_h)
    builder.add_text(
        "فهرس مخططات ERD",
        MARGIN,
        20,
        page_w - 2 * MARGIN,
        32,
        size=20,
        bold=True,
        color=TEAL,
    )
    builder.add_text(
        "كل صفحة 01–15 تحتوي جداول قاعدة واحدة. الصفحة 17 تجمع كل القواعد في مخطط شامل مع علاقات FK وREF العابرة.",
        MARGIN,
        56,
        page_w - 2 * MARGIN,
        24,
        size=12,
        align="left",
    )
    cols = 5
    box_w, box_h = 340, 150
    builder.add_box(
        "17 — المخطط الشامل\nجميع الجداول (15 قاعدة)\nFK داخل القاعدة + REF عابر",
        MARGIN,
        920,
        box_w,
        box_h,
        fill="#F3E8FF",
        stroke="#9333EA",
        font_color="#6B21A8",
    )
    for idx, model in enumerate(models):
        col = idx % cols
        row = idx // cols
        x = MARGIN + col * (box_w + 24)
        y = 110 + row * (box_h + 24)
        grouped = notes_by_table(model)
        note_count = sum(len(v) for v in grouped.values())
        extra = f"\n{note_count} حقل يشير إلى قاعدة أخرى" if note_count else "\nلا حقول خارجية"
        builder.add_box(
            f"{model.index:02d}  {model.database}\n{model.service_ar}\n"
            f"{len(model.entities)} جدول  ·  {len(model.relations)} FK"
            f"{extra}",
            x,
            y,
            box_w,
            box_h,
            fill="#F7FBFC",
            stroke=TEAL,
            font_color=TEAL_DARK,
        )
    return builder.render_page()


def build_workbook(models: list[DatabaseContextModel]) -> str:
    pages = [build_index_page(models)]
    pages.extend(build_service_page(model) for model in models)
    pages.append(build_unified_landscape_page(models))
    joined = "\n".join(pages)
    return (
        '<?xml version="1.0" encoding="UTF-8"?>\n'
        '<mxfile host="app.diagrams.net" agent="HRManagement database ERD generator" '
        'version="22.1.0" type="device">\n'
        f"{joined}\n"
        "</mxfile>\n"
    )


def validate(models: list[DatabaseContextModel], xml: str) -> dict:
    pages = xml.split("<diagram ")
    integration = pages[1] if len(pages) > 1 else ""
    unified = pages[-1] if pages else ""
    total_tables = sum(len(m.entities) for m in models)
    cross_refs = len(collect_cross_db_refs(models))
    report: dict = {
        "pages": len(models) + 2,
        "contexts": [],
        "unified_page": "erd-unified" in xml,
        "unified_tables": total_tables,
        "unified_cross_db_refs": cross_refs,
        "unified_has_cross_edges": "#9333EA" in unified,
        "index_has_er_fk": "ERone" in integration,
        "has_http_arrows": "HTTP:" in xml,
        "has_event_arrows": "حدث:" in xml,
    }
    for model in models:
        snapshot_text = model.snapshot_path.read_text(encoding="utf-8-sig", errors="ignore")
        table_count = len(re.findall(r'b\.ToTable\("', snapshot_text))
        hasone_count = len(re.findall(r"b\.HasOne\(", snapshot_text))
        report["contexts"].append(
            {
                "context": model.context,
                "database": model.database,
                "snapshot_tables": table_count,
                "diagram_entities": len(model.entities),
                "snapshot_hasone": hasone_count,
                "diagram_relations": len(model.relations),
                "external_ref_tables": len(notes_by_table(model)),
            }
        )
    return report


def main() -> None:
    models = load_all_database_models()
    xml = build_workbook(models)
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(xml, encoding="utf-8")

    unified_xml = (
        '<?xml version="1.0" encoding="UTF-8"?>\n'
        '<mxfile host="app.diagrams.net" agent="HRManagement database ERD unified" '
        'version="22.1.0" type="device">\n'
        f"{build_unified_landscape_page(models)}\n"
        "</mxfile>\n"
    )
    OUTPUT_UNIFIED.write_text(unified_xml, encoding="utf-8")

    VALIDATION.parent.mkdir(parents=True, exist_ok=True)
    report = validate(models, xml)
    report["output"] = str(OUTPUT.relative_to(ROOT)).replace("\\", "/")
    report["output_unified"] = str(OUTPUT_UNIFIED.relative_to(ROOT)).replace("\\", "/")
    VALIDATION.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(
        json.dumps(
            {
                "pages": len(models) + 2,
                "output": str(OUTPUT),
                "output_unified": str(OUTPUT_UNIFIED),
                "contexts": len(models),
                "unified_tables": report["unified_tables"],
                "cross_db_refs": report["unified_cross_db_refs"],
                "http_arrows": report["has_http_arrows"],
                "event_arrows": report["has_event_arrows"],
            },
            ensure_ascii=False,
        )
    )


if __name__ == "__main__":
    main()
