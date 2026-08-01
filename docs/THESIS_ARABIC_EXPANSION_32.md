### 3.2.2 مخطط حالات الاستخدام (Use Case)

**[شكل (3-1): مخطط حالات الاستخدام]**

**الغرض من المخطط:** يُظهر **Use Case Diagram** من يرى النظام (Actors) وماذا يمكنه أن يفعل (Use Cases)، دون الدخول في تفاصيل التنفيذ التقني. يساعد لجنة المناقشة على فهم نطاق النظام من منظور المستخدم.

**الفاعلون (Actors) ودورهم:**

| الفاعل | الدور في المؤسسة | أمثلة حالات استخدام |
|--------|-------------------|---------------------|
| `hr-admin` | مدير النظام | جميع الوحدات، حذف سجلات |
| `hr-core` | موظف الشؤون الشخصية | إدارة موظف، هيكل تنظيمي |
| `hr-contracts` | موظف الخدمة والعقود | عقود، إجازات، معالج تعيين |
| `payroll` | موظف الرواتب | دورة رواتب، ملفات أجور |
| `hr-employee` | موظف عادي | ESS: إجازة، سلفة، حضور |
| `citizen` | مواطن (بدون حساب) | تصفّح وظائف، تقديم طلب |

**حالات الاستخدام الرئيسية:** إدارة موظف، معالج تعيين، معالج فصل، الموافقة على إجازة، معالجة رواتب، تنفيذ نقل، حل شكوى، إصدار بطاقة، أرشفة وثيقة، تقديم إجازة عبر ESS.

**العلاقات:** علاقة `<<include>>` تربط «معالج التعيين» بـ «إدارة موظف» و«إصدار بطاقة» — أي لا يكتمل التعيين دونها. علاقة `<<extend>>` قد تربط «الموافقة على إجازة» بـ «ESS» عندما يقدّم الموظف الطلب ذاتياً.

**كيفية الرسم:** رسم المستطيل الكبير (System Boundary) بعنوان HRManagement، الفاعلون كـ stick figures على الجانبين، حالات الاستخدام كبيضاويات داخل الحدود، أسهم association بين الفاعل والحالة.

### 3.2.3 مخطط المكوّنات (Component)

**[شكل (3-2): مخطط المكوّنات]**

**الغرض:** يُبيّن **Component Diagram** المكوّنات البرمجية الكبرى واعتمادياتها — وليس خوادم النشر.

**المكوّنات:**
- **Gateway:** YARP + OIDC + Middleware تدقيق.
- **14 Service Components:** كل واحد يحتوي Domain + Infrastructure + Web.
- **Shared Components:** MessageContracts (39 حدث)، SharedKernel (BaseEntity)، ServiceDefaults (RBAC, Health).
- **Infrastructure:** PostgreSQL×15، RabbitMQ، Keycloak، File.Storage.

**العلاقات:** Gateway **depends on** Keycloak (مصادقة). كل Service **depends on** MessageContracts وServiceDefaults. Services **publish/consume** عبر RabbitMQ — لا depends-on مباشر service-to-service على HTTP للأعمال.

### 3.2.4 مخطط النشر (Deployment)

**[شكل (3-3): مخطط النشر — Docker Compose]**

**الغرض:** يُظهر **Deployment Diagram** أين تُشغَّل المكوّنات في بيئة التطوير/التجهيز.

**العقد (Nodes):**
- **Host Machine:** Docker Desktop أو Linux server.
- **Container postgresql:** منفذ 5433، 15 قاعدة بيانات.
- **Container rabbitmq:** 5672، 15672.
- **Container keycloak:** 8080.
- **Processes dotnet:** Gateway + 14 خدمة (أو containers في staging).

**الأسهم:** Client Browser → Gateway:5000 → Service:5010-5140. Services → PostgreSQL, RabbitMQ. Gateway → Keycloak.

---

### 3.3.1 المبدأ المعماري وشرح مخططات ER

**Database-per-Service** يعني أن كل خدمة **تملك** مخططها بالكامل. لا يوجد JOIN بين `hr_core_db.Employee` و`payroll_db.SalaryLine` — بل `EmployeeId` Guid في Payroll يشير منطقياً إلى Employee في HR.Core [4][13].

**[شكل (3-4): مخطط ER — HR.Core]**

**الغرض:** يوضّح العلاقات **داخل** قاعدة HR.Core. **Employee** في المركز: علاقات Cascade للملفات الفرعية (Profile, Document, Qualification). **OrgUnit** شجري (ParentId → self). **ApprovalRequest/Step** لسير موافقات عام. جداول المناوبات والتعاقب والتوظيف العام أُضيفت في v1.

**[شكل (3-5): مخطط ER — HR.Contracts]**

**الغرض:** يركز على **Contract** ← **ContractType**، **ServiceTransaction** كمحور للمعاملات، **HireChecklist/SeparationChecklist** لقوائم التحقق، **LeaveRequest/AdvanceRequest** للخدمة اليومية.

**[شكل (3-6): مخطط ER — Payroll]**

**الغرض:** **PayrollRun** ← **SalaryLine** ← **SalaryLineComponent**؛ **EmployeePayProfile** ← **PayGrade** ← **SalaryScale**؛ **PayrollConfiguration** للإعدادات التشغيلية.

**[شكل (3-7): مخطط ER المنطقي بين الخدمات]**

**الغرض:** **Logical Cross-Service ER** — EmployeeId كمحور بدون FK فعلي. HR.Core (master) → Contracts, Payroll, Health, IDCard (مراجع فقط). الأسهم المنقطة = أحداث RabbitMQ.

**ملاحظة على وصف الجداول:** يلي في الأقسام 3.3.2–3.3.16 وصف **كل جدول** بمفتاحه الأساسي وعلاقاته و**دوره الإداري** في الشعبة. الأعمدة الموروثة من `BaseEntity` (Id, TenantId, CreatedAt, UpdatedAt) و`AuditableEntity` (CreatedBy, UpdatedBy) تُكرَّر في كل جدول ولا تُذكر إلا عند الحاجة.
