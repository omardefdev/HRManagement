"""Generate one complete logical ERD source for every database context.

The snapshots are the most stable repository evidence for the declared schema.
The generated diagrams intentionally show keys and relationships, not every
audit timestamp or scalar column, so that the complete context remains legible.
"""

from __future__ import annotations

import json
from pathlib import Path

from database_schema_parser import load_all_database_models


ROOT = Path(__file__).resolve().parents[1]
OUT_DIR = ROOT / "docs" / "thesis_diagrams" / "database_erdds"
SOURCE_DIR = OUT_DIR / "source"
RENDER_DIR = OUT_DIR / "rendered"
COMPACT_SOURCE_DIR = OUT_DIR / "compact_source"
COMPACT_RENDER_DIR = OUT_DIR / "compact_rendered"
MANIFEST = OUT_DIR / "manifest.json"


def entity_dict(entity) -> dict:
    return {
        "fqn": entity.fqn,
        "class": entity.class_name,
        "table": entity.table,
        "keys": entity.keys,
        "properties": entity.properties,
    }


def relation_dict(relation) -> dict:
    return {
        "source": relation.source_fqn,
        "target": relation.target_fqn,
        "fields": relation.fields,
    }


def erd_source(item: dict, entities: dict[str, dict], relations: list[dict]) -> str:
    aliases = {fqn: f"T{index:02d}" for index, fqn in enumerate(entities, 1)}
    lines = [
        "@startuml",
        "!theme plain",
        "skinparam backgroundColor #FFFFFF",
        "skinparam defaultFontName Arial",
        "skinparam defaultFontSize 15",
        "skinparam shadowing false",
        "skinparam roundcorner 8",
        "skinparam linetype ortho",
        "skinparam dpi 170",
        "skinparam EntityBorderColor #0F6B73",
        "skinparam EntityBackgroundColor #F4FAFA",
        "top to bottom direction",
        f'title "ERD الكامل لقاعدة {item["database"]}\\n{item["service"]} / {item["context"]}"',
        "",
    ]
    for fqn, entity in entities.items():
        lines.append(f'entity "{entity["table"]}\\n{entity["class"]}" as {aliases[fqn]} {{')
        keys = set(entity["keys"])
        names = list(entity["properties"])
        ordered = [name for name in names if name in keys] + [
            name for name in names if name not in keys and (name.endswith("Id") or name.endswith("Ids"))
        ]
        if not ordered:
            ordered = names[:1]
        for name in dict.fromkeys(ordered):
            marker = "* " if name in keys else "  "
            lines.append(f"  {marker}{name} : {entity['properties'].get(name, 'unknown')}")
        if not ordered:
            lines.append("  (key metadata unavailable in snapshot)")
        lines.append("}")
    lines.append("")
    for relation in relations:
        label = ", ".join(relation["fields"]) or "relationship"
        lines.append(f'{aliases[relation["source"]]} }}o--|| {aliases[relation["target"]]} : {label}')
    lines.extend([
        "",
        "note bottom",
        "يعرض هذا المخطط نموذج البيانات النهائي للسياق، بما يشمل الجداول والمفاتيح والعلاقات الداخلية.",
        "المعرفات مثل EmployeeId أو OrgUnitId التي لا تظهر كعلاقة داخلية هي معرفات تكاملية عابرة للخدمات وليست FK بين قواعد البيانات.",
        "end note",
        "@enduml",
    ])
    return "\n".join(lines) + "\n"


def compact_erd_source(item: dict, entities: dict[str, dict], relations: list[dict]) -> str:
    aliases = {fqn: f"T{index:02d}" for index, fqn in enumerate(entities, 1)}
    lines = [
        "@startuml",
        "!theme plain",
        "skinparam backgroundColor #FFFFFF",
        "skinparam defaultFontName Arial",
        "skinparam defaultFontSize 17",
        "skinparam shadowing false",
        "skinparam roundcorner 8",
        "skinparam linetype ortho",
        "skinparam dpi 170",
        "skinparam EntityBorderColor #0F6B73",
        "skinparam EntityBackgroundColor #F4FAFA",
        "top to bottom direction",
        f'title "خريطة ERD المختصرة لقاعدة {item["database"]}\\n{item["service"]}"',
        "",
    ]
    for fqn, entity in entities.items():
        lines.append(f'entity "{entity["table"]}\\n{entity["class"]}" as {aliases[fqn]} {{')
        keys = set(entity["keys"])
        names = list(entity["properties"])
        foreign = [name for name in names if name.endswith("Id") or name.endswith("Ids")]
        for name in dict.fromkeys([name for name in names if name in keys] + foreign):
            marker = "* " if name in keys else "  "
            lines.append(f"  {marker}{name}")
        if not names:
            lines.append("  * Id")
        lines.append("}")
    lines.append("")
    for relation in relations:
        label = ", ".join(relation["fields"]) or "relationship"
        lines.append(f'{aliases[relation["source"]]} }}o--|| {aliases[relation["target"]]} : {label}')
    lines.extend(["", "note bottom", "الخريطة المختصرة تعرض الجداول والعلاقات الأساسية؛ التفاصيل الكاملة في ملحق ERD.", "end note", "@enduml"])
    return "\n".join(lines) + "\n"


def main() -> None:
    models = load_all_database_models()
    SOURCE_DIR.mkdir(parents=True, exist_ok=True)
    RENDER_DIR.mkdir(parents=True, exist_ok=True)
    COMPACT_SOURCE_DIR.mkdir(parents=True, exist_ok=True)
    COMPACT_RENDER_DIR.mkdir(parents=True, exist_ok=True)
    manifest = []
    for model in models:
        entities = {fqn: entity_dict(e) for fqn, e in model.entities.items()}
        relations = [relation_dict(r) for r in model.relations]
        item = {
            "service_ar": model.service_ar,
            "service": model.service,
            "context": model.context,
            "database": model.database,
        }
        stem = f"{model.index:02d}_{model.context.removesuffix('DbContext').lower()}"
        source = SOURCE_DIR / f"{stem}.puml"
        source.write_text(erd_source(item, entities, relations), encoding="utf-8")
        compact_source = COMPACT_SOURCE_DIR / f"{stem}_compact.puml"
        compact_source.write_text(compact_erd_source(item, entities, relations), encoding="utf-8")
        manifest.append({
            "index": model.index,
            "service_ar": model.service_ar,
            "service": model.service,
            "context": model.context,
            "database": model.database,
            "source": str(source.relative_to(ROOT)).replace("\\", "/"),
            "image": f"{stem}.png",
            "compact_source": str(compact_source.relative_to(ROOT)).replace("\\", "/"),
            "compact_image": f"{stem}_compact.png",
            "entity_count": len(model.entities),
            "relationship_count": len(model.relations),
        })
    MANIFEST.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"diagrams": len(manifest), "manifest": str(MANIFEST)}, ensure_ascii=False))


if __name__ == "__main__":
    main()
