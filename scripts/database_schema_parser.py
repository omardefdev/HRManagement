"""Parse EF Core DbContext + ModelSnapshot evidence from the HRManagement source tree."""

from __future__ import annotations

import json
import re
from dataclasses import dataclass, field
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "src"

INFRA_TABLE_NAMES = frozenset({
    "AuditLogs",
    "EntityAuditLog",
    "EntityAuditLogs",
    "IntegrationMessageReceipts",
    "IntegrationMessageReceipt",
    "InboxState",
    "OutboxMessage",
    "OutboxState",
    "FileReferenceOutboxMessage",
    "FileReferenceOutbox",
    "IdempotencyRecords",
    "IdempotencyRecord",
})

LOGICAL_REF_COLUMNS = frozenset({
    "EmployeeId",
    "OrgUnitId",
    "PrimaryOrgUnitId",
    "ScopeOrgUnitId",
    "FileId",
    "HandoverFileId",
    "ReturnFileId",
    "PhotoFileId",
    "LossFileId",
    "WorkflowInstanceId",
    "TenantId",
    "MessageId",
    "CorrelationId",
    "RequestedBy",
    "ReceivedBy",
    "AssignedTo",
    "IssuedBy",
    "CompletedBy",
    "ApprovedBy",
    "NominatedBy",
    "GradedBy",
    "PrintedBy",
    "DeliveredTo",
    "PerformedBy",
    "OriginatingUnit",
    "SubjectEmployeeId",
    "ResponsibleUnitId",
    "ReporterProtectedId",
    "GeneratedBy",
    "UploadedBy",
    "ReportedBy",
    "HandledBy",
    "DisposeRequestedBy",
    "DisposeApprovedBy",
    "ActorUserId",
    "Creator",
})

