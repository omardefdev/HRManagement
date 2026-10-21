"""Extract a reviewable database inventory from the HRManagement source tree."""

from __future__ import annotations

import json
import re
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "src"
OUT_JSON = ROOT / "docs" / "DATABASE_SCHEMA_INVENTORY.json"
OUT_MD = ROOT / "docs" / "DATABASE_SCHEMA_INVENTORY.md"

SERVICE_NAMES = {
    "GatewayAuditDbContext": ("البوابة والتدقيق", "Gateway Audit", "gateway_audit_db"),
    "AdminLogisticsDbContext": ("اللوجستيات الإدارية", "Admin.Logistics", "admin_logistics_db"),
    "AuthDirectoryDbContext": ("الدليل والمصادقة", "Auth.Directory", "auth_directory_db"),
    "ComplianceDbContext": ("الامتثال والشكاوى", "Compliance", "compliance_db"),
    "AnalyticsDbContext": ("التحليلات", "Dev.Analytics", "analytics_db"),
    "FileStorageDbContext": ("تخزين الملفات", "File.Storage", "file_storage_db"),
    "ContractsDbContext": ("العقود والخدمة", "HR.Contracts", "hr_contracts_db"),
    "HRCoreDbContext": ("النواة والموظفون", "HR.Core", "hr_core_db"),
    "HealthDbContext": ("الصحة المهنية", "HR.Health", "hr_health_db"),
    "MovementsDbContext": ("الحركات والتنقلات", "HR.Movements", "hr_movements_db"),
    "TrainingDbContext": ("التدريب والتعلم", "HR.Training", "hr_training_db"),
    "IDCardDbContext": ("الهويات والبطاقات", "IDCard", "id_card_db"),
    "ITInfraDbContext": ("البنية التقنية", "IT.Infra", "it_infra_db"),
    "PayrollDbContext": ("الرواتب والتعويضات", "Payroll", "payroll_db"),
    "ArchiveDbContext": ("الأرشيف والسجلات", "Records.Archive", "records_archive_db"),
}


def database_name_for(context_path: Path, fallback: str) -> str:
    for settings in context_path.parents:
        for appsettings in settings.glob("*.Web/appsettings.json"):
            try:
                payload = json.loads(appsettings.read_text(encoding="utf-8-sig"))
            except (OSError, json.JSONDecodeError):
                continue
            for value in payload.get("ConnectionStrings", {}).values():
                match = re.search(r"(?:^|;)Database=([^;]+)", str(value), re.I)
                if match:
                    return match.group(1)
        if settings == SRC:
            break
    return fallback


def snapshot_for(context_path: Path, context_name: str) -> Path | None:
    candidates = list(context_path.parent.parent.parent.rglob("*ModelSnapshot.cs"))
    for candidate in candidates:
        text = candidate.read_text(encoding="utf-8-sig", errors="ignore")
        if f"typeof({context_name})" in text:
            return candidate
    return None


def snapshot_tables(snapshot: Path | None) -> list[str]:
    if snapshot is None:
        return []
    text = snapshot.read_text(encoding="utf-8-sig", errors="ignore")
    return sorted(set(re.findall(r'b\.ToTable\("([^"]+)"', text)))


def extract_context(path: Path) -> dict[str, object] | None:
    text = path.read_text(encoding="utf-8-sig", errors="ignore")
    if re.search(r"\babstract\s+class\s+\w+DbContext\b", text):
        return None
    context_match = re.search(r"class\s+(\w+DbContext)\b", text)
    if not context_match:
        return None
    context = context_match.group(1)
    sets = []
    for match in re.finditer(r"DbSet<\s*(.+?)\s*>\s+(\w+)\s*=>", text, flags=re.S):
        entity = re.sub(r"\s+", " ", match.group(1)).strip()
        entity = entity.rsplit(".", 1)[-1]
        sets.append({"entity": entity, "set": match.group(2)})
    snapshot = snapshot_for(path, context)
    tables = snapshot_tables(snapshot)
    arabic, english, fallback_db = SERVICE_NAMES.get(context, (context, context, context.removesuffix("DbContext").lower()))
    db_name = database_name_for(path, fallback_db)
    infra_tables = {"AuditLogs", "IntegrationMessageReceipts", "InboxState", "OutboxState", "OutboxMessage"}
    business_tables = [table for table in tables if table not in infra_tables]
    return {
        "service_ar": arabic,
        "service": english,
        "context": context,
        "database": db_name,
        "dbset_count": len(sets),
        "entity_sets": sets,
        "snapshot_tables": tables,
        "business_table_count": len(business_tables),
        "infrastructure_tables": [table for table in tables if table in infra_tables],
        "source": str(path.relative_to(ROOT)).replace("\\", "/"),
        "snapshot_source": str(snapshot.relative_to(ROOT)).replace("\\", "/") if snapshot else None,
    }


def build_inventory() -> list[dict[str, object]]:
    items = []
    for path in sorted(SRC.rglob("*DbContext.cs")):
        if "Tests" in path.parts or "Migrations" in path.parts:
            continue
        item = extract_context(path)
        if item:
            items.append(item)
    return sorted(items, key=lambda item: (item["service"] != "Gateway Audit", str(item["service"])))


def write_outputs(items: list[dict[str, object]]) -> None:
    OUT_JSON.write_text(json.dumps(items, ensure_ascii=False, indent=2), encoding="utf-8")
    lines = [
        "# جرد مخطط قواعد البيانات",
        "",
        "هذا الجرد مولد آلياً من ملفات `DbContext` و`ModelSnapshot` و`appsettings.json` في المشروع. لا يعني وجود جدول في snapshot أنه مغطى باختبار تكامل حي؛ بل يثبت أنه جزء من النموذج البرمجي المعلن.",
        "",
        "| الخدمة | DbContext | قاعدة البيانات | DbSet | جداول الأعمال | جداول البنية | المصدر |",
        "|---|---|---|---:|---:|---|---|",
    ]
    for item in items:
        infra = ", ".join(item["infrastructure_tables"]) or "-"
        lines.append(
            f"| {item['service_ar']} ({item['service']}) | `{item['context']}` | `{item['database']}` | {item['dbset_count']} | {item['business_table_count']} | {infra} | `{item['source']}` |"
        )
    lines += ["", "## الكيانات المعلنة لكل سياق", ""]
    for item in items:
        names = ", ".join(f"`{entry['entity']}`" for entry in item["entity_sets"])
        lines.append(f"### {item['service_ar']} ({item['service']})")
        lines.append("")
        lines.append(names or "لا توجد DbSet معلنة مباشرة.")
        lines.append("")
    OUT_MD.write_text("\n".join(lines) + "\n", encoding="utf-8")


if __name__ == "__main__":
    inventory = build_inventory()
    write_outputs(inventory)
    print(json.dumps({"contexts": len(inventory), "dbsets": sum(item["dbset_count"] for item in inventory), "json": str(OUT_JSON), "markdown": str(OUT_MD)}, ensure_ascii=False))
