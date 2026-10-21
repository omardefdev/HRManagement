"""Arabic labels for ERD table and column names."""

from __future__ import annotations

import re
from html import escape

from database_schema_parser import EXTERNAL_REF_TARGETS


def split_pascal(name: str) -> list[str]:
    spaced = re.sub(r"([a-z0-9])([A-Z])", r"\1 \2", name)
    spaced = re.sub(r"([A-Z]+)([A-Z][a-z])", r"\1 \2", spaced)
    return [part for part in spaced.replace("_", " ").split() if part]


# مكونات شائعة (أطول المفاتيح أولاً عند المطابقة الجشعة).
WORD_AR: dict[str, str] = {
    "Integration": "تكامل",
    "Message": "رسالة",
    "Messages": "رسائل",
    "Receipt": "إيصال",
    "Receipts": "إيصالات",
    "Reference": "مرجع",
    "References": "مراجع",
    "Outbox": "صندوق صادر",
    "Inbox": "صندوق وارد",
    "Idempotency": "منع التكرار",
    "Workflow": "سير عمل",
    "Definition": "تعريف",
    "Definitions": "تعريفات",
    "Instance": "مثيل",
    "Instances": "مثيلات",
    "Approval": "اعتماد",
    "Approvals": "اعتمادات",
    "Request": "طلب",
    "Requests": "طلبات",
    "Employee": "موظف",
    "Employees": "موظفون",
    "OrgUnit": "وحدة تنظيمية",
    "OrgUnits": "وحدات تنظيمية",
    "Organization": "تنظيم",
    "Tenant": "جهة",
    "Tenants": "جهات",
    "Contract": "عقد",
    "Contracts": "عقود",
    "Candidate": "مرشح",
    "Candidates": "مرشحون",
    "Hire": "تعيين",
    "Separation": "فصل",
    "Leave": "إجازة",
    "Advance": "سلفة",
    "Transfer": "نقل",
    "Secondment": "ندب",
    "Loan": "إعارة",
    "Assignment": "تعيين",
    "Assignments": "تعيينات",
    "Asset": "أصل",
    "Assets": "أصول",
    "Inventory": "مخزون",
    "Supply": "توريد",
    "Order": "أمر",
    "Orders": "أوامر",
    "Line": "بند",
    "Lines": "بنود",
    "Receipt": "استلام",
    "Loss": "فقد",
    "Payroll": "رواتب",
    "Payment": "دفع",
    "Payments": "مدفوعات",
    "Batch": "دفعة",
    "Salary": "راتب",
    "Run": "تشغيل",
    "Configuration": "تهيئة",
    "Snapshot": "لقطة",
    "Partition": "جزء",
    "Hold": "حجز",
    "Arrears": "متأخرات",
    "Fraud": "احتيال",
    "Alert": "تنبيه",
    "Grade": "درجة",
    "Profile": "ملف",
    "Medical": "طبي",
    "Health": "صحة",
    "Training": "تدريب",
    "Learning": "تعلم",
    "Enrollment": "تسجيل",
    "Mission": "بعثة",
    "Course": "دورة",
    "Certificate": "شهادة",
    "Card": "بطاقة",
    "Issued": "صادر",
    "Design": "تصميم",
    "Serial": "تسلسل",
    "Complaint": "شكوى",
    "Complaints": "شكاوى",
    "Disciplinary": "تأديبي",
    "Action": "إجراء",
    "Actions": "إجراءات",
    "Investigation": "تحقيق",
    "Finding": "نتيجة",
    "Legal": "قانوني",
    "Judgment": "حكم",
    "Analytics": "تحليلات",
    "Event": "حدث",
    "Events": "أحداث",
    "Export": "تصدير",
    "Job": "مهمة",
    "File": "ملف",
    "Files": "ملفات",
    "Scan": "فحص",
    "Grant": "منحة",
    "Access": "وصول",
    "Audit": "تدقيق",
    "Log": "سجل",
    "Logs": "سجلات",
    "Archive": "أرشيف",
    "Item": "عنصر",
    "Items": "عناصر",
    "Checklist": "قائمة",
    "Disposal": "إتلاف",
    "Retention": "احتفاظ",
    "Custody": "حيازة",
    "Digital": "رقمي",
    "Record": "سجل",
    "Records": "سجلات",
    "Legacy": "قديم",
    "System": "نظام",
    "Ticket": "تذكرة",
    "Device": "جهاز",
    "Permission": "صلاحية",
    "Permissions": "صلاحيات",
    "Directory": "دليل",
    "Auth": "مصادقة",
    "User": "مستخدم",
    "Users": "مستخدمون",
    "Role": "دور",
    "Roles": "أدوار",
    "Policy": "سياسة",
    "Policies": "سياسات",
    "Template": "قالب",
    "Version": "إصدار",
    "Rule": "قاعدة",
    "Rules": "قواعد",
    "Code": "رمز",
    "Issuance": "إصدار",
    "Sequence": "تسلسل",
    "Counter": "عداد",
    "Enforcement": "إنفاذ",
    "Ledger": "دفتر",
    "Attendance": "حضور",
    "Daily": "يومي",
    "Shift": "وردية",
    "Schedule": "جدول",
    "Career": "مسار",
    "Path": "مسار",
    "Succession": "تعاقب",
    "Talent": "كفاءات",
    "Marketplace": "سوق",
    "Citizen": "مواطن",
    "Application": "طلب",
    "Applications": "طلبات",
    "Recruitment": "توظيف",
    "Branch": "فرع",
    "Diwan": "ديوان",
    "Correspondence": "مراسلة",
    "Gateway": "بوابة",
    "Security": "أمن",
    "Evidence": "دليل",
    "Artifact": "أثر",
    "Artifacts": "آثار",
    "Pack": "حزمة",
    "Chain": "سلسلة",
    "State": "حالة",
    "Legal": "قانوني",
    "Hold": "حجز",
    "Reconcile": "مصالحة",
    "Exception": "استثناء",
    "Purge": "تنظيف",
    "Work": "عمل",
    "Metadata": "بيانات وصفية",
    "Content": "محتوى",
    "Hash": "تجزئة",
    "Status": "حالة",
    "Type": "نوع",
    "Name": "اسم",
    "Title": "عنوان",
    "Description": "وصف",
    "Reason": "سبب",
    "Note": "ملاحظة",
    "Notes": "ملاحظات",
    "Comment": "تعليق",
    "Comments": "تعليقات",
    "Step": "خطوة",
    "Stage": "مرحلة",
    "Phase": "طور",
    "Result": "نتيجة",
    "Outcome": "مخرج",
    "Decision": "قرار",
    "Recommendation": "توصية",
    "Transaction": "معاملة",
    "Service": "خدمة",
    "Checklist": "قائمة",
    "Party": "طرف",
    "Ack": "إقرار",
    "Cursor": "مؤشر",
    "Primary": "أساسي",
    "Scope": "نطاق",
    "From": "مصدر",
    "To": "مقصد",
    "Lending": "مُعير",
    "Borrowing": "مستفيد",
    "Related": "متعلق",
    "Merged": "مدموج",
    "Into": "إلى",
    "Subject": "موضوع",
    "Responsible": "مسؤول",
    "Originating": "مُصدِر",
    "Reporter": "مبلّغ",
    "Protected": "محمي",
    "Performed": "منفّذ",
    "Nominated": "مرشّح",
    "Graded": "مقيّم",
    "Printed": "مطبوع",
    "Delivered": "مُسلّم",
    "Handled": "معالج",
    "Generated": "مُولَّد",
    "Uploaded": "مرفوع",
    "Requested": "طالب",
    "Received": "مستلم",
    "Approved": "معتمد",
    "Completed": "مكتمل",
    "Assigned": "مسند",
    "Issued": "صادر",
    "Actor": "فاعل",
    "Creator": "منشئ",
    "Dispose": "إتلاف",
    "Approval": "اعتماد",
    "Handover": "تسليم",
    "Return": "إرجاع",
    "Photo": "صورة",
    "Seal": "ختم",
    "Keycloak": "Keycloak",
    "Shadow": "ظل",
    "Operation": "عملية",
    "Destruction": "إتلاف",
    "Media": "وسائط",
    "Check": "فحص",
    "Input": "مدخلات",
    "Output": "مخرجات",
    "Lineage": "سلسلة",
    "Holder": "حائز",
    "Key": "مفتاح",
    "Local": "محلي",
    "National": "وطني",
    "Encrypted": "مشفر",
    "Duplicate": "مكرر",
    "Case": "حالة",
    "Cases": "حالات",
    "Quarantine": "حجر",
    "Lifecycle": "دورة حياة",
    "Flags": "أعلام",
    "Internal": "داخلي",
    "External": "خارجي",
    "Lookup": "استعلام",
    "Trigger": "مُشغّل",
    "Simulation": "محاكاة",
    "Evaluation": "تقييم",
    "Binding": "ربط",
    "Platform": "منصة",
    "Governance": "حوكمة",
    "Master": "رئيسي",
    "Data": "بيانات",
    "Seed": "بذرة",
    "Demo": "تجريبي",
    "Sync": "مزامنة",
    "Model": "نموذج",
    "Contact": "اتصال",
    "Info": "معلومات",
    "Employment": "توظيف",
    "Probation": "تجربة",
    "Commencement": "مباشرة",
    "ApprovedCV": "سيرة معتمدة",
    "CV": "سيرة",
    "Org": "تنظيم",
    "Unit": "وحدة",
    "Parent": "أب",
    "Child": "فرعي",
    "Version": "إصدار",
    "Snapshot": "لقطة",
    "Materialized": "مُجمّع",
    "Path": "مسار",
    "Attendance": "حضور",
    "Work": "عمل",
    "Citizen": "مواطن",
    "Portal": "بوابة",
    "Careers": "مسارات",
    "Internal": "داخلي",
    "Marketplace": "سوق",
    "Succession": "تعاقب",
    "Scheduling": "جدولة",
    "Shift": "وردية",
    "Daily": "يومي",
    "Contact": "اتصال",
    "National": "وطني",
    "Id": "معرّف",
    "Ids": "معرّفات",
}