# Cross-database logical references (no PostgreSQL FK). Shown as dashed stubs on service pages.
# value: (database, entity, arabic, mechanism)
EXTERNAL_REF_TARGETS: dict[str, tuple[str, str, str, str]] = {
    "EmployeeId": ("hr_core_db", "Employee", "موظف في النواة", "مرجع منطقي + HTTP lookup"),
    "OrgUnitId": ("hr_core_db", "OrgUnit", "وحدة تنظيمية", "مرجع منطقي"),
    "PrimaryOrgUnitId": ("hr_core_db", "OrgUnit", "وحدة تنظيمية", "مرجع منطقي"),
    "ScopeOrgUnitId": ("hr_core_db", "OrgUnit", "نطاق الوحدة", "مرجع منطقي"),
    "FromOrgUnitId": ("hr_core_db", "OrgUnit", "وحدة مصدر النقل", "مرجع منطقي"),
    "ToOrgUnitId": ("hr_core_db", "OrgUnit", "وحدة مقصد النقل", "مرجع منطقي"),
    "LendingOrgUnitId": ("hr_core_db", "OrgUnit", "وحدة مُعيرة", "مرجع منطقي"),
    "BorrowingOrgUnitId": ("hr_core_db", "OrgUnit", "وحدة مستفيدة من الإعارة", "مرجع منطقي"),
    "RequestedBy": ("hr_core_db", "Employee", "طالب الطلب (موظف)", "مرجع منطقي + HTTP lookup"),
    "ReceivedBy": ("hr_core_db", "Employee", "مستلم (موظف)", "مرجع منطقي + HTTP lookup"),
    "FileId": ("file_storage_db", "Files", "ملف", "مرجع منطقي + HTTP FileReference"),
    "HandoverFileId": ("file_storage_db", "Files", "ملف تسليم", "مرجع منطقي + HTTP FileReference"),
    "ReturnFileId": ("file_storage_db", "Files", "ملف إعادة", "مرجع منطقي + HTTP FileReference"),
    "PhotoFileId": ("file_storage_db", "Files", "صورة", "مرجع منطقي + HTTP FileReference"),
    "EvidenceFileId": ("file_storage_db", "Files", "ملف دليل", "مرجع منطقي + HTTP FileReference"),
    "ResultFileId": ("file_storage_db", "Files", "ملف نتيجة", "مرجع منطقي + HTTP FileReference"),
    "CertificateFileId": ("file_storage_db", "Files", "شهادة", "مرجع منطقي + HTTP FileReference"),
    "ContentFileId": ("file_storage_db", "Files", "محتوى", "مرجع منطقي + HTTP FileReference"),
    "ReceiptFileId": ("file_storage_db", "Files", "إيصال", "مرجع منطقي + HTTP FileReference"),
    "SealFileId": ("file_storage_db", "Files", "ملف ختم", "مرجع منطقي + HTTP FileReference"),
    "PrintedBy": ("hr_core_db", "Employee", "طابع البطاقة", "مرجع منطقي"),
    "DeliveredTo": ("hr_core_db", "Employee", "مستلم البطاقة", "مرجع منطقي"),
    "LossFileId": ("file_storage_db", "Files", "ملف فقد", "مرجع منطقي + HTTP FileReference"),
    "WorkflowInstanceId": ("hr_core_db", "WorkflowInstance", "سير عمل", "مرجع منطقي + HTTP workflow"),
    "WorkflowDefinitionId": ("hr_core_db", "WorkflowDefinition", "تعريف سير عمل", "مرجع منطقي"),
    "KeycloakUserId": ("keycloak", "User", "مستخدم Keycloak", "HTTP Admin REST"),
    "SubjectEmployeeId": ("hr_core_db", "Employee", "موظف موضوع الطلب", "مرجع منطقي"),
    "AssignedTo": ("hr_core_db", "Employee", "مسند إلى موظف", "مرجع منطقي"),
    "IssuedBy": ("hr_core_db", "Employee", "صادر من موظف", "مرجع منطقي"),
    "CompletedBy": ("hr_core_db", "Employee", "منفّذ الخطوة", "مرجع منطقي"),
    "ApprovedBy": ("hr_core_db", "Employee", "معتمد", "مرجع منطقي"),
    "NominatedBy": ("hr_core_db", "Employee", "مرشِّح للتدريب", "مرجع منطقي"),
    "GradedBy": ("hr_core_db", "Employee", "مقيّم النتيجة", "مرجع منطقي"),
    "PerformedBy": ("hr_core_db", "Employee", "منفّذ المتابعة", "مرجع منطقي"),
    "ReporterProtectedId": ("hr_core_db", "Employee", "هوية المبلّغ المحمية", "مرجع منطقي"),
    "ResponsibleUnitId": ("hr_core_db", "OrgUnit", "وحدة مسؤولة", "مرجع منطقي"),
    "OriginatingUnit": ("hr_core_db", "OrgUnit", "وحدة مصدرة", "مرجع منطقي"),
    "GeneratedBy": ("auth_directory_db", "AuthUser", "مستخدم الدليل", "مرجع منطقي"),
    "UploadedBy": ("auth_directory_db", "AuthUser", "رافع الملف", "مرجع منطقي"),
    "ReportedBy": ("hr_core_db", "Employee", "مبلّغ التذكرة", "مرجع منطقي"),
    "HandledBy": ("hr_core_db", "Employee", "معالج الحادثة", "مرجع منطقي"),
    "DisposeRequestedBy": ("auth_directory_db", "AuthUser", "طالب إتلاف", "مرجع منطقي"),
    "DisposeApprovedBy": ("auth_directory_db", "AuthUser", "معتمد إتلاف", "مرجع منطقي"),
    "ActorUserId": ("auth_directory_db", "AuthUser", "فاعل سلسلة الحيازة", "مرجع منطقي"),
    "Creator": ("hr_core_db", "Employee", "منشئ عنصر الأرشيف", "مرجع منطقي"),
    "ApprovalEvidenceFileId": ("file_storage_db", "Files", "دليل اعتماد إتلاف", "مرجع منطقي بلا RegisterReference"),
    "RelatedEmployeeId": ("hr_core_db", "Employee", "موظف متعلق بالقرار", "مرجع منطقي"),
    "ServiceTransactionId": ("hr_contracts_db", "ServiceTransaction", "معاملة خدمة", "مرجع منطقي + حدث"),
    "SeparationRecordId": ("hr_contracts_db", "SeparationRecord", "سجل الفصل", "مرجع منطقي + حدث ساغا"),
    "RequestedByUserId": ("auth_directory_db", "AuthUser", "طالب التصدير", "مرجع منطقي"),
    "CursorEmployeeId": ("hr_core_db", "Employee", "مؤشر موظف التقسيم", "مرجع منطقي"),
}

