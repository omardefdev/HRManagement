"""Generate editable PlantUML sources and rendered figures for the thesis.

The source files remain under docs/thesis_diagrams so the graduation team can
revise every UML/DFD diagram after this delivery. PNG copies are generated for
insertion into the Word document.
"""

from __future__ import annotations

import subprocess
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
DIAGRAM_DIR = ROOT / "docs" / "thesis_diagrams"
RENDER_DIR = DIAGRAM_DIR / "rendered"
JAR = ROOT / "tmp" / "plantuml" / "plantuml-mit-1.2026.6.jar"

HEADER = """@startuml
!theme plain
skinparam backgroundColor #FFFFFF
skinparam defaultFontName Arial
skinparam shadowing false
skinparam roundcorner 8
skinparam linetype ortho
skinparam dpi 170
skinparam ArrowColor #24465A
skinparam ActorBorderColor #0F6B73
skinparam ActorBackgroundColor #E6F3F3
skinparam UsecaseBorderColor #0F6B73
skinparam UsecaseBackgroundColor #F4FAFA
skinparam RectangleBorderColor #24465A
skinparam RectangleBackgroundColor #F8FBFC
skinparam ComponentBorderColor #24465A
skinparam ComponentBackgroundColor #F4F7F8
skinparam ClassBorderColor #24465A
skinparam ClassBackgroundColor #F8FBFC
skinparam SequenceLifeLineBorderColor #78909C
skinparam SequenceParticipantBorderColor #24465A
skinparam SequenceParticipantBackgroundColor #F4F7F8
"""


def uml(body: str) -> str:
    # Triple-quoted Python strings turn the intended PlantUML line break token
    # inside labels into a physical newline. Restore it only while parsing a
    # quoted PlantUML label; structural source lines remain untouched.
    quoted = False
    normalized: list[str] = []
    for char in body.strip():
        if char == '"':
            quoted = not quoted
        if char == "\n" and quoted:
            normalized.append("\\\\n")
        else:
            normalized.append(char)
    return f"{HEADER}\n{''.join(normalized)}\n@enduml\n"