TABLE_OVERRIDES: dict[str, str] = {
    "Employee": "الموظف",
    "Employees": "الموظفون",
    "OrgUnit": "الوحدة التنظيمية",
    "OrgUnits": "الوحدات التنظيمية",
    "Tenant": "الجهة",
    "Tenants": "الجهات",
    "FileRecord": "سجل الملف",
    "Files": "الملفات",
    "OutboxMessage": "رسالة الصندوق الصادر",
    "InboxState": "حالة الصندوق الوارد",
    "IntegrationMessageReceipt": "إيصال رسالة التكامل",
    "IntegrationMessageReceipts": "إيصالات رسائل التكامل",
    "AuditLog": "سجل التدقيق",
    "AuditLogs": "سجلات التدقيق",
    "EntityAuditLog": "سجل تدقيق الكيان",
    "GatewayAuditEntry": "قيد تدقيق البوابة",
    "SecurityAuditEvent": "حدث تدقيق أمني",
    "AuthUser": "مستخدم الدليل",
    "DirectoryEntry": "سجل الدليل",
    "ArchiveItem": "عنصر أرشيف",
    "AnalyticsEvent": "حدث تحليلات",
    "PayrollRun": "تشغيل الرواتب",
    "SeparationRecord": "سجل إنهاء الخدمة",
    "ServiceTransaction": "معاملة الخدمة",
    "WorkflowInstance": "مثيل سير العمل",
    "WorkflowDefinition": "تعريف سير العمل",
    "CodePolicyVersion": "إصدار سياسة الرمز",
    "PolicyTemplateVersion": "إصدار قالب السياسة",
    "LegacySystemRecord": "نظام قديم",
}