# REF columns that look like cross-DB keys but are local/opaque in that snapshot.
EXTERNAL_REF_SKIP: dict[str, frozenset[str]] = {
    "analytics_db": frozenset({"ResultFileId"}),  # LocalExportArtifactStore disk Guid
    "hr_contracts_db": frozenset({"ResultFileId"}),
    "hr_core_db": frozenset({"ResultFileId"}),
    "payroll_db": frozenset({"ResultFileId"}),  # LocalExportArtifactStore disk Guid
    "archive_db": frozenset({"ResultFileId", "ApprovedBy", "CompletedBy"}),  # AuthUser session, not Employee
}

# Same-database REF columns that can be linked when target entity exists but HasOne is absent.
INTERNAL_REF_SUFFIX_TARGETS: dict[str, list[str]] = {
    "EmployeeId": ["Employee"],
    "OrgUnitId": ["OrgUnit"],
    "PrimaryOrgUnitId": ["OrgUnit"],
    "ParentId": ["OrgUnit", "OrgUnitSnapshot"],
    "WorkflowInstanceId": ["WorkflowInstance"],
    "WorkflowDefinitionId": ["WorkflowDefinition"],
    "ApprovalRequestId": ["ApprovalRequest"],
    "PolicyTemplateVersionId": ["PolicyTemplateVersion"],
    "PolicyVersionId": ["CodePolicyVersion", "PolicyTemplateVersion"],
    "ServiceTransactionId": ["ServiceTransaction"],
    "MergedIntoEmployeeId": ["Employee"],
    "RelatedEmployeeId": ["Employee"],
    "InventoryItemId": ["InventoryItem"],
    "SupplyOrderId": ["SupplyOrder"],
    "AssetId": ["Asset"],
    "ContractId": ["Contract"],
    "IssuedCardId": ["IssuedCard"],
    "EnrollmentId": ["TrainingEnrollment", "LearningEnrollment"],
    "ComplaintId": ["Complaint"],
    "FindingId": ["AuditFinding"],
    "FileId": ["FileRecord"],
    "LegacySystemId": ["LegacySystemRecord"],
}

CANONICAL_ORDER = [
    "GatewayAuditDbContext",
    "AdminLogisticsDbContext",
    "AuthDirectoryDbContext",
    "ComplianceDbContext",
    "AnalyticsDbContext",
    "FileStorageDbContext",
    "ContractsDbContext",
    "HRCoreDbContext",
    "HealthDbContext",
    "MovementsDbContext",
    "TrainingDbContext",
    "IDCardDbContext",
    "ITInfraDbContext",
    "PayrollDbContext",
    "ArchiveDbContext",
]

SERVICE_LABELS: dict[str, tuple[str, str]] = {
    "GatewayAuditDbContext": ("Gateway Audit", "البوابة والتدقيق"),
    "AdminLogisticsDbContext": ("Admin.Logistics", "اللوجستيات الإدارية"),
    "AuthDirectoryDbContext": ("Auth.Directory", "الدليل والمصادقة"),
    "ComplianceDbContext": ("Compliance", "الامتثال والشكاوى"),
    "AnalyticsDbContext": ("Dev.Analytics", "التحليلات"),
    "FileStorageDbContext": ("File.Storage", "تخزين الملفات"),
    "ContractsDbContext": ("HR.Contracts", "العقود والخدمة"),
    "HRCoreDbContext": ("HR.Core", "النواة والموظفون"),
    "HealthDbContext": ("HR.Health", "الصحة المهنية"),
    "MovementsDbContext": ("HR.Movements", "الحركات والتنقلات"),
    "TrainingDbContext": ("HR.Training", "التدريب والتعلم"),
    "IDCardDbContext": ("IDCard", "الهويات والبطاقات"),
    "ITInfraDbContext": ("IT.Infra", "البنية التقنية"),
    "PayrollDbContext": ("Payroll", "الرواتب والتعويضات"),
    "ArchiveDbContext": ("Records.Archive", "الأرشيف والسجلات"),
}


