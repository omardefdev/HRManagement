"""Generate one complete logical ERD source for every database context.

The snapshots are the most stable repository evidence for the declared schema.
The generated diagrams intentionally show keys and relationships, not every
audit timestamp or scalar column, so that the complete context remains legible.
"""

from __future__ import annotations

import json
import re
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
INVENTORY = ROOT / "docs" / "DATABASE_SCHEMA_INVENTORY.json"
OUT_DIR = ROOT / "docs" / "thesis_diagrams" / "database_erdds"
SOURCE_DIR = OUT_DIR / "source"
RENDER_DIR = OUT_DIR / "rendered"
COMPACT_SOURCE_DIR = OUT_DIR / "compact_source"
COMPACT_RENDER_DIR = OUT_DIR / "compact_rendered"
MANIFEST = OUT_DIR / "manifest.json"


def balanced_block(text: str, brace_start: int) -> str:
    depth = 0
    for index in range(brace_start, len(text)):
        if text[index] == "{":
            depth += 1
        elif text[index] == "}":
            depth -= 1
            if depth == 0:
                return text[brace_start + 1:index]
    return text[brace_start + 1:]


def quoted_values(value: str) -> list[str]:
    return re.findall(r'"([^"]+)"', value)


def short_name(full_name: str) -> str:
    return full_name.rsplit(".", 1)[-1]


def type_name(raw: str) -> str:
    value = raw.strip().replace("?", "")
    return {
        "Guid": "uuid",
        "DateTime": "timestamp",
        "DateTimeOffset": "timestamp",
        "DateOnly": "date",
        "TimeOnly": "time",
        "int": "int",
        "long": "bigint",
        "decimal": "decimal",
        "double": "double",
        "bool": "bool",
        "string": "text",
    }.get(value, value.lower())


def snapshot_entities(text: str) -> tuple[dict[str, dict], list[dict]]:
    entities: dict[str, dict] = {}
    relation_blocks: list[dict] = []
    pattern = re.compile(r'modelBuilder\.Entity\("([^"]+)"\s*,\s*b\s*=>\s*\{')
    for match in pattern.finditer(text):
        fqn = match.group(1)
        block = balanced_block(text, text.find("{", match.start()))
        table_match = re.search(r'b\.ToTable\("([^"]+)"', block)
        has_key = re.search(r'b\.HasKey\(([^)]*)\)', block, flags=re.S)
        keys = quoted_values(has_key.group(1)) if has_key else []
        properties: dict[str, str] = {}
        for prop in re.finditer(r'b\.Property<([^>]+)>\("([^"]+)"\)', block):
            properties[prop.group(2)] = type_name(prop.group(1))
        if table_match:
            entities[fqn] = {
                "fqn": fqn,
                "class": short_name(fqn),
                "table": table_match.group(1),
                "keys": keys,
                "properties": properties,
            }
        if "b.HasOne(" in block:
            relation_blocks.append({"source": fqn, "block": block})
    return entities, relation_blocks


def relationships(entities: dict[str, dict], blocks: list[dict]) -> list[dict]:
    result: list[dict] = []
    seen: set[tuple[str, str, tuple[str, ...]]] = set()
    for item in blocks:
        block = item["block"]
        for match in re.finditer(
            r'b\.HasOne\("([^"]+)"(?:\s*,\s*"[^"]+")?\)(.*?)(?=;|$)',
            block,
            flags=re.S,
        ):
            target = match.group(1)
            if target not in entities or item["source"] not in entities:
                continue
            foreign = re.search(r'\.HasForeignKey\(([^)]*)\)', match.group(2), flags=re.S)
            fields = tuple(quoted_values(foreign.group(1))) if foreign else tuple()
            key = (item["source"], target, fields)
            if key in seen:
                continue
            seen.add(key)
            result.append({"source": item["source"], "target": target, "fields": list(fields)})
    return result


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
    items = json.loads(INVENTORY.read_text(encoding="utf-8"))
    SOURCE_DIR.mkdir(parents=True, exist_ok=True)
    RENDER_DIR.mkdir(parents=True, exist_ok=True)
    COMPACT_SOURCE_DIR.mkdir(parents=True, exist_ok=True)
    COMPACT_RENDER_DIR.mkdir(parents=True, exist_ok=True)
    manifest = []
    for index, item in enumerate(items, 1):
        snapshot_source = item.get("snapshot_source")
        if not snapshot_source:
            continue
        snapshot = ROOT / snapshot_source
        text = snapshot.read_text(encoding="utf-8-sig", errors="ignore")
        entities, blocks = snapshot_entities(text)
        relations = relationships(entities, blocks)
        stem = f"{index:02d}_{item['context'].removesuffix('DbContext').lower()}"
        source = SOURCE_DIR / f"{stem}.puml"
        source.write_text(erd_source(item, entities, relations), encoding="utf-8")
        compact_source = COMPACT_SOURCE_DIR / f"{stem}_compact.puml"
        compact_source.write_text(compact_erd_source(item, entities, relations), encoding="utf-8")
        manifest.append({
            "index": index,
            "service_ar": item["service_ar"],
            "service": item["service"],
            "context": item["context"],
            "database": item["database"],
            "source": str(source.relative_to(ROOT)).replace("\\", "/"),
            "image": f"{stem}.png",
            "compact_source": str(compact_source.relative_to(ROOT)).replace("\\", "/"),
            "compact_image": f"{stem}_compact.png",
            "entity_count": len(entities),
            "relationship_count": len(relations),
        })
    MANIFEST.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"diagrams": len(manifest), "manifest": str(MANIFEST)}, ensure_ascii=False))


if __name__ == "__main__":
    main()