COLUMN_OVERRIDES: dict[str, str] = {
    "Id": "المعرّف",
    "TenantId": "معرّف الجهة",
    "CorrelationId": "معرّف الارتباط",
    "MessageId": "معرّف الرسالة",
    "CreatedAt": "تاريخ الإنشاء",
    "UpdatedAt": "تاريخ التحديث",
    "CreatedBy": "أنشأه",
    "UpdatedBy": "حدّثه",
    "DeletedAt": "تاريخ الحذف",
    "IsDeleted": "محذوف",
    "IsCancelled": "ملغى",
    "IsActive": "نشط",
    "IsRevoked": "ملغى الصلاحية",
    "Status": "الحالة",
    "Type": "النوع",
    "Name": "الاسم",
    "Title": "العنوان",
    "Description": "الوصف",
    "Code": "الرمز",
    "SerialNumber": "الرقم التسلسلي",
    "HolderKey": "مفتاح الحائز",
    "OccurredAt": "وقت الحدوث",
    "ProcessedAt": "وقت المعالجة",
    "ExpiresAtUtc": "انتهاء الصلاحية",
    "StartedAt": "وقت البدء",
    "CompletedAt": "وقت الإكمال",
    "ApprovedAt": "وقت الاعتماد",
    "IssuedAt": "وقت الإصدار",
    "EffectiveFrom": "ساري من",
    "EffectiveTo": "ساري إلى",
    "StartDate": "تاريخ البداية",
    "EndDate": "تاريخ النهاية",
    "BaseSalary": "الراتب الأساسي",
    "Amount": "المبلغ",
    "Quantity": "الكمية",
    "Reason": "السبب",
    "Notes": "ملاحظات",
    "Fingerprint": "بصمة",
    "PayloadJson": "حمولة JSON",
    "EventName": "اسم الحدث",
    "ReferenceKey": "مفتاح المرجع",
    "ContentHash": "تجزئة المحتوى",
    "StoragePath": "مسار التخزين",
    "MimeType": "نوع MIME",
    "FileSizeBytes": "حجم الملف",
    "NationalIdHash": "تجزئة الهوية",
    "EmployeeNumber": "رقم الموظف",
    "Email": "البريد",
    "Phone": "الهاتف",
    "UserSub": "معرّف المستخدم",
    "ActorSub": "معرّف الفاعل",
    "Path": "المسار",
    "Sequence": "التسلسل",
    "RowVersion": "إصدار الصف",
    "ConcurrencyToken": "رمز التزامن",
}