def balanced_block(text: str, brace_start: int) -> str:
    depth = 0
    for index in range(brace_start, len(text)):
        if text[index] == "{":
            depth += 1
        elif text[index] == "}":
            depth -= 1
            if depth == 0:
                return text[brace_start + 1 : index]
    return text[brace_start + 1 :]


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


@dataclass
class EntityModel:
    fqn: str
    class_name: str
    table: str
    keys: list[str]
    properties: dict[str, str]
    is_infrastructure: bool = False

    def _is_logical_ref(self, name: str) -> bool:
        if name.endswith("Id") or name.endswith("Ids"):
            return True
        if name not in LOGICAL_REF_COLUMNS:
            return False
        typ = (self.properties.get(name) or "").lower()
        return typ in {"uuid", "guid"}

    def display_columns(self) -> list[tuple[str, str, str]]:
        """Return (key_marker, name, type) rows: PK + *Id columns."""
        keys = set(self.keys or {"Id"})
        names = list(self.properties)
        fk_names = [n for n in names if self._is_logical_ref(n)]
        ordered = [n for n in names if n in keys] + [n for n in fk_names if n not in keys]
        if not ordered:
            ordered = ["Id"]
        rows: list[tuple[str, str, str]] = []
        for name in dict.fromkeys(ordered):
            if name in keys:
                marker = "PK"
            elif name in self.foreign_key_fields():
                marker = "FK"
            elif self._is_logical_ref(name):
                marker = "REF"
            else:
                marker = ""
            rows.append((marker, name, self.properties.get(name, "unknown")))
        return rows

    def foreign_key_fields(self) -> set[str]:
        return getattr(self, "_fk_fields", set())

    def set_foreign_key_fields(self, fields: set[str]) -> None:
        self._fk_fields = fields


@dataclass
class RelationModel:
    source_fqn: str
    target_fqn: str
    fields: list[str]


@dataclass
class DatabaseContextModel:
    index: int
    context: str
    service: str
    service_ar: str
    database: str
    context_path: Path
    snapshot_path: Path
    dbsets: list[str] = field(default_factory=list)
    entities: dict[str, EntityModel] = field(default_factory=dict)
    relations: list[RelationModel] = field(default_factory=list)
    has_outbox: bool = False
    lookup_http: bool = False
    workflow_http: bool = False
    file_http: bool = False
    policy_http: bool = False
    code_http: bool = False
    keycloak_http: bool = False
    directory_sync_http: bool = False


def snapshot_entities(text: str) -> tuple[dict[str, EntityModel], list[dict]]:
    entities: dict[str, EntityModel] = {}
    relation_blocks: list[dict] = []
    pattern = re.compile(r'modelBuilder\.Entity\("([^"]+)"\s*,\s*b\s*=>\s*\{')
    for match in pattern.finditer(text):
        fqn = match.group(1)
        block = balanced_block(text, text.find("{", match.start()))
        table_match = re.search(r'b\.ToTable\("([^"]+)"', block)
        has_key = re.search(r"b\.HasKey\(([^)]*)\)", block, flags=re.S)
        keys = quoted_values(has_key.group(1)) if has_key else []
        properties: dict[str, str] = {}
        for prop in re.finditer(r'b\.Property<([^>]+)>\("([^"]+)"\)', block):
            properties[prop.group(2)] = type_name(prop.group(1))
        if table_match:
            table = table_match.group(1)
            class_name = short_name(fqn)
            entities[fqn] = EntityModel(
                fqn=fqn,
                class_name=class_name,
                table=table,
                keys=keys,
                properties=properties,
                is_infrastructure=table in INFRA_TABLE_NAMES or class_name in INFRA_TABLE_NAMES,
            )
        if "b.HasOne(" in block:
            relation_blocks.append({"source": fqn, "block": block})
    return entities, relation_blocks


