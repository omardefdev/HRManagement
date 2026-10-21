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
            normalized.append("\\n")
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
Contracts --> Bus : IHireCaseCompleted\\nISeparationCompleted
Moves --> Bus : ITransferExecuted\\nISecondmentExecuted
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
    "26_database_landscape": uml(
        """
title منظومة قواعد البيانات الحالية وملكية الخدمات
left to right direction
cloud "Keycloak
مخزن الهوية" as Keycloak
component "Gateway / YARP" as Gateway
rectangle "TenantContext
tenant_id + scope_org_unit_id" as TenantContext
queue "RabbitMQ / MassTransit
Outbox + Inbox" as Bus
storage "MinIO
المحتوى الثنائي" as Minio

package "السياقات الجوهرية" {
  database "gateway_audit_db
Gateway Audit" as GatewayDb
  database "hr_core_db
HR.Core" as CoreDb
  database "hr_contracts_db
HR.Contracts" as ContractsDb
  database "payroll_db
Payroll" as PayrollDb
}
package "سياقات دورة حياة الموظف" {
  database "hr_health_db
HR.Health" as HealthDb
  database "hr_movements_db
HR.Movements" as MovementsDb
  database "hr_training_db
HR.Training" as TrainingDb
  database "idcard_db
IDCard" as CardDb
}
package "السياقات المساندة" {
  database "admin_db
Admin.Logistics" as LogisticsDb
  database "auth_directory_db
Auth.Directory" as DirectoryDb
  database "compliance_db
Compliance" as ComplianceDb
  database "analytics_db
Dev.Analytics" as AnalyticsDb
  database "it_infra_db
IT.Infra" as ItDb
  database "archive_db
Records.Archive" as ArchiveDb
  database "file_storage_db
File.Storage" as FileDb
}

Gateway --> Keycloak : OIDC / JWT
Gateway --> GatewayDb : تدقيق الطلبات
Gateway --> TenantContext : استخراج سياق الجهة والنطاق
TenantContext ..> CoreDb
TenantContext ..> ContractsDb
TenantContext ..> PayrollDb
CoreDb --> Bus
ContractsDb --> Bus
MovementsDb --> Bus
PayrollDb --> Bus
Bus --> ArchiveDb
Bus --> AnalyticsDb
FileDb --> Minio : بيانات وصفية + كائنات

note top of Bus
أحداث الموظف والجهة والسياسة والرمز،
والتعيين والعقد والإجازة والفصل،
والنقل والراتب والدفع.
end note
note bottom of TenantContext
تملك كل خدمة سياقها وترحيلاتها. يطبق العزل عبر مرشح الجهة،
ونطاق الوحدة التنظيمية، وسياسات PostgreSQL RLS حيث توافرت.
ولا تنشأ مفاتيح أجنبية عابرة لقواعد الخدمات.
end note
"""
    ),
    "27_database_hrcore": uml(
        """
title ERD مفاهيمي مختصر للنواة الحالية HR.Core
left to right direction

entity "Tenant
الجهة" as Tenant {
  * Id : uuid <<PK>>
  Code : text <<UQ>>
}
entity "TenantAssignmentRule
قاعدة الإسناد" as AssignmentRule {
  * Id : uuid <<PK>>
  TenantId : uuid <<FK>>
}
entity "TenantQuarantineEntry
سجل الحجر" as Quarantine {
  * Id : uuid <<PK>>
  CandidateTenantId : uuid
}
entity "OrgUnit
الوحدة التنظيمية" as OrgUnit {
  * Id : uuid <<PK>>
  TenantId : uuid <<FK>>
  ParentId : uuid <<FK>>
}
entity "OrgUnitVersion
نسخة الهيكل المؤرخة" as OrgVersion {
  * Id : uuid <<PK>>
  OrgUnitId : uuid <<FK>>
  ValidFrom : timestamp
}
entity "Employee
الموظف" as Employee {
  * Id : uuid <<PK>>
  TenantId : uuid <<FK>>
  PrimaryOrgUnitId : uuid <<FK>>
  NationalIdEncrypted : bytes
  NationalIdHash : text
}
entity "JobCatalogEntry
دليل الوظائف" as Job {
  * Id : uuid <<PK>>
  TenantId : uuid <<FK>>
}
entity "FundedPosition
وظيفة ممولة" as Position {
  * Id : uuid <<PK>>
  JobCatalogEntryId : uuid <<FK>>
  OrgUnitId : uuid <<FK>>
}
entity "WorkforcePlan
خطة القوى العاملة" as Workforce {
  * Id : uuid <<PK>>
  TenantId : uuid <<FK>>
}
entity "PolicyTemplateVersion
إصدار السياسة" as Policy {
  * Id : uuid <<PK>>
  TenantId : uuid <<FK>>
  Status : enum
}
entity "WorkflowDefinition
تعريف سير العمل" as Workflow {
  * Id : uuid <<PK>>
  TenantId : uuid <<FK>>
}
entity "CodePolicyVersion
سياسة الرمز" as CodePolicy {
  * Id : uuid <<PK>>
  TenantId : uuid <<FK>>
}

Tenant ||--o{ OrgUnit
Tenant ||--o{ Employee
Tenant ||--o{ AssignmentRule
AssignmentRule ..> Quarantine
OrgUnit ||--o{ OrgVersion
OrgUnit ||--o{ Employee : PrimaryOrgUnit
OrgUnit ||--o{ Position
Job ||--o{ Position
Workforce ..> Position
Policy ..> Workflow
CodePolicy ..> OrgUnit

note bottom of Employee
هذا عرض مفاهيمي للكيانات المحورية لا قائمة جداول نهائية.
يعتمد ملحق ERD المستخرج من Migration Snapshot مرجعاً بنيوياً كاملاً.
end note
"""
    ),
    "28_database_contracts": uml(
        """
title ERD مفاهيمي مختصر للعقود ودورة الخدمة HR.Contracts
left to right direction

entity "Contract
العقد" as Contract {
  * Id : uuid <<PK>>
  TenantId : uuid
  EmployeeId : uuid <<identifier>>
  ContractTypeId : uuid <<FK>>
  Status : enum
}
entity "ContractType
نوع العقد" as ContractType {
  * Id : uuid <<PK>>
  TenantId : uuid
}
entity "ContractSignature
توقيع العقد" as Signature {
  * Id : uuid <<PK>>
  ContractId : uuid <<FK>>
}
entity "ProbationRecord
فترة التجربة" as Probation {
  * Id : uuid <<PK>>
  ContractId : uuid <<FK>>
}
entity "ServiceTransaction
معاملة الخدمة" as ServiceTx {
  * Id : uuid <<PK>>
  TenantId : uuid
  EmployeeId : uuid <<identifier>>
  State : enum
}
entity "HireChecklist
قائمة التعيين" as HireChecklist {
  * Id : uuid <<PK>>
  ServiceTransactionId : uuid <<FK>>
}
entity "SeparationRecord
إنهاء الخدمة" as Separation {
  * Id : uuid <<PK>>
  ContractId : uuid <<FK>>
}
entity "SeparationChecklist
قائمة الإنهاء" as SeparationChecklist {
  * Id : uuid <<PK>>
  SeparationRecordId : uuid <<FK>>
}
entity "LeaveRequest
طلب إجازة" as Leave {
  * Id : uuid <<PK>>
  TenantId : uuid
  EmployeeId : uuid <<identifier>>
}
entity "AdvanceRequest
طلب سلفة" as Advance {
  * Id : uuid <<PK>>
  TenantId : uuid
  EmployeeId : uuid <<identifier>>
}
entity "AdvanceInstallmentPlan
خطة التقسيط" as Installment {
  * Id : uuid <<PK>>
  AdvanceRequestId : uuid <<FK>>
}

ContractType ||--o{ Contract
Contract ||--o{ Signature
Contract ||--o{ Probation
Contract ||--o{ Separation
ServiceTx ||--o{ HireChecklist
Separation ||--o{ SeparationChecklist
Advance ||--o{ Installment

note bottom of Contract
EmployeeId وOrgUnitId معرفات تكاملية إلى HR.Core بلا FK عابر.
وتنتشر آثار التعيين والإجازة والفصل عبر العقود المنشورة.
end note
"""
    ),
    "29_database_payroll": uml(
        """
title ERD مفاهيمي مختصر للرواتب والخزينة الحالية
left to right direction

entity "PayrollConfigurationVersion
إصدار الإعداد" as Config {
  * Id : uuid <<PK>>
  TenantId : uuid
  Status : enum
}
entity "PayrollRun
دورة الراتب" as Run {
  * Id : uuid <<PK>>
  TenantId : uuid
  ConfigurationVersionId : uuid <<FK>>
  Status : enum
}
entity "PayrollRunInputSnapshot
لقطة المدخلات" as Snapshot {
  * Id : uuid <<PK>>
  PayrollRunId : uuid <<FK>>
  InputHash : text
}
entity "PayrollRunJob
مهمة الحساب" as Job {
  * Id : uuid <<PK>>
  PayrollRunId : uuid <<FK>>
  Status : enum
}
entity "PayrollRunPartition
قسم التنفيذ" as Partition {
  * Id : uuid <<PK>>
  PayrollRunJobId : uuid <<FK>>
  Checkpoint : text
}
entity "SalaryLine
سطر الراتب" as SalaryLine {
  * Id : uuid <<PK>>
  PayrollRunId : uuid <<FK>>
  EmployeeId : uuid <<identifier>>
}
entity "FundingDecision
قرار التمويل" as Funding {
  * Id : uuid <<PK>>
  PayrollRunId : uuid <<FK>>
}
entity "ArrearsLedgerEntry
قيد المتأخرات" as Arrears {
  * Id : uuid <<PK>>
  EmployeeId : uuid <<identifier>>
}
entity "PaymentBatch
دفعة الصرف" as Batch {
  * Id : uuid <<PK>>
  PayrollRunId : uuid <<FK>>
}
entity "PaymentOrder
أمر الدفع" as PaymentOrder {
  * Id : uuid <<PK>>
  PaymentBatchId : uuid <<FK>>
}
entity "BankStatementLine
سطر الكشف المصرفي" as BankLine {
  * Id : uuid <<PK>>
  PaymentOrderId : uuid <<FK nullable>>
}
entity "FraudAlert
تنبيه الاحتيال" as Fraud {
  * Id : uuid <<PK>>
  PayrollRunId : uuid <<FK>>
}
entity "PaymentHold
حجز الدفع" as Hold {
  * Id : uuid <<PK>>
  FraudAlertId : uuid <<FK>>
}

Config ||--o{ Run
Run ||--|| Snapshot
Run ||--o{ Job
Job ||--o{ Partition
Run ||--o{ SalaryLine
Run ||--o{ Funding
Funding ..> Arrears : الصرف الجزئي
Run ||--o{ Batch
Batch ||--o{ PaymentOrder
PaymentOrder ||--o{ BankLine : التسوية
Run ||--o{ Fraud
Fraud ||--o{ Hold

note bottom of Run
يفصل النموذج بين الحساب والترحيل والصرف، ويحفظ لقطة ثابتة للمدخلات،
ويدعم الاستئناف والمتأخرات والتسوية والحجز. الاعتماد القانوني والتكامل
المصرفي الحي خارج نطاق النتائج المثبتة في النسخة الحالية.
end note
"""
    ),
    "30_database_integration": uml(
        """
title تواصل قواعد البيانات الحالية عبر السياق والأحداث والمعرفات
left to right direction

rectangle "TenantContext
tenant_id + scope_org_unit_id" as TenantContext
component "Policy Engine
Workflow + Enforcement" as Policy
component "Code Generation
Version + Counter" as Codes
package "خدمات مقيدة بسياق الجهة والنطاق" as Scoped {
  database "HR.Core
Tenant / Employee / OrgUnit" as Core
  database "HR.Contracts
Contract / Leave / Separation" as Contracts
  database "HR.Movements
Transfer / Secondment" as Movements
  database "Payroll
Run / Funding / Payment" as Payroll
  database "Admin.Logistics
Supply / Receipt / Asset" as Logistics
  database "File.Storage
Quarantine / Grant / Audit" as Files
}
database "Records.Archive
ArchiveItem" as Archive
database "Dev.Analytics
Read models" as Analytics
queue "RabbitMQ / MassTransit
Outbox + Inbox + retry" as Bus

TenantContext ..> Scoped
Core --> Policy
Core --> Codes
Core --> Bus
Contracts --> Bus
Movements --> Bus
Payroll --> Bus
Logistics --> Bus
Files --> Bus
Bus --> Archive
Bus --> Analytics
Bus --> Payroll
Bus --> Logistics

note top of Bus
حقائق الموظف والجهة والتهيئة، والعقود والحركات،
والرواتب والدفع، والعهد، وفحص الملفات وترقيتها.
end note

note right of Policy
يطبق TenantContext على جميع المسارات المحمية.
وتستدعي النواة عمليات تقييم السياسات وإنفاذها،
ومعاينة الرموز وتوليدها من المحركين المشتركين.
end note

note bottom of Bus
لا توجد استعلامات أو مفاتيح أجنبية عابرة لقواعد الخدمات.
يحمل كل أثر معرف الجهة والكيان وCorrelationId، ويعالج التكرار محلياً.
ويظل اكتمال الرحلة قابلاً للمراقبة والتسوية بعد نجاح المعاملة المحلية.
end note
"""
    ),
    "31_tenant_context": uml(
        """
title سياق المنصة متعددة الجهات والإدارات
left to right direction
actor "مسؤول المنصة" as Platform
actor "مدير الجهة" as Agency
actor "الإدارة المركزية" as Central
actor "إدارة فرعية" as Branch
actor "موظف" as Employee
actor "مواطن" as Citizen
cloud "موفر الهوية\nKeycloak" as Identity
rectangle "منصة HRManagement" as System
rectangle "حدود الجهة Tenant" as Tenant
Platform --> System : تهيئة جهة وحزم وطنية
Agency --> System : سياسات الجهة والهيكل
Central --> System : حوكمة وقرارات شاملة
Branch --> System : تشغيل ضمن الشجرة المخولة
Employee --> System : خدمة ذاتية
Citizen --> System : توظيف عام
System --> Identity : OIDC / JWT
System --> Tenant : tenant_id + OrgUnit scope
note bottom of Tenant
كل جهة تملك شجرتها التنظيمية وبياناتها التشغيلية،
ولا تسمح مرشحات EF Core وسياسات PostgreSQL RLS
بالعبور إلى جهة أخرى في المسارات المحمية.
end note
"""
    ),
    "32_usecase_platform_governance": uml(
        """
title حالات استخدام حوكمة المنصة والجهة
left to right direction
actor "مسؤول المنصة" as Platform
actor "مدير الجهة" as Agency
actor "مسؤول السياسات" as PolicyOfficer
actor "مسؤول البيانات المرجعية" as MdmOfficer
rectangle "الحوكمة المؤسسية" {
  usecase "إنشاء جهة جديدة" as U1
  usecase "معاينة خطة التهيئة" as U2
  usecase "تطبيق الحزم الوطنية" as U3
  usecase "نشر قالب سياسة" as U4
  usecase "محاكاة أثر السياسة" as U5
  usecase "فرض السياسة أو تشغيلها ظلياً" as U6
  usecase "تهيئة أنماط توليد الرموز" as U7
  usecase "توليد رمز مؤسسي فريد" as U8
  usecase "إدارة القيم المرجعية ثنائية اللغة" as U9
}
Platform --> U1
U1 .> U2 : <<include>>
U1 .> U3 : <<include>>
Agency --> U7
PolicyOfficer --> U4
U4 .> U5 : <<include>>
U4 .> U6 : <<extend>>
MdmOfficer --> U9
Agency --> U8
U8 .> U7 : <<include>>
"""
    ),
    "33_dfd_governance_level1": uml(
        """
title DFD المستوى الأول لحوكمة الجهة والسياسات والرموز
left to right direction
actor "مسؤول المنصة" as Platform
actor "مدير الجهة" as Agency
rectangle "1.0 تهيئة الجهة" as P1
rectangle "2.0 إدارة السياسات" as P2
rectangle "3.0 توليد الرموز" as P3
rectangle "4.0 إدارة البيانات المرجعية" as P4
database "D1 الجهات والوحدات" as D1
database "D2 إصدارات السياسات وقواعدها" as D2
database "D3 عدادات الرموز والسجل" as D3
database "D4 الحزم الوطنية وتطبيقاتها" as D4
queue "أحداث التكامل" as Bus
Platform --> P1 : AgencyManifest
P1 --> D1 : جهة وشجرة أولية
P1 --> D4 : نتيجة تطبيق الحزم
Agency --> P2 : مسودة / نشر / محاكاة
P2 --> D2 : نسخة وقواعد ودفتر إنفاذ
Agency --> P3 : نوع الرمز وسياق الجهة
P3 --> D3 : حجز وتسلسل ونتيجة
Agency --> P4 : قيم وبدائل محلية
P4 --> D4 : طبقة وطنية + طبقة الجهة
P1 --> Bus : AgencyOnboarded
P2 --> Bus : PolicyPublished
P3 --> Bus : CodeGenerated
"""
    ),
    "34_activity_agency_onboarding": uml(
        """
title مخطط نشاط تهيئة جهة جديدة
start
:استقبال AgencyManifest;
:التحقق من هوية الجهة والرموز والاعتماديات;
if (طلب معاينة فقط؟) then (نعم)
  :بناء خطة Dry Run;
  :عرض التغييرات والتعارضات دون كتابة;
  stop
else (لا)
  :بدء معاملة تهيئة قابلة للتكرار;
endif
if (سبق تطبيق الحزمة نفسها؟) then (نعم)
  :إرجاع سجل التطبيق السابق;
else (لا)
  :إنشاء Tenant والوحدات الجذرية;
  :تطبيق بيانات MDM الوطنية;
  :تطبيق قوالب السياسات والرموز;
  :حفظ SeedPackApplication;
  :نشر حدث اكتمال التهيئة;
endif
stop
"""
    ),
    "35_activity_policy_lifecycle": uml(
        """
title مخطط نشاط دورة حياة السياسة
start
:إنشاء قالب وسياسة مسودة;
:إضافة القواعد والحقائق الملزمة;
:التحقق من الصياغة والتعارضات;
if (المحاكاة ناجحة؟) then (نعم)
  :اعتماد نسخة غير قابلة للتعديل;
else (لا)
  :إعادة المسودة مع أسباب الرفض;
  stop
endif
if (وضع التشغيل؟) then (Shadow)
  :تسجيل القرار دون منع المعاملة;
else (Enforce)
  :تطبيق Allow / Deny / Requirements;
endif
:حفظ Enforcement Ledger وCorrelationId;
:ربط السياسة بسير العمل عند الحاجة;
stop
"""
    ),
    "36_sequence_policy_enforcement": uml(
        """
title مخطط تسلسل إنفاذ سياسة حية
actor "المستخدم المخول" as User
participant "خدمة الأعمال" as Service
participant "PolicyEnforceClient" as Client
participant "HR.Core Policy API" as Policy
database "Policy Store" as Store
participant "Workflow Engine" as Workflow
User -> Service : تنفيذ معاملة
Service -> Client : Enforce(policyKey, facts, tenant)
Client -> Policy : POST /api/policies/enforce
Policy -> Store : تحميل النسخة المنشورة والقواعد
Store --> Policy : policy version
Policy -> Policy : تقييم الحقائق والنطاق
Policy -> Store : حفظ قرار الإنفاذ
Policy --> Client : Allow / Deny / Requirements
alt القرار يسمح ويتطلب سير عمل
  Client --> Service : Allow + workflow binding
  Service -> Workflow : بدء العملية
else القرار يمنع
  Client --> Service : Deny + reason code
  Service --> User : رفض موثق
end
"""
    ),
    "37_sequence_code_generation": uml(
        """
title مخطط تسلسل توليد رمز مؤسسي
actor "خدمة المجال" as Domain
participant "CodeGenerationClient" as Client
participant "HR.Core Code API" as CodeApi
database "Code Patterns and Counters" as Store
Domain -> Client : Generate(type, tenant, orgUnit, date)
Client -> CodeApi : POST /api/codes/generate
CodeApi -> Store : تحميل النمط الفعال
CodeApi -> Store : حجز العداد داخل معاملة
CodeApi -> CodeApi : تركيب الرمز والتحقق من التفرد
CodeApi -> Store : حفظ CodeGenerationRecord
CodeApi --> Client : code + sequence + metadata
Client --> Domain : الرمز المعتمد
note bottom
المعاينة لا تحجز عداداً، أما التوليد الفعلي
فيحفظ أثراً ويضمن عدم تكرار الرمز ضمن نطاقه.
end note
"""
    ),
    "38_activity_asset_custody": uml(
        """
title مخطط نشاط عهدة الأصل والفقد والإرجاع
start
:اختيار أصل وموظف ضمن نطاق الجهة;
:إنشاء طلب تسليم عهدة;
if (تتطلب موافقة؟) then (نعم)
  :إرسال الطلب للمراجع;
  if (مرفوض؟) then (نعم)
    :حفظ سبب الرفض;
    stop
  endif
endif
:إثبات التسليم وتوقيع المستلم;
:تفعيل AssetAssignment;
if (بلاغ فقد؟) then (نعم)
  :إنشاء AssetLossReport;
  :بدء التحقيق وتوثيق القرار;
elseif (إرجاع؟) then (نعم)
  :فحص الحالة وإثبات الإرجاع;
  :إغلاق العهدة وتحديث حالة الأصل;
endif
:إتاحة محضر PDF عربي;
stop
"""
    ),
    "39_sequence_asset_assignment": uml(
        """
title مخطط تسلسل اعتماد عهدة أصل
actor "موظف اللوجستيات" as Officer
participant "Admin.Logistics" as Logistics
participant "Policy Client" as Policy
database "admin_db" as Db
queue "RabbitMQ" as Bus
participant "File.Storage" as Files
Officer -> Logistics : إنشاء طلب عهدة
Logistics -> Policy : تحقق من السياسة والنطاق
Policy --> Logistics : Allow + approval requirements
Logistics -> Db : حفظ الطلب
Logistics -> Bus : AssetAssignmentRequested
Bus --> Logistics : AssetAssignmentApproved / Rejected
alt تمت الموافقة
  Logistics -> Db : تفعيل العهدة وحفظ إثبات التسليم
  Logistics -> Files : حفظ المحضر والمرفقات
  Logistics -> Bus : AssetAssigned
else تم الرفض
  Logistics -> Db : حفظ القرار وسببه
end
"""
    ),
    "40_component_platform_governance": uml(
        """
title مكونات حوكمة المنصة والخدمات المشتركة
component "Gateway / YARP" as Gateway
component "Keycloak" as Keycloak
component "Tenant Context" as Tenant
component "Policy Engine" as Policy
component "Code Generation" as Codes
component "Agency Onboarding" as Onboarding
component "MDM / Seed Packs" as Mdm
component "Workflow Engine" as Workflow
component "14 Domain Services" as Services
database "15 Application Contexts" as Databases
queue "RabbitMQ / MassTransit" as Bus
Gateway --> Keycloak : OIDC
Gateway --> Tenant : tenant claim
Services --> Tenant : scoped context
Services --> Policy : evaluate / enforce
Services --> Codes : generate / preview
Onboarding --> Mdm : apply national pack
Onboarding --> Policy : policy overlays
Onboarding --> Codes : code overlays
Policy --> Workflow : trigger binding
Services --> Databases : owned context only
Services --> Bus : integration events
"""
    ),
    "41_security_tenant_rls": uml(
        """
title طبقات عزل الجهة والنطاق التنظيمي
actor "مستخدم" as User
rectangle "Keycloak claims\nroles + tenant_id + scope_org_unit_id" as Claims
rectangle "Gateway policies" as Gateway
rectangle "Service authorization" as Authz
rectangle "TenantAwareDbContext\nEF Core query filters" as Ef
database "PostgreSQL\nRow-Level Security" as Rls
database "Audit / Enforcement Ledger" as Audit
User --> Claims : تسجيل الدخول
Claims --> Gateway : JWT
Gateway --> Authz : هوية موثقة
Authz --> Ef : TenantContext + OrgScope
Ef --> Rls : tenant session context
Rls --> Audit : قرار وصول وأثر حساس
note right of Rls
دفاع متعدد الطبقات:
1. الدور والسياسة
2. نطاق الوحدة التنظيمية
3. مرشح الجهة في EF Core
4. RLS داخل PostgreSQL
end note
"""
    ),
    "42_reliability_business_journeys": uml(
        """
title الاعتمادية حسب رحلات الأعمال العابرة للخدمات
component "التعيين" as Hire
component "النقل" as Move
component "الفصل" as Separation
component "الرواتب" as Pay
component "العهد" as Assets
queue "Outbox / RabbitMQ / Inbox" as Flow
component "إعادة المحاولة" as Retry
component "التسوية اليدوية" as Reconcile
component "المراقبة والتنبيه" as Observe
Hire --> Flow : أحداث بدء الخدمة
Move --> Flow : تغيير الوحدة وآثاره
Separation --> Flow : إيقاف العقد والبطاقة والراتب
Pay --> Flow : نتائج الصرف والتقارير
Assets --> Flow : تسليم أو فقد أو إرجاع
Flow --> Retry : فشل مؤقت
Retry --> Reconcile : استنفاد المحاولات
Flow --> Observe : correlation + status
Reconcile --> Observe : قرار التسوية
note bottom
لا تعني المعمارية الموزعة معاملة واحدة بين كل القواعد؛
تتحقق الاعتمادية بالتتبع، وعدم التكرار، والتسوية،
مع تحديد RTO/RPO مؤسسياً وتجربتهما قبل الإنتاج.
end note
"""
    ),
    "43_usecase_current_master": uml(
        """
title حالات الاستخدام المرجعية لخط الأساس الحالي
left to right direction
actor "مسؤول المنصة" as Platform
actor "الإدارة المركزية" as Central
actor "مدير الفرع" as Branch
actor "موظف المجال" as Officer
actor "الرواتب والخزينة" as Treasury
actor "الأمن والخصوصية" as Security
actor "الموظف" as Employee
actor "المواطن" as Citizen
rectangle "منصة HRManagement متعددة الجهات" {
  usecase "تهيئة الجهة وإسناد البيانات\nوالحجر والمصالحة" as Tenant
  usecase "الهيكل المؤرخ والوظائف\nالممولة وخطة القوى العاملة" as Org
  usecase "دورة حياة الموظف\nوالعقود والحركات" as Lifecycle
  usecase "السياسات وسير العمل\nوالتوقيع وتوليد الرموز" as Governance
  usecase "الرواتب والتمويل\nوالدفع والتسوية" as Payroll
  usecase "التوريد والمخزون\nوالأصول والعهد" as Logistics
  usecase "الملفات والخصوصية\nوثقة الخدمات" as Privacy
  usecase "الخدمة الذاتية\nوالتوظيف العام" as Portals
}
Platform --> Tenant
Central --> Org
Central --> Governance
Central --> Lifecycle
Branch --> Lifecycle
Branch --> Logistics
Officer --> Lifecycle
Officer --> Logistics
Treasury --> Payroll
Security --> Privacy
Employee --> Portals
Citizen --> Portals
"""
    ),
    "44_usecase_tenant_assignment": uml(
        """
title حالات استخدام إسناد الجهة والحجر والمصالحة
left to right direction
actor "مسؤول منصة البيانات" as DataAdmin
actor "مسؤول المنصة" as Platform
actor "المدقق" as Auditor
actor "مالك الجهة" as Agency
rectangle "حوكمة بيانات الجهة" {
  usecase "تعريف قاعدة إسناد" as U1
  usecase "معاينة Dry Run" as U2
  usecase "إسناد السجلات الواضحة" as U3
  usecase "حجر السجلات الملتبسة" as U4
  usecase "مصالحة العدد والمراجع" as U5
  usecase "حل سجل محجور" as U6
  usecase "التراجع من نسخة احتياطية" as U7
  usecase "مراجعة أثر القرار" as U8
}
DataAdmin --> U1
DataAdmin --> U2
U2 .> U3 : <<include>>
U3 .> U4 : <<extend>>
U3 .> U5 : <<include>>
Platform --> U6
Platform --> U7
Auditor --> U5
Auditor --> U8
Agency --> U8
U6 .> U8 : <<include>>
"""
    ),
    "45_usecase_temporal_org": uml(
        """
title حالات استخدام الهيكل المؤرخ والوظائف الممولة
left to right direction
actor "مسؤول التنظيم" as OrgAdmin
actor "المعتمد المركزي" as Approver
actor "مخطط القوى العاملة" as Planner
actor "المالية" as Finance
actor "مدير الفرع" as Branch
rectangle "الهيكل والملاك" {
  usecase "اقتراح إنشاء أو نقل أو دمج\nأو تقسيم أو إغلاق وحدة" as U1
  usecase "تحليل الأثر ومنع الدورات" as U2
  usecase "اعتماد تاريخ النفاذ" as U3
  usecase "الاستعلام كما في تاريخ" as U4
  usecase "إدارة دليل الوظائف" as U5
  usecase "تعريف وظيفة ممولة" as U6
  usecase "إعداد خطة القوى العاملة" as U7
  usecase "مقارنة الممول والمشغول والشاغر" as U8
}
OrgAdmin --> U1
U1 .> U2 : <<include>>
Approver --> U3
U3 .> U2 : <<include>>
OrgAdmin --> U4
Branch --> U4
Planner --> U5
Finance --> U6
U6 .> U5 : <<include>>
Planner --> U7
U7 .> U8 : <<include>>
"""
    ),
    "46_usecase_policy_workflow": uml(
        """
title حالات استخدام السياسة وسير العمل والتوقيع والرموز
left to right direction
actor "مسؤول السياسات" as PolicyOfficer
actor "مسؤول سير العمل" as WorkflowOfficer
actor "مسؤول الرموز" as CodeOfficer
actor "خدمة المجال" as Service
actor "المعتمد" as Approver
actor "المدقق" as Auditor
rectangle "حوكمة القرار المؤسسي" {
  usecase "إنشاء إصدار سياسة" as P1
  usecase "محاكاة السياسة" as P2
  usecase "نشر Shadow أو Enforce" as P3
  usecase "تقييم أو إنفاذ القرار" as P4
  usecase "ربط القرار بسير عمل" as W1
  usecase "بدء مثيل مثبت الإصدار" as W2
  usecase "اعتماد أو رفض أو تفويض" as W3
  usecase "إثبات التوقيع" as W4
  usecase "إصدار نمط رمز" as C1
  usecase "معاينة أو توليد رمز" as C2
  usecase "مراجعة دفتر الإنفاذ" as A1
}
PolicyOfficer --> P1
P1 .> P2 : <<include>>
P1 .> P3 : <<include>>
Service --> P4
WorkflowOfficer --> W1
P4 .> W2 : <<extend>>
W1 .> W2 : <<include>>
Approver --> W3
W3 .> W4 : <<extend>>
CodeOfficer --> C1
Service --> C2
C2 .> C1 : <<include>>
Auditor --> A1
"""
    ),
    "47_usecase_advanced_payroll": uml(
        """
title حالات استخدام الرواتب والخزينة المتقدمة
left to right direction
actor "مسؤول إعدادات الرواتب" as Config
actor "مشغل الرواتب" as Operator
actor "المعتمد المالي" as Approver
actor "الخزينة" as Treasury
actor "مسؤول التسوية" as Reconcile
actor "مدقق الاحتيال" as Fraud
rectangle "Payroll" {
  usecase "اعتماد ونشر إصدار القاعدة" as U1
  usecase "تجميد لقطة المدخلات" as U2
  usecase "بدء حساب مقسم ومتواز" as U3
  usecase "إيقاف أو استئناف أو إلغاء" as U4
  usecase "التحقق وتجميد النتائج" as U5
  usecase "اعتماد وترحيل الدورة" as U6
  usecase "تسجيل قرار التمويل" as U7
  usecase "الصرف الجزئي وترحيل المتأخرات" as U8
  usecase "إنشاء أوامر الدفع" as U9
  usecase "الإقرار والتسوية المصرفية" as U10
  usecase "إنشاء تنبيه أو حجز دفع" as U11
  usecase "عكس تعويضي" as U12
}
Config --> U1
Operator --> U2
U2 .> U1 : <<include>>
Operator --> U3
Operator --> U4
U3 .> U5 : <<include>>
Approver --> U6
U6 .> U5 : <<include>>
Treasury --> U7
U7 .> U8 : <<extend>>
U6 .> U9 : <<include>>
Reconcile --> U10
U10 .> U9 : <<include>>
Fraud --> U11
Approver --> U12
"""
    ),
    "48_usecase_logistics_lineage": uml(
        """
title حالات استخدام التوريد وتتبع الأصل والعهدة
left to right direction
actor "موظف التوريد" as Procurement
actor "أمين المستودع" as Storekeeper
actor "مسؤول الأصول" as AssetOfficer
actor "المعتمد" as Approver
actor "المستلم" as Recipient
actor "الامتثال" as Compliance
rectangle "Admin.Logistics" {
  usecase "إنشاء أمر وخطوط توريد" as U1
  usecase "استلام كمية وربطها بالسطر" as U2
  usecase "زيادة المخزون أو إنشاء أصل" as U3
  usecase "توليد AssetTag" as U4
  usecase "طلب واعتماد عهدة" as U5
  usecase "إثبات التسليم والتوقيع" as U6
  usecase "إرجاع وفحص الأصل" as U7
  usecase "تسجيل فقد وفتح تحقيق" as U8
  usecase "تتبع النسب إلى محضر الاستلام" as U9
}
Procurement --> U1
Storekeeper --> U2
U2 .> U1 : <<include>>
U2 .> U3 : <<include>>
U3 .> U4 : <<extend>>
AssetOfficer --> U5
Approver --> U5
Recipient --> U6
U5 .> U6 : <<include>>
AssetOfficer --> U7
AssetOfficer --> U8
Compliance --> U8
U3 .> U9 : <<include>>
"""
    ),
    "49_usecase_security_files": uml(
        """
title حالات استخدام الخصوصية والملفات وثقة الخدمات
left to right direction
actor "خدمة المجال" as Service
actor "مسؤول الأمن" as Security
actor "مسؤول الخصوصية" as Privacy
actor "صاحب البيانات" as Subject
actor "المدقق" as Auditor
rectangle "الأمن والخصوصية" {
  usecase "رفع ملف إلى الحجر" as F1
  usecase "فحص البرمجيات الضارة وCDR" as F2
  usecase "ترقية الملف النظيف" as F3
  usecase "منح وصول كائني مؤقت" as F4
  usecase "تدقيق الوصول والحذف" as F5
  usecase "تسجيل طلب DSAR" as D1
  usecase "جمع القرار أو التصحيح أو المحو" as D2
  usecase "إدارة تعارض الأدوار" as S1
  usecase "إدارة جمهور ونطاق هوية الخدمة" as S2
  usecase "اختبار رفض الاتصال غير المخول" as S3
}
Service --> F1
F1 .> F2 : <<include>>
F2 .> F3 : <<extend>>
Service --> F4
F4 .> F3 : <<include>>
Auditor --> F5
Subject --> D1
Privacy --> D2
D2 .> D1 : <<include>>
Security --> S1
Security --> S2
S2 .> S3 : <<include>>
"""
    ),
    "50_dfd_current_level0": uml(
        """
title DFD المرجعي للمستوى صفر في خط الأساس الحالي
left to right direction
actor "مسؤول المنصة" as Platform
actor "المركز والفروع" as OrgUsers
actor "الموظف والمواطن" as PublicUsers
actor "الخزينة والأنظمة الخارجية" as External
rectangle "1.0 حوكمة الجهة والهيكل" as P1
rectangle "2.0 دورة حياة الموارد البشرية" as P2
rectangle "3.0 السياسات وسير العمل والرموز" as P3
rectangle "4.0 الرواتب والخزينة" as P4
rectangle "5.0 اللوجستيات والخدمات المساندة" as P5
rectangle "6.0 الملفات والخصوصية والهوية" as P6
database "D1 بيانات النواة والجهة" as D1
database "D2 قواعد مجالات الموارد البشرية" as D2
database "D3 السياسات والعمليات والرموز" as D3
database "D4 الرواتب والدفع" as D4
database "D5 الأصول والملفات والسجلات" as D5
queue "أحداث التكامل" as Bus
Platform --> P1 : Manifest وقواعد إسناد
OrgUsers --> P2 : معاملات واعتمادات
OrgUsers --> P3 : سياسات وتعريفات
OrgUsers --> P4 : دورات وقرارات تمويل
OrgUsers --> P5 : توريد وعهد وخدمات
PublicUsers --> P2 : طلبات وخدمة ذاتية
External --> P4 : إقرار وكشف تسوية
P1 --> D1
P2 --> D2
P3 --> D3
P4 --> D4
P5 --> D5
P6 --> D5
P1 --> Bus
P2 --> Bus
P3 --> Bus
P4 --> Bus
P5 --> Bus
Bus --> P6 : مراجع ملفات وهوية وتدقيق
"""
    ),
    "51_dfd_tenant_assignment": uml(
        """
title DFD لإسناد الجهة والحجر والمصالحة
left to right direction
actor "مسؤول منصة البيانات" as Admin
actor "المدقق ومالك الجهة" as Reviewer
rectangle "1.0 بناء خطة Dry Run" as P1
rectangle "2.0 تطبيق قواعد الإسناد" as P2
rectangle "3.0 حجر السجلات الملتبسة" as P3
rectangle "4.0 المصالحة والتحقق" as P4
rectangle "5.0 حل الحجر أو التراجع" as P5
database "D1 الجهات والوحدات" as D1
database "D2 قواعد الإسناد" as D2
database "D3 سجلات المجالات ذات TenantId" as D3
database "D4 سجل الحجر" as D4
database "D5 تقارير قبل وبعد وأثر القرار" as D5
Admin --> P1 : نطاق الترحيل
D1 --> P1
D2 --> P1
D3 --> P1
P1 --> Admin : خطة وتعارضات
Admin --> P2 : اعتماد التنفيذ
P2 --> D3 : TenantId صحيح
P2 --> P3 : سجل بلا دلالة فريدة
P3 --> D4
P2 --> P4
D3 --> P4
D4 --> P4
P4 --> D5 : counts وorphans وcross-tenant
Reviewer --> P5 : قرار موقع
P5 --> D3
P5 --> D4
P5 --> D5
"""
    ),
    "52_dfd_policy_workflow": uml(
        """
title DFD للسياسة وسير العمل والرموز
left to right direction
actor "مسؤولو الحوكمة" as Admin
actor "خدمة المجال" as Service
actor "المعتمد" as Approver
rectangle "1.0 إعداد ومحاكاة السياسة" as P1
rectangle "2.0 نشر وتقييم أو إنفاذ" as P2
rectangle "3.0 ربط وبدء سير العمل" as P3
rectangle "4.0 اعتماد وتوقيع" as P4
rectangle "5.0 معاينة أو توليد رمز" as P5
database "D1 إصدارات السياسات والقواعد" as D1
database "D2 دفتر الإنفاذ والروابط" as D2
database "D3 تعريفات ومثيلات سير العمل" as D3
database "D4 التوقيعات والأثر" as D4
database "D5 أنماط الرموز والعدادات والإصدارات" as D5
Admin --> P1 : مسودة وحقائق
P1 --> D1
Service --> P2 : tenant وfacts وcorrelation
D1 --> P2
P2 --> D2 : القرار وإصدار السياسة
P2 --> P3 : requirements وbinding
P3 --> D3
Approver --> P4 : موافقة أو رفض
P4 --> D3
P4 --> D4
Service --> P5 : النوع والسياق
P5 --> D5
P5 --> Service : الرمز والبيانات الوصفية
"""
    ),
    "53_dfd_advanced_payroll": uml(
        """
title DFD للرواتب والتمويل والدفع والتسوية
left to right direction
actor "مشغل الرواتب" as Operator
actor "المعتمد المالي" as Approver
actor "الخزينة" as Treasury
actor "المصرف أو قناة الدفع" as Bank
actor "المدقق" as Auditor
rectangle "1.0 حل إصدار القاعدة وتجميد المدخلات" as P1
rectangle "2.0 الحساب المقسم ونقاط التحقق" as P2
rectangle "3.0 التحقق والاعتماد والترحيل" as P3
rectangle "4.0 قرار التمويل والصرف الجزئي" as P4
rectangle "5.0 أوامر الدفع والإرسال" as P5
rectangle "6.0 التسوية والمتأخرات والحجوزات" as P6
database "D1 الإعدادات واللقطات" as D1
database "D2 المهام والأقسام وأسطر الراتب" as D2
database "D3 الدفعات والأوامر" as D3
database "D4 المتأخرات وكشوف المصرف" as D4
database "D5 تنبيهات الاحتيال وحجوزات الدفع" as D5
Operator --> P1
P1 --> D1
P1 --> P2
P2 --> D2
Approver --> P3
D2 --> P3
P3 --> D3 : PaymentBatch Pending
Treasury --> P4 : المبلغ وقواعد الأولوية
P4 --> D4 : ArrearsLedger
P4 --> P5
P5 --> D3 : PaymentOrders
P5 --> Bank : ملف أو API موقع
Bank --> P6 : إقرار وكشف تسوية
P6 --> D3
P6 --> D4
Auditor --> P6 : مراجعة أو حجز
P6 --> D5
"""
    ),
    "54_dfd_logistics_lineage": uml(
        """
title DFD للتوريد والاستلام وتتبع الأصل والعهدة
left to right direction
actor "موظف التوريد" as Procurement
actor "أمين المستودع" as Storekeeper
actor "مسؤول الأصول والمعتمد" as Assets
actor "المستلم والامتثال" as Recipient
rectangle "1.0 أمر وخطوط التوريد" as P1
rectangle "2.0 الاستلام والتحقق من الكمية" as P2
rectangle "3.0 زيادة المخزون أو إنشاء أصل" as P3
rectangle "4.0 ترميز الأصل" as P4
rectangle "5.0 طلب واعتماد العهدة" as P5
rectangle "6.0 التسليم والإرجاع أو الفقد" as P6
database "D1 أوامر وخطوط التوريد" as D1
database "D2 محاضر الاستلام والمخزون" as D2
database "D3 الأصول وسلسلة المصدر" as D3
database "D4 العهد وبلاغات الفقد" as D4
database "D5 مراجع الملفات ومحاضر PDF" as D5
Procurement --> P1
P1 --> D1
Storekeeper --> P2
D1 --> P2
P2 --> D2
P2 --> P3
P3 --> D2
P3 --> D3
P3 --> P4
P4 --> D3 : AssetTag
Assets --> P5
D3 --> P5
P5 --> D4
Recipient --> P6
P6 --> D4
P6 --> D5
P6 --> D3 : حالة الأصل
"""
    ),
    "55_dfd_file_security": uml(
        """
title DFD لحجر الملفات والفحص والمنح والخصوصية
left to right direction
actor "خدمة المجال أو المستخدم" as Caller
actor "ماسح البرمجيات الضارة" as Scanner
actor "مسؤول الخصوصية" as Privacy
actor "المدقق" as Auditor
rectangle "1.0 تحقق النوع وارفع إلى الحجر" as P1
rectangle "2.0 فحص وإعادة محاولة" as P2
rectangle "3.0 تنقية CDR وترقية النظيف" as P3
rectangle "4.0 تقييم المنحة وإصدار رابط مؤقت" as P4
rectangle "5.0 معالجة طلب صاحب البيانات" as P5
database "D1 بيانات الملف وحالة الفحص" as D1
database "D2 حاوية الحجر" as D2
database "D3 المخزن النشط" as D3
database "D4 منح الوصول وسجل التدقيق" as D4
database "D5 طلبات DSAR والقرارات" as D5
Caller --> P1 : الملف والمرجع
P1 --> D1
P1 --> D2
P2 --> Scanner
Scanner --> P2 : Clean أو Infected أو Error
P2 --> D1
P2 --> P3 : Clean فقط
P3 --> D3
P3 --> D1 : Active وSha256
Caller --> P4
D1 --> P4
D4 --> P4
P4 --> Caller : رفض أو presigned URL
P4 --> D4 : أثر الوصول
Caller --> P5 : طلب DSAR
Privacy --> P5 : قرار ومعالجة
P5 --> D5
Auditor --> D4
Auditor --> D5
"""
    ),
    "56_sequence_payroll_resumable": uml(
        """
title تسلسل تشغيل الرواتب القابل للاستئناف والصرف
actor "مشغل الرواتب" as Operator
participant "Payroll API" as Api
participant "Payroll Job Worker" as Worker
database "Payroll DB" as Db
participant "Policy and Approval" as Approval
participant "Payment Channel" as Channel
Operator -> Api : Start(runId)
Api -> Db : حل الإصدار وتثبيت InputSnapshot
Api -> Db : إنشاء Job وPartitions وLease
Api --> Operator : jobId
loop كل قسم ثم موظف
  Worker -> Db : الحصول على partition وcheckpoint
  Worker -> Db : معاملة SalaryLine ومكوناته
  Worker -> Db : تحديث cursor وProcessedCount
end
alt Pause أو lease منته
  Operator -> Api : Pause أو Resume
  Api -> Db : تغيير الحالة واستئناف غير المكتمل
else اكتمل الحساب
  Worker -> Db : Validate ثم ResultsFrozen
end
Operator -> Approval : طلب الاعتماد
Approval --> Api : Approved
Api -> Db : Post وإنشاء PaymentBatch Pending
Operator -> Api : Process payment
Api -> Db : FundingDecision وPaymentOrders
Api -> Channel : إرسال أوامر الدفع
Channel --> Api : Acknowledgement وStatement
Api -> Db : Reconcile أو Arrears أو PaymentHold
"""
    ),
    "57_activity_tenant_reconciliation": uml(
        """
title نشاط إسناد بيانات الجهة والحجر والمصالحة
start
:أخذ نسخة احتياطية وتثبيت نطاق الترحيل;
:تحميل الجهات والجذور وقواعد الإسناد;
:تشغيل Dry Run دون كتابة;
if (الخطة معتمدة؟) then (نعم)
  :إسناد السجلات ذات الدلالة الفريدة;
else (لا)
  :تعديل القواعد أو إلغاء النافذة;
  stop
endif
if (السجل بقي على الجهة الافتراضية؟) then (نعم)
  :إنشاء أو تحديث سجل الحجر;
else (لا)
  :تسجيل سبب الإسناد والجهة;
endif
:تشغيل المصالحة والـchecksums;
if (كل سجل منسوب أو محجور\nولا مراجع عابرة للجهات؟) then (نعم)
  :حفظ التقرير وتوقيع المراجعين;
else (لا)
  :إيقاف الإتاحة والتراجع أو المعالجة;
endif
:مراجعة سجلات الحجر وحلها بقرار مؤرخ;
stop
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
        "تشغل بواسطة `scripts/generate_thesis_diagrams.py` لإنتاج نسخ PNG داخل `rendered/`. "
        "تمثل الملفات 26 إلى 30 الخرائط المفاهيمية الحالية لملكية البيانات والتكامل، وتمثل الملفات 43 إلى 57 الحزمة المرجعية الحالية لحالات الاستخدام وDFD والرحلات الحديثة، "
        "أما مخططات ERD المختصرة والكاملة المستخرجة من Migration Snapshots فتولد في حزمة قاعدة البيانات المستقلة، "
        "بينما تحفظ حزم Draw.io السابقة خارج هذا المجلد بوصفها أثراً تحليلياً تاريخياً.\n",
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