SOURCES: dict[str, str] = {
    "01_dfd_context": uml(
        """
title مخطط سياق النظام (DFD Context) - HRManagement
actor "المواطن" as Citizen
actor "الموظف" as Employee
actor "موظف الفرع" as BranchHr
actor "مدير الفرع" as BranchManager
actor "الإدارة المركزية" as Central
actor "مسؤول الرواتب" as Payroll
actor "مدير النظام" as Admin
rectangle "نظام إدارة الموارد البشرية\nHRManagement" as System
Citizen --> System : طلب توظيف وتتبع طلب
Employee --> System : خدمات ذاتية وتعلّم
BranchHr --> System : معاملات موظفي الفرع
BranchManager --> System : اعتماد ضمن نطاق الفرع
Central --> System : قرارات وتقارير شاملة
Payroll --> System : تشغيل دورات الرواتب
Admin --> System : أدوار ومراقبة تشغيلية
System --> Citizen : رقم متابعة وحالة الطلب
System --> Employee : إشعارات وسجل تعلم
System --> Central : مؤشرات وتقارير المؤسسة
"""
    ),
    "02_usecase_master": uml(
        """
title مخطط حالات الاستخدام الشامل
left to right direction
actor "الإدارة المركزية" as Central
actor "مدير الفرع" as BranchManager
actor "موظف الموارد البشرية" as HrOfficer
actor "الموظف" as Employee
actor "المواطن" as Citizen
actor "مسؤول النظام" as Admin
rectangle "HRManagement" {
  usecase "إدارة الهيكل\nوالنطاق التنظيمي" as UCOrg
  usecase "إدارة الموظف\nودورة حياته" as UCEmp
  usecase "التوظيف والتعيين" as UCHire
  usecase "العقود والإجازات\nوالفصل" as UCContracts
  usecase "الحركات والتدريب\nوالصحة" as UCPeople
  usecase "الرواتب والهوية\nوالأرشفة" as UCOps
  usecase "الخدمة الذاتية\nوالتوظيف العام" as UCPortal
  usecase "المراقبة والتدقيق\nوالصلاحيات" as UCGov
}
Central --> UCOrg
Central --> UCEmp
Central --> UCHire
Central --> UCContracts
Central --> UCPeople
Central --> UCOps
Central --> UCGov
BranchManager --> UCEmp
BranchManager --> UCContracts
BranchManager --> UCPeople
HrOfficer --> UCEmp
HrOfficer --> UCHire
HrOfficer --> UCContracts
Employee --> UCPortal
Citizen --> UCPortal
Admin --> UCGov
"""
    ),
    "03_usecase_organization": uml(
        """
title حالات استخدام الهيكل التنظيمي والمركز والفروع
left to right direction
actor "مدير النظام" as Admin
actor "الإدارة المركزية" as Central
actor "مدير الفرع" as Branch
rectangle "HR.Core / نطاق المؤسسة" {
  usecase "إنشاء وحدة تنظيمية" as UC1
  usecase "ربط وحدة بوالدها" as UC2
  usecase "تعيين وحدة الموظف الأساسية" as UC3
  usecase "عرض المؤسسة كاملة" as UC4
  usecase "عرض الفرع وتفرعاته" as UC5
  usecase "منع عرض فرع شقيق" as UC6
  usecase "إدارة claim\nscope_org_unit_id" as UC7
}
Admin --> UC1
Admin --> UC2
Admin --> UC7
Central --> UC3
Central --> UC4
Branch --> UC5
UC5 .> UC6 : <<include>>
UC5 .> UC7 : <<include>>
"""
    ),
    "04_usecase_hire_contracts": uml(
        """
title حالات استخدام التوظيف والعقود والخدمة
left to right direction
actor "مواطن" as Citizen
actor "موظف تعاقد" as ContractOps
actor "معتمد مركزي" as Approver
actor "مسؤول أرشيف" as Archivist
rectangle "HR.Core + HR.Contracts" {
  usecase "نشر إعلان وظيفي" as U1
  usecase "تقديم طلب توظيف" as U2
  usecase "فرز المرشحين" as U3
  usecase "إنشاء حالة تعيين" as U4
  usecase "تفعيل العقد" as U5
  usecase "تسجيل المباشرة" as U6
  usecase "نشر حدث اكتمال التعيين" as U7
  usecase "أرشفة ملف التعيين" as U8
}
Citizen --> U2
ContractOps --> U1
ContractOps --> U3
ContractOps --> U4
Approver --> U5
ContractOps --> U6
U6 .> U7 : <<include>>
U7 .> U8 : <<include>>
Archivist --> U8
"""
    ),
    "05_usecase_people_development": uml(
        """
title حالات استخدام النقل والتدريب والصحة
left to right direction
actor "موظف الموارد البشرية" as Hr
actor "مدير الفرع" as Branch
actor "مسؤول التدريب" as Training
actor "مسؤول الصحة" as Health
rectangle "خدمات الأفراد" {
  usecase "طلب نقل أو انتداب" as U1
  usecase "اعتماد وتنفيذ النقل" as U2
  usecase "نشر الأثر المالي" as U3
  usecase "إعداد خطة تدريب" as U4
  usecase "تسجيل الالتحاق والإكمال" as U5
  usecase "إنشاء سجل صحي" as U6
  usecase "توثيق فحص أو حادث مهني" as U7
}
Hr --> U1
Branch --> U2
U2 .> U3 : <<include>>
Training --> U4
Training --> U5
Health --> U6
Health --> U7
"""
    ),
    "06_usecase_payroll_governance": uml(
        """
title حالات استخدام الرواتب والحوكمة والخدمات المساندة
left to right direction
actor "مسؤول الرواتب" as PayrollOps
actor "مدقق الامتثال" as Auditor
actor "مسؤول تقنية" as It
actor "الإدارة المركزية" as Central
rectangle "Payroll + Governance" {
  usecase "فتح دورة رواتب" as U1
  usecase "حساب ومراجعة واعتماد الدورة" as U2
  usecase "تطبيق تعديلات الأحداث" as U3
  usecase "إصدار بطاقة هوية" as U4
  usecase "إدارة العهد والدعم" as U5
  usecase "معالجة شكوى أو إجراء" as U6
  usecase "إنتاج لوحة ومؤشرات" as U7
}
PayrollOps --> U1
PayrollOps --> U2
U2 .> U3 : <<include>>
It --> U4
It --> U5
Auditor --> U6
Central --> U7
"""
    ),
    "07_usecase_ess_public": uml(
        """
title حالات استخدام البوابة العامة والخدمة الذاتية
left to right direction
actor "مواطن" as Citizen
actor "موظف" as Employee
actor "موظف الموارد البشرية" as Hr
rectangle "بوابة المواطن وESS" {
  usecase "استعراض الوظائف العامة" as U1
  usecase "تقديم وترقيم الطلب" as U2
  usecase "تتبع الطلب بالرقم الوطني" as U3
  usecase "الترشح لفرصة داخلية" as U4
  usecase "الالتحاق بدورة إلكترونية" as U5
  usecase "تحديث تقدم التعلم" as U6
  usecase "إصدار شهادة تعلم" as U7
  usecase "إدارة الإعلانات والفرص" as U8
}
Citizen --> U1
Citizen --> U2
Citizen --> U3
Employee --> U4
Employee --> U5
Employee --> U6
Employee --> U7
Hr --> U8
"""
    ),
    "08_activity_hire": uml(
        """
title مخطط نشاط: دورة التعيين
start
:استلام طلب المرشح;
if (البيانات مكتملة؟) then (نعم)
  :فرز واعتماد المرشح;
  :إنشاء حالة التعيين وقائمة التحقق;
  :إدخال الملف المرجعي للموظف;
  :إعداد العقد;
  if (اعتماد العقد؟) then (نعم)
    :تفعيل العقد وتسجيل المباشرة;
    :نشر HireCaseCompleted;
    :إنشاء أرشيف وبطاقة وحساب;
  else (لا)
    :إعادة المعاملة للتصحيح;
  endif
else (لا)
  :طلب استكمال البيانات;
endif
stop
"""
    ),
    "09_activity_contract": uml(
        """
title مخطط نشاط: تفعيل العقد والمعاملة الخدمية
start
:فتح سجل العقد;
:التحقق من حالة الموظف والبيانات;
if (العقد صالح؟) then (نعم)
  :اعتماد العقد;
  :تسجيل المباشرة;
  :إنشاء معاملة خدمة;
  :إتاحة الأثر للرواتب;
else (لا)
  :رفض أو إرجاع العقد;
endif
stop
"""
    ),
    "10_activity_leave_advance": uml(
        """
title مخطط نشاط: الإجازة أو السلفة
start
:إنشاء الطلب;
:التحقق من الصلاحية والنطاق;
if (يحقق قواعد السياسة؟) then (نعم)
  :إحالته للاعتماد;
  if (اعتماد الطلب؟) then (نعم)
    :تحديث حالة المعاملة;
    :نشر الحدث إلى الرواتب عند وجود أثر;
    :تسجيل الأثر والتدقيق;
  else (لا)
    :إشعار مقدم الطلب بالرفض;
  endif
else (لا)
  :رفض مع سبب قابل للتتبع;
endif
stop
"""
    ),
    "11_activity_transfer": uml(
        """
title مخطط نشاط: النقل أو الانتداب بين الوحدات
start
:استلام طلب الحركة;
:التحقق من الوحدة المصدر والوجهة;
if (المستخدم مخول للنطاق؟) then (نعم)
  :مراجعة الاحتياج والاعتماد;
  :تنفيذ النقل;
  :نشر TransferExecuted;
  fork
    :تحديث وحدة الموظف المرجعية;
  fork again
    :تحديث الأثر المالي;
  fork again
    :تحديث الدليل والتجهيزات;
  end fork
else (لا)
  :منع الوصول وتسجيل المحاولة;
endif
stop
"""
    ),
    "12_activity_separation": uml(
        """
title مخطط نشاط: إنهاء الخدمة
start
:بدء معاملة الفصل;
:اختيار السبب والتحقق من المستندات;
:تشغيل قائمة التحقق;
if (اكتملت الاعتمادات؟) then (نعم)
  :اعتماد الإنهاء;
  :نشر SeparationCompleted;
  fork
    :إقفال الأثر المالي;
  fork again
    :إبطال البطاقة والعهد;
  fork again
    :أرشفة الملف;
  end fork
else (لا)
  :إبقاء المعاملة قيد الاستكمال;
endif
stop
"""
    ),
    "13_activity_payroll": uml(
        """
title مخطط نشاط: دورة الرواتب التشغيلية
start
:فتح فترة الرواتب;
:تجميع ملفات الأجر والتعديلات;
:حساب المسودات;
if (البيانات صحيحة؟) then (نعم)
  :مراجعة الدورة;
  :اعتماد الدورة;
  :ترحيل الدورة;
  :نشر PayrollProcessed;
else (لا)
  :تسجيل الأخطاء وإعادة الحساب;
endif
stop
"""
    ),
    "14_sequence_hire": uml(
        """
title مخطط تسلسل: التعيين
actor "موظف الموارد البشرية" as Hr
participant Gateway
participant "HR.Core" as Core
participant "HR.Contracts" as Contracts
queue RabbitMQ
participant "Records.Archive" as Archive
participant "Auth.Directory" as Directory
Hr -> Gateway : اعتماد المرشح
Gateway -> Core : إنشاء HireCase
Core -> Contracts : تجهيز العقد
Hr -> Gateway : اعتماد وتفعيل العقد
Gateway -> Contracts : ActivateContract
Contracts -> RabbitMQ : HireCaseCompleted
RabbitMQ -> Archive : أرشفة الملف
RabbitMQ -> Directory : تجهيز الحساب
Contracts --> Gateway : حالة النجاح
Gateway --> Hr : قائمة تحقق مكتملة
"""
    ),
    "15_sequence_leave": uml(
        """
title مخطط تسلسل: اعتماد الإجازة أو السلفة
actor "موظف الفرع" as Branch
participant Gateway
participant "HR.Contracts" as Contracts
participant "سياسة الخدمة" as Rules
queue RabbitMQ
participant Payroll
Branch -> Gateway : إرسال الطلب
Gateway -> Contracts : إنشاء الطلب ضمن النطاق
Contracts -> Rules : تحقق من القواعد
Rules --> Contracts : نتيجة التحقق
Branch -> Gateway : اعتماد الطلب
Gateway -> Contracts : اعتماد
Contracts -> RabbitMQ : LeaveApproved / AdvanceApproved
RabbitMQ -> Payroll : تطبيق تعديل عند الحاجة
Payroll --> Contracts : تسجيل الأثر
Contracts --> Gateway : حالة الطلب
"""
    ),
    "16_sequence_transfer": uml(
        """
title مخطط تسلسل: النقل بين الفروع
actor "مسؤول الفرع" as Branch
participant Gateway
participant "HR.Movements" as Moves
queue RabbitMQ
participant "HR.Core" as Core
participant Payroll
participant "Auth.Directory" as Directory
Branch -> Gateway : طلب وتنفيذ نقل
Gateway -> Moves : TransferRequest
Moves -> Moves : تحقق النطاق والاعتماد
Moves -> RabbitMQ : TransferExecuted
RabbitMQ -> Core : تحديث PrimaryOrgUnitId
RabbitMQ -> Payroll : تحديث الأثر المالي
RabbitMQ -> Directory : تحديث دليل المستخدم
Moves --> Gateway : تنفيذ الحركة
"""
    ),
    "17_sequence_separation": uml(
        """
title مخطط تسلسل: إنهاء الخدمة
actor "موظف العقود" as ContractsOps
participant Gateway
participant "HR.Contracts" as Contracts
queue RabbitMQ
participant Payroll
participant IDCard
participant "Records.Archive" as Archive
participant "IT.Infra" as IT
ContractsOps -> Gateway : اعتماد الفصل
Gateway -> Contracts : PublishSeparation
Contracts -> RabbitMQ : SeparationCompleted
RabbitMQ -> Payroll : إقفال الأثر المالي
RabbitMQ -> IDCard : إبطال البطاقة
RabbitMQ -> Archive : أرشفة المعاملة
RabbitMQ -> IT : استرداد العهد/تعطيل الوصول
Contracts --> Gateway : اكتمال المسار
"""
    ),
    "18_sequence_payroll": uml(
        """
title مخطط تسلسل: معالجة دورة رواتب
actor "مسؤول الرواتب" as Officer
participant Gateway
participant Payroll
participant "قواعد فترة الرواتب" as Rules
queue RabbitMQ
participant "Dev.Analytics" as Analytics
Officer -> Gateway : فتح دورة رواتب
Gateway -> Payroll : CreatePayrollRun
Officer -> Gateway : حساب الدورة
Gateway -> Payroll : Calculate
Payroll -> Rules : تحقق الأيام والتناسب
Rules --> Payroll : قواعد تطبيقية
Officer -> Gateway : مراجعة واعتماد وترحيل
Gateway -> Payroll : Approve / Post
Payroll -> RabbitMQ : PayrollProcessed
RabbitMQ -> Analytics : تحديث المؤشرات
Payroll --> Gateway : حالة الدورة
"""
    ),
    "19_domain_hrcore": uml(
        """
title مخطط نموذج المجال: النواة التنظيمية والموظف
class OrgUnit {
  +Id: Guid
  +Code: string
  +NameAr: string
  +ParentId: Guid?
}
class Employee {
  +Id: Guid
  +NationalId: string
  +FullName: string
  +PrimaryOrgUnitId: Guid?
  +Status: EmploymentStatus
}
class EmploymentRecord {
  +Id: Guid
  +EmployeeId: Guid
  +OrgUnitId: Guid?
  +StartedAt: DateTime
}
class EmploymentStatusHistory {
  +Id: Guid
  +EmployeeId: Guid
  +Status: EmploymentStatus
  +ChangedAt: DateTime
}
class PublicJobPosting
class CitizenApplication
class InternalVacancy
class InternalApplication
OrgUnit "0..1" <-- "0..*" OrgUnit : Parent
Employee "0..*" --> "0..1" OrgUnit : PrimaryOrgUnit
Employee "1" -- "0..*" EmploymentRecord
Employee "1" -- "0..*" EmploymentStatusHistory
PublicJobPosting "1" -- "0..*" CitizenApplication
InternalVacancy "1" -- "0..*" InternalApplication
"""
    ),
    "20_erd_logical": uml(
        """
title مخطط ERD منطقي للكيانات المحورية
entity "OrgUnit" as Org {
  * Id : uuid
  --
  Code : text
  ParentId : uuid?
}
entity "Employee" as Emp {
  * Id : uuid
  --
  NationalId : text
  PrimaryOrgUnitId : uuid?
}
entity "Contract" as Contract {
  * Id : uuid
  --
  EmployeeId : uuid
  Status : text
}
entity "LeaveRequest" as Leave {
  * Id : uuid
  --
  EmployeeId : uuid
  Status : text
}
entity "TransferRequest" as Transfer {
  * Id : uuid
  --
  EmployeeId : uuid
  FromOrgUnitId : uuid
  ToOrgUnitId : uuid
}
entity "PayrollRun" as Run {
  * Id : uuid
  --
  Period : text
  Status : text
}
entity "SalaryLine" as Salary {
  * Id : uuid
  --
  PayrollRunId : uuid
  EmployeeId : uuid
}
Org ||--o{ Emp : unit
Emp ||--o{ Contract : employee reference
Emp ||--o{ Leave : employee reference
Emp ||--o{ Transfer : employee reference
Run ||--o{ Salary : payroll run
Emp ||..o{ Salary : cross-service reference
note bottom
الأسهم المنقطة تمثل مراجع منطقية بين قواعد مستقلة،
وليست مفاتيح خارجية عابرة للخدمات.
end note
"""
    ),
    "21_component_architecture": uml(
        """
title مخطط المكونات والخدمات
actor "مستخدم" as User
component "Gateway\nYARP" as Gateway
component "Keycloak\nOIDC" as Keycloak
package "الخدمات الأساسية" {
  [HR.Core] as Core
  [HR.Contracts] as Contracts
  [Payroll] as Payroll
}
package "الخدمات المساندة" {
  [Health] as Health
  [Movements] as Moves
  [Training] as Training
  [Archive / ID / IT] as Support
  [Compliance / Analytics] as Gov
}
queue "RabbitMQ\nMassTransit" as Bus
database "قواعد PostgreSQL\nمنفصلة" as Db
User --> Gateway
Gateway --> Keycloak
Gateway --> Core
Gateway --> Contracts
Gateway --> Payroll
Gateway --> Health
Gateway --> Moves
Gateway --> Training
Gateway --> Support
Gateway --> Gov
Core --> Bus
Contracts --> Bus
Payroll --> Bus
Moves --> Bus
Bus --> Gov
Core --> Db
Contracts --> Db
Payroll --> Db
Health --> Db
"""
    ),
    "22_deployment": uml(
        """
title مخطط النشر التشغيلي
node "متصفح المستخدم" as Browser
node "شبكة المؤسسة / HTTPS" as Network
node "بيئة Docker Compose أو إنتاج" {
  node "Gateway" as Gateway
  node "خدمات HRManagement" as Services
  node "Keycloak" as Keycloak
  queue "RabbitMQ" as Rabbit
  database "PostgreSQL\nقواعد متعددة" as Postgres
  storage "MinIO\nملفات" as Minio
}
Browser --> Network
Network --> Gateway
Gateway --> Keycloak
Gateway --> Services
Services --> Rabbit
Services --> Postgres
Services --> Minio
note right of Gateway
Health checks + correlation IDs
توجد على البوابة والخدمات
end note
"""
    ),
    "23_security_scope": uml(
        """
title نموذج التفويض ونطاق الفرع
actor "مدير مركزي" as Central
actor "مدير الفرع A" as A
actor "مدير الفرع B" as B
rectangle "Keycloak\nRoles + scope_org_unit_id" as Identity
rectangle "HrOrgScopeVisibility\nOrgScopeService" as Scope
database "HR.Core: OrgUnit Tree" as Tree
rectangle "واجهات الخدمات" as Apps
Central --> Identity : role مركزي
A --> Identity : branch-manager + scope=A
B --> Identity : branch-manager + scope=B
Identity --> Scope : claims
Scope --> Tree : expand subtree
Scope --> Apps : allowed OrgUnitIds
note bottom of Apps
المركز: كل الوحدات
الفرع A: A وتفرعاته فقط
الفرع B: B وتفرعاته فقط
منع الفروع الشقيقة
end note
"""
    ),
    "24_event_flow": uml(
        """
title تدفق الأحداث العابرة للخدمات
component "HR.Core" as Core
component "HR.Contracts" as Contracts
component "HR.Movements" as Moves
component Payroll
component "Records.Archive" as Archive
component "Auth.Directory" as Directory
component "Dev.Analytics" as Analytics
queue "RabbitMQ\nEvents" as Bus
Core --> Bus : IEmployeeCreated
Contracts --> Bus : IHireCaseCompleted\nISeparationCompleted
Moves --> Bus : ITransferExecuted\nISecondmentExecuted
Payroll --> Bus : IPayrollProcessed
Bus --> Archive : hire / separation archive
Bus --> Directory : user / movement update
Bus --> Payroll : adjustments
Bus --> Core : employee org update
Bus --> Analytics : indicators
note bottom of Bus
Outbox, retry, idempotency and correlation IDs
reduce duplicate processing and increase traceability.
end note
"""
    ),
    "25_data_ownership": uml(
        """
title ملكية البيانات وعلاقاتها المنطقية
database "hr_core_db\nEmployee, OrgUnit" as CoreDb
database "hr_contracts_db\nContract, Leave, Separation" as ContractDb
database "payroll_db\nPayrollRun, SalaryLine" as PayDb
database "movements_db\nTransfer, Secondment" as MoveDb
database "health_db\nMedicalRecord" as HealthDb
database "auth_directory_db\nAuthUser, DirectoryEntry" as AuthDb
queue "RabbitMQ" as Bus
CoreDb ..> ContractDb : EmployeeId reference
CoreDb ..> PayDb : EmployeeId reference
CoreDb ..> MoveDb : EmployeeId / OrgUnitId
CoreDb ..> HealthDb : EmployeeId reference
CoreDb ..> AuthDb : EmployeeId / PrimaryOrgUnitId
ContractDb --> Bus
MoveDb --> Bus
PayDb --> Bus
note bottom
لا توجد وصلات قاعدة بيانات مباشرة بين الخدمات؛
كل خدمة تملك مخططها وتتكامل عبر معرفات وأحداث.
end note
"""
    ),
}