def relationships(entities: dict[str, EntityModel], blocks: list[dict]) -> list[RelationModel]:
    result: list[RelationModel] = []
    seen: set[tuple[str, str, tuple[str, ...]]] = set()
    fk_by_source: dict[str, set[str]] = {}
    has_one_pattern = re.compile(
        r'b\.HasOne\("([^"]+)"(?:\s*,\s*(?:"[^"]*"|null))?\)\s*(.*?)(?=;|$)',
        flags=re.S,
    )
    for item in blocks:
        block = item["block"]
        for match in has_one_pattern.finditer(block):
            target = match.group(1)
            source = item["source"]
            if target not in entities or source not in entities:
                continue
            foreign = re.search(r"\.HasForeignKey\(([^)]*)\)", match.group(2), flags=re.S)
            fields = tuple(quoted_values(foreign.group(1))) if foreign else tuple()
            key = (source, target, fields)
            if key in seen:
                continue
            seen.add(key)
            result.append(RelationModel(source_fqn=source, target_fqn=target, fields=list(fields)))
            fk_by_source.setdefault(source, set()).update(fields)
    for fqn, fields in fk_by_source.items():
        if fqn in entities:
            entities[fqn].set_foreign_key_fields(fields)
    return result


def discover_db_contexts() -> list[Path]:
    paths: list[Path] = []
    for path in SRC.rglob("*DbContext.cs"):
        text = path.read_text(encoding="utf-8-sig", errors="ignore")
        if re.search(r"\babstract\s+class\s+\w+DbContext\b", text):
            continue
        if re.search(r"class\s+(\w+DbContext)\b", text):
            paths.append(path)
    return sorted(paths, key=lambda p: context_sort_key(p))


def context_sort_key(path: Path) -> tuple[int, str]:
    text = path.read_text(encoding="utf-8-sig", errors="ignore")
    match = re.search(r"class\s+(\w+DbContext)\b", text)
    name = match.group(1) if match else path.name
    if name in CANONICAL_ORDER:
        return (CANONICAL_ORDER.index(name), name)
    return (999, name)


def snapshot_for_context(context_path: Path, context_name: str) -> Path | None:
    for candidate in context_path.parent.rglob("*ModelSnapshot.cs"):
        text = candidate.read_text(encoding="utf-8-sig", errors="ignore")
        if f"typeof({context_name})" in text:
            return candidate
    service_root = context_path.parents[3] if "Gateway" not in str(context_path) else context_path.parents[2]
    for candidate in service_root.rglob("*ModelSnapshot.cs"):
        text = candidate.read_text(encoding="utf-8-sig", errors="ignore")
        if f"typeof({context_name})" in text:
            return candidate
    return None