# استخراج أسماء عربية من مراجع EXTERNAL_REF
for col, (_db, _entity, desc, _mechanism) in EXTERNAL_REF_TARGETS.items():
    if col.endswith("Id"):
        COLUMN_OVERRIDES.setdefault(col, f"معرّف {desc.split('(')[0].strip()}")
    elif col.endswith("By") or col in {"Creator", "AssignedTo", "DeliveredTo", "OriginatingUnit"}:
        COLUMN_OVERRIDES.setdefault(col, desc.split("(")[0].strip())


def _greedy_translate_tokens(tokens: list[str]) -> list[str]:
    parts: list[str] = []
    index = 0
    while index < len(tokens):
        matched = False
        for size in range(len(tokens) - index, 0, -1):
            chunk = "".join(tokens[index : index + size])
            if chunk in WORD_AR:
                parts.append(WORD_AR[chunk])
                index += size
                matched = True
                break
        if not matched:
            token = tokens[index]
            parts.append(WORD_AR.get(token, token))
            index += 1
    return parts


def translate_entity(name: str) -> str:
    if not name:
        return name
    if name in TABLE_OVERRIDES:
        return TABLE_OVERRIDES[name]
    tokens = split_pascal(name)
    if not tokens:
        return name
    parts = _greedy_translate_tokens(tokens)
    if all(part == tokens[i] for i, part in enumerate(parts)):
        # nothing translated — keep readable fallback
        return " ".join(reversed(parts)) if len(parts) > 1 else parts[0]
    return " ".join(reversed(parts))


def translate_table(class_name: str, table_name: str | None = None) -> str:
    label = translate_entity(class_name)
    if table_name and table_name not in {class_name, class_name + "s"}:
        snake = table_name.replace("_", " ")
        if snake != class_name.lower() and not label:
            return snake
    return label


def translate_column(name: str) -> str:
    if name in COLUMN_OVERRIDES:
        return COLUMN_OVERRIDES[name]
    if name.endswith("At") and name not in COLUMN_OVERRIDES:
        base = name[:-2]
        return f"تاريخ {translate_entity(base)}"
    if name.endswith("By") and name not in COLUMN_OVERRIDES:
        base = name[:-2]
        return translate_entity(base)
    if name.endswith("Id") and name != "Id":
        base = name[:-2]
        return f"معرّف {translate_entity(base)}"
    if name.endswith("Ids"):
        base = name[:-3]
        return f"معرّفات {translate_entity(base)}"
    return translate_entity(name)


def bilingual_html(
    english: str,
    arabic: str,
    *,
    english_size: int = 11,
    arabic_color: str = "#6B7280",
) -> str:
    if not arabic or arabic.strip().lower() == english.strip().lower():
        return escape(english)
    en = escape(english)
    ar = escape(arabic.strip())
    return (
        f'<div style="text-align:left;line-height:1.15;">'
        f'<div style="font-size:{english_size}px;">{en}</div>'
        f'<div style="font-size:9px;color:{arabic_color};font-family:Segoe UI;">{ar}</div>'
        f"</div>"
    )