def write_sources() -> list[Path]:
    DIAGRAM_DIR.mkdir(parents=True, exist_ok=True)
    paths: list[Path] = []
    for name, source in SOURCES.items():
        path = DIAGRAM_DIR / f"{name}.puml"
        path.write_text(source, encoding="utf-8")
        paths.append(path)
    readme = DIAGRAM_DIR / "README.md"
    readme.write_text(
        "# مخططات الأطروحة\n\n"
        "هذه الملفات مصادر PlantUML قابلة للتحرير للمخططات المدرجة في الأطروحة. "
        "تشغل بواسطة `scripts/generate_thesis_diagrams.py` لإنتاج نسخ PNG داخل `rendered/`.\n",
        encoding="utf-8",
    )
    return paths


def render() -> None:
    if not JAR.exists():
        raise FileNotFoundError(f"PlantUML jar was not found: {JAR}")
    RENDER_DIR.mkdir(parents=True, exist_ok=True)
    subprocess.run(
        [
            "java",
            "-DPLANTUML_LIMIT_SIZE=8192",
            "-jar",
            str(JAR),
            "-tpng",
            "-charset",
            "UTF-8",
            "-o",
            str(RENDER_DIR),
            str(DIAGRAM_DIR),
        ],
        check=True,
        cwd=ROOT,
    )


def main() -> None:
    paths = write_sources()
    render()
    missing = [path.stem for path in paths if not (RENDER_DIR / f"{path.stem}.png").exists()]
    if missing:
        raise RuntimeError(f"PlantUML did not render: {', '.join(missing)}")
    print(f"Rendered {len(paths)} editable diagrams in {RENDER_DIR}")


if __name__ == "__main__":
    main()