def database_name_for(context_path: Path) -> str:
    service_roots: list[Path] = []
    for parent in context_path.parents:
        if parent.name.endswith(".Web") or parent.name == "HRManagement.Gateway":
            service_roots.append(parent)
        if parent == SRC:
            break
    search_paths = service_roots + list(context_path.parents)
    for root in search_paths:
        candidates = list(root.glob("appsettings.json"))
        if root.name != "HRManagement.Gateway":
            candidates.extend(root.parent.glob("*.Web/appsettings.json") if root.parent != root else [])
        for appsettings in candidates:
            try:
                payload = json.loads(appsettings.read_text(encoding="utf-8-sig"))
            except (OSError, json.JSONDecodeError):
                continue
            for value in payload.get("ConnectionStrings", {}).values():
                match = re.search(r"(?:^|;)Database=([^;]+)", str(value), re.I)
                if match:
                    return match.group(1)
    # Walk service tree for any sibling Web project
    if "Services" in context_path.parts:
        idx = context_path.parts.index("Services")
        service_folder = Path(*context_path.parts[: idx + 2])
        for appsettings in service_folder.rglob("appsettings.json"):
            try:
                payload = json.loads(appsettings.read_text(encoding="utf-8-sig"))
            except (OSError, json.JSONDecodeError):
                continue
            for value in payload.get("ConnectionStrings", {}).values():
                match = re.search(r"(?:^|;)Database=([^;]+)", str(value), re.I)
                if match:
                    return match.group(1)
    if "Gateway" in context_path.parts:
        gateway_appsettings = SRC / "Gateway" / "HRManagement.Gateway" / "appsettings.json"
        if gateway_appsettings.exists():
            payload = json.loads(gateway_appsettings.read_text(encoding="utf-8-sig"))
            for value in payload.get("ConnectionStrings", {}).values():
                match = re.search(r"(?:^|;)Database=([^;]+)", str(value), re.I)
                if match:
                    return match.group(1)
    return context_path.stem.replace("DbContext", "").lower() + "_db"


def parse_dbsets(context_path: Path) -> list[str]:
    text = context_path.read_text(encoding="utf-8-sig", errors="ignore")
    sets: list[str] = []
    for match in re.finditer(r"DbSet<\s*(.+?)\s*>\s+(\w+)\s*=>", text, flags=re.S):
        entity = re.sub(r"\s+", " ", match.group(1)).strip()
        sets.append(short_name(entity))
    return sets


def scan_outbox_contexts() -> set[str]:
    contexts: set[str] = set()
    for program in SRC.rglob("Program.cs"):
        text = program.read_text(encoding="utf-8-sig", errors="ignore")
        for match in re.finditer(r"AddPostgresOutbox<(\w+)>", text):
            contexts.add(match.group(1))
    return contexts


def empty_http_flags() -> dict[str, bool]:
    return {
        "lookup": False,
        "workflow": False,
        "file": False,
        "policy": False,
        "code": False,
        "keycloak": False,
        "directory_sync": False,
    }


def scan_http_integrations() -> dict[str, dict[str, bool]]:
    result: dict[str, dict[str, bool]] = {}
    scan_files = list(SRC.rglob("DependencyInjection.cs")) + list(SRC.rglob("Program.cs"))
    for path in scan_files:
        service_key = infer_service_key_from_path(path)
        if not service_key:
            continue
        text = path.read_text(encoding="utf-8-sig", errors="ignore")
        entry = result.setdefault(service_key, empty_http_flags())
        if "AddHrCoreLookupClient" in text or "IHrCoreLookupClient" in text:
            entry["lookup"] = True
        if "WorkflowTriggerService" in text:
            entry["workflow"] = True
        if "AddHrFileReferenceClient" in text or "IFileReferenceClient" in text:
            entry["file"] = True
        if "AddHrPolicyEnforceClient" in text or "IPolicyEnforceClient" in text:
            entry["policy"] = True
        if "AddHrCodeGeneration" in text or "ICodeGenerationClient" in text:
            entry["code"] = True
        if "IKeycloakUserProvisioningService" in text or "KeycloakUserProvisioningService" in text:
            entry["keycloak"] = True
        if "HttpDirectoryEntrySyncClient" in text or "api/internal/directory/sync-employee" in text:
            entry["directory_sync"] = True
    return result


def infer_service_key_from_path(path: Path) -> str | None:
    parts = path.parts
    if "Gateway" in parts:
        return "GatewayAuditDbContext"
    if "Services" in parts:
        idx = parts.index("Services")
        if idx + 1 < len(parts):
            folder = parts[idx + 1]
            mapping = {
                "AdminLogistics": "AdminLogisticsDbContext",
                "AuthDirectory": "AuthDirectoryDbContext",
                "Compliance": "ComplianceDbContext",
                "DevAnalytics": "AnalyticsDbContext",
                "FileStorage": "FileStorageDbContext",
                "HRContracts": "ContractsDbContext",
                "HRCore": "HRCoreDbContext",
                "HRHealth": "HealthDbContext",
                "HRMovements": "MovementsDbContext",
                "HRTraining": "TrainingDbContext",
                "IDCard": "IDCardDbContext",
                "ITInfrastructure": "ITInfraDbContext",
                "Payroll": "PayrollDbContext",
                "RecordsArchive": "ArchiveDbContext",
            }
            return mapping.get(folder)
    return None


@dataclass
class ConsumerBinding:
    event_name: str
    context: str
    database: str
    consumer_file: str


def scan_consumers(models: list[DatabaseContextModel]) -> list[ConsumerBinding]:
    context_by_folder: dict[str, DatabaseContextModel] = {}
    for model in models:
        folder = model.context_path.parts
        if "Gateway" in folder:
            context_by_folder["Gateway"] = model
        elif "Services" in folder:
            idx = folder.index("Services")
            if idx + 1 < len(folder):
                context_by_folder[folder[idx + 1]] = model

    folder_map = {
        "AdminLogistics": "AdminLogisticsDbContext",
        "AuthDirectory": "AuthDirectoryDbContext",
        "Compliance": "ComplianceDbContext",
        "DevAnalytics": "AnalyticsDbContext",
        "FileStorage": "FileStorageDbContext",
        "HRContracts": "ContractsDbContext",
        "HRCore": "HRCoreDbContext",
        "HRHealth": "HealthDbContext",
        "HRMovements": "MovementsDbContext",
        "HRTraining": "TrainingDbContext",
        "IDCard": "IDCardDbContext",
        "ITInfrastructure": "ITInfraDbContext",
        "Payroll": "PayrollDbContext",
        "RecordsArchive": "ArchiveDbContext",
        "Gateway": "GatewayAuditDbContext",
    }
    model_by_context = {m.context: m for m in models}
    party_ack_bases = {
        "SeparationPartyAckConsumerBase": "IExecuteSeparationParty",
        "TransferPartyAckConsumerBase": "IExecuteTransferParty",
        "HirePartyAckConsumerBase": "IExecuteHireParty",
        "SecondmentPartyAckConsumerBase": "IExecuteSecondmentParty",
    }
    bindings: list[ConsumerBinding] = []
    consumer_files: list[Path] = []
    seen_paths: set[Path] = set()
    for pattern in ("*Consumer*.cs", "*ExportJob*.cs"):
        for path in SRC.rglob(pattern):
            if path in seen_paths:
                continue
            seen_paths.add(path)
            consumer_files.append(path)
    for consumer in consumer_files:
        text = consumer.read_text(encoding="utf-8-sig", errors="ignore")
        if "IConsumer<" not in text and not any(base in text for base in party_ack_bases):
            continue
        parts = consumer.parts
        service_folder = None
        if "Gateway" in parts:
            service_folder = "Gateway"
        elif "Services" in parts:
            idx = parts.index("Services")
            if idx + 1 < len(parts):
                service_folder = parts[idx + 1]
        if not service_folder:
            continue
        context_name = folder_map.get(service_folder)
        if not context_name or context_name not in model_by_context:
            continue
        db_model = model_by_context[context_name]
        for match in re.finditer(r"IConsumer<(I\w+)>", text):
            bindings.append(
                ConsumerBinding(
                    event_name=match.group(1),
                    context=context_name,
                    database=db_model.database,
                    consumer_file=str(consumer.relative_to(ROOT)).replace("\\", "/"),
                )
            )
        for base_name, event_name in party_ack_bases.items():
            if base_name in text:
                bindings.append(
                    ConsumerBinding(
                        event_name=event_name,
                        context=context_name,
                        database=db_model.database,
                        consumer_file=str(consumer.relative_to(ROOT)).replace("\\", "/"),
                    )
                )
    return bindings


def infer_internal_logical_relations(model: DatabaseContextModel) -> list[RelationModel]:
    """Link REF columns to entities in the same snapshot when EF HasOne is missing."""
    by_class = {e.class_name: fqn for fqn, e in model.entities.items()}
    result: list[RelationModel] = []
    seen: set[tuple[str, str, str]] = set()
    fk_fields = {
        (r.source_fqn, field)
        for r in model.relations
        for field in r.fields
    }
    for source_fqn, entity in model.entities.items():
        for marker, name, _ in entity.display_columns():
            if marker != "REF":
                continue
            if (source_fqn, name) in fk_fields:
                continue
            targets = INTERNAL_REF_SUFFIX_TARGETS.get(name, [])
            if name.endswith("Id") and name not in INTERNAL_REF_SUFFIX_TARGETS:
                guess = name[:-2]
                if guess.endswith("ie"):
                    guess = guess[:-1] + "y"  # rough plural
                targets = targets + [guess]
            for class_name in targets:
                target_fqn = by_class.get(class_name)
                if not target_fqn or target_fqn == source_fqn:
                    continue
                key = (source_fqn, target_fqn, name)
                if key in seen:
                    break
                seen.add(key)
                result.append(RelationModel(source_fqn=source_fqn, target_fqn=target_fqn, fields=[name]))
                break
    return result


def external_ref_usage(model: DatabaseContextModel) -> dict[str, list[str]]:
    """Map cross-service REF column -> tables that use it."""
    usage: dict[str, list[str]] = {}
    local_db = model.database
    skip = EXTERNAL_REF_SKIP.get(local_db, frozenset())
    for entity in model.entities.values():
        for marker, name, _ in entity.display_columns():
            if marker != "REF" or name not in EXTERNAL_REF_TARGETS:
                continue
            if name in skip:
                continue
            target_db, _, _, _ = EXTERNAL_REF_TARGETS[name]
            if target_db == local_db:
                continue
            usage.setdefault(name, [])
            if entity.table not in usage[name]:
                usage[name].append(entity.table)
    return usage


def load_all_database_models() -> list[DatabaseContextModel]:
    outbox_contexts = scan_outbox_contexts()
    http_integrations = scan_http_integrations()
    models: list[DatabaseContextModel] = []
    for index, context_path in enumerate(discover_db_contexts(), 1):
        text = context_path.read_text(encoding="utf-8-sig", errors="ignore")
        match = re.search(r"class\s+(\w+DbContext)\b", text)
        if not match:
            continue
        context_name = match.group(1)
        snapshot_path = snapshot_for_context(context_path, context_name)
        if snapshot_path is None:
            continue
        snapshot_text = snapshot_path.read_text(encoding="utf-8-sig", errors="ignore")
        entities, blocks = snapshot_entities(snapshot_text)
        relations = relationships(entities, blocks)
        service, service_ar = SERVICE_LABELS.get(context_name, (context_name, context_name))
        service_key = infer_service_key_from_path(context_path) or context_name
        http = http_integrations.get(service_key, {})
        models.append(
            DatabaseContextModel(
                index=index,
                context=context_name,
                service=service,
                service_ar=service_ar,
                database=database_name_for(context_path),
                context_path=context_path,
                snapshot_path=snapshot_path,
                dbsets=parse_dbsets(context_path),
                entities=entities,
                relations=relations,
                has_outbox=context_name in outbox_contexts,
                lookup_http=http.get("lookup", False),
                workflow_http=http.get("workflow", False),
                file_http=http.get("file", False),
                policy_http=http.get("policy", False),
                code_http=http.get("code", False),
                keycloak_http=http.get("keycloak", False),
                directory_sync_http=http.get("directory_sync", False),
            )
        )
    models.sort(key=lambda m: CANONICAL_ORDER.index(m.context) if m.context in CANONICAL_ORDER else 999)
    for i, model in enumerate(models, 1):
        model.index = i
    return models
