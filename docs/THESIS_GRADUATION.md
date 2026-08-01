# أطروحة مشروع التخرّج

> **تعليمات التغليف:** النموذج العربي (نموذج-1) يُوضَع على الغلاف الأيمن، والنموذج الإنجليزي (نموذج-2) على الغلاف الأيسر، وفق منهجية جامعة الشمال الخاصة.

---

## نموذج (1) — صفحة الغلاف (عربي — الغلاف الأيمن)

<div align="center">

**الجمهورية العربية السورية**

**جامعة الشمال الخاصة**

**كلية الهندسة المعلوماتية**

**قسم هندسة البرمجيات ونظم المعلومات**

<br><br><br>

# نظام إدارة الموارد البشرية المتكامل
## (HRManagement)

<br><br><br>

**مشروع تخرّج أُعدّ لنيل درجة الإجازة في الهندسة المعلوماتية**

**باختصاص هندسة البرمجيات ونظم المعلومات**

<br><br>

**إعداد الطلاب:**

| | |
|---|---|
| عمر أحمد الخطاب | علي صالح أحمد الدحو |
| علي مصطفى السعيد | عبدالرحمن هيثم طحان |

<br><br>

**بإشراف**

**د. ماهر دبساوي**

<br><br>

**العام الدراسي 2025 – 2026**

</div>

---

## نموذج (2) — Cover Page (English — Left Cover)

<div align="center">

**Syrian Arab Republic**

**North Private University**

**Faculty of Informatics Engineering**

**Department of Software Engineering and Information Systems**

<br><br><br>

# Integrated Human Resources Management System
## (HRManagement)

<br><br><br>

**Graduation Project prepared to obtain bachelor's degree**

**of Informatics Engineering / Software Engineering and Information Systems**

<br><br>

**Prepared by:**

| | |
|---|---|
| Omar Ahmad Al-Khattab | Ali Saleh Ahmad Al-Dahou |
| Ali Mustafa Al-Saeed | Abdulrahman Haitham Tahhan |

<br><br>

**Supervised by**

**Dr. Maher Dubsawi**

<br><br>

**Academic Year 2025 – 2026**

</div>

---

# كلمة الشكر

الحمد لله رب العالمين، والصلاة والسلام على سيدنا محمد وعلى آله وصحبه أجمعين.

نتقدّم بجزيل الشكر والامتنان إلى **د. ماهر دبساوي** على إشرافه الأكاديمي الدؤوب وتوجيهاته القيّمة التي ساهمت في صقل هذا العمل وإنجازه على الوجه الأمثل.

كما نشكر **جامعة الشمال الخاصة** و**كلية الهندسة المعلوماتية** و**قسم هندسة البرمجيات ونظم المعلومات** على توفير البيئة العلمية والمادية التي مكّنتنا من إتمام هذا المشروع.

ولا ننسى زملاءنا في الدفعة الذين شاركونا النقاشات التقنية والأكاديمية، وكل من ساهم — بشكل مباشر أو غير مباشر — في إنجاز هذا العمل.

---

# الإهداء

**إلى والديّ**  
…على دعمهما المستمر وصبرهما طوال مسيرة دراستي.

**إلى عائلتي**  
…على تشجيعها الدائم وثقتها بي.

**إلى كل من آمن بقدرتي على الإنجاز**

---

# الملخص

**المشكلة:** تعاني شعب التنظيم والموارد البشرية في المؤسسات المدنية الكبيرة من تشتت البيانات بين فروع متعددة (الخدمة، الشؤون الشخصية، التنقلات، التدريب، الرواتب، الأرشيف، الامتثال)، ومن بطء تنسيق الإجراءات الإدارية — كالتعيين والفصل والإجازات والتنقلات — بسبب غياب منظومة معلومات موحّدة تربط هذه الوحدات مع الحفاظ على استقلالية بياناتها وحساسيتها.

**الهدف:** تصميم وتنفيذ نظام **HRManagement** لأتمتة عمليات شعبة التنظيم والموارد البشرية، مبنياً على **معمارية الخدمات المصغّرة (Microservices)** مع **14 خدمة مستقلة**، و**15 قاعدة بيانات PostgreSQL**، وتكامل **Event-Driven** عبر **RabbitMQ/MassTransit**، ومصادقة مركزية عبر **Keycloak/OpenID Connect**.

**المنهجية:** تحليل المتطلبات التنظيمية (`Requirements.md`)، تصميم Bounded Contexts وفق **Domain-Driven Design**، تنفيذ طبقات Domain/Infrastructure/Web لكل خدمة، إعداد بوابة **YARP**، وربط الخدمات بعقود رسائل مشتركة، مع اختبارات وحدة وتكامل.

**التقنيات:** .NET 10، ASP.NET Core MVC، Entity Framework Core، PostgreSQL 16، RabbitMQ 3.13، MassTransit، YARP، Keycloak 24، AdminLTE 3 (RTL)، Docker Compose، MediatR، Serilog.

**النتائج:** منظومة تشغيلية v1 تغطي دورة حياة الموظف (استقطاب → تعيين → خدمة → فصل → أرشفة)، مع 39 حدث تكامل، 20+ سياسة صلاحيات، بوابة موظف (ESS)، وبوابة توظيف عامة، و~180 جدولاً موزّعة، واختبارات CI/CD.

**الكلمات المفتاحية:** الموارد البشرية، الخدمات المصغّرة، PostgreSQL، RabbitMQ، Keycloak، .NET، Event-Driven Architecture.

---

# Abstract

**Problem:** Large civil organizations' HR divisions suffer from fragmented data across multiple branches (service, personnel, movements, training, payroll, archive, compliance) and slow coordination of administrative processes — hiring, separation, leave, transfers — due to the absence of an integrated information system that connects these units while preserving data autonomy and sensitivity.

**Objective:** To design and implement **HRManagement**, an integrated HR platform automating the Organization and Human Resources Division's operations, built on **Microservices Architecture** with **14 independent services**, **15 PostgreSQL databases**, **Event-Driven** integration via **RabbitMQ/MassTransit**, and centralized authentication through **Keycloak/OpenID Connect**.

**Methodology:** Requirements analysis from organizational specifications, Bounded Context design following **Domain-Driven Design**, implementation of Domain/Infrastructure/Web layers per service, **YARP** API Gateway setup, inter-service messaging via shared contracts, with unit and integration testing.

**Technologies:** .NET 10, ASP.NET Core MVC, Entity Framework Core, PostgreSQL 16, RabbitMQ 3.13, MassTransit, YARP, Keycloak 24, AdminLTE 3 (RTL), Docker Compose, MediatR, Serilog.

**Results:** An operational v1 system covering the employee lifecycle (recruitment → hire → service → separation → archive), with 39 integration events, 20+ authorization policies, Employee Self-Service (ESS) portal, public careers portal, ~180 distributed tables, and CI/CD testing.

**Keywords:** Human Resources, Microservices, PostgreSQL, RabbitMQ, Keycloak, .NET, Event-Driven Architecture.

---

# فهرس المحتويات

| # | العنوان | الصفحة |
|---|---------|--------|
| | **الغلاف العربي** | i |
| | **الغلاف الإنجليزي** | ii |
| | **كلمة الشكر** | iii |
| | **الإهداء** | iv |
| | **الملخص** | v |
| | **Abstract** | vi |
| | **فهرس المحتويات** | vii |
| | **قائمة الأشكال** | ix |
| | **قائمة الجداول** | x |
| **1** | **الفصل الأول: المقدمة** | 1 |
| 1.1 | مقدمة | 1 |
| 1.2 | فكرة المشروع | 2 |
| 1.3 | الهدف من المشروع | 3 |
| 1.4 | أهمية المشروع | 4 |
| 1.5 | الجهة المستفيدة | 5 |
| 1.6 | حدود المشروع | 6 |
| 1.7 | منهجية التنفيذ | 7 |
| **1.8** | **متطلبات النظام الوظيفية وغير الوظيفية** | **8** |
| **1.9** | **دراسة الجدوى** | **10** |
| **1.10** | **توزيع المهام وخطة العمل الزمنية** | **11** |
| **2** | **الفصل الثاني: الدراسة النظرية** | 13 |
| 2.1–2.11 | *(كما في النسخة الأصلية)* | 13–32 |
| **2.12** | **الدراسات السابقة والأنظمة المشابهة** | **33** |
| **3** | **الفصل الثالث: التطبيق العملي** | 36 |
| 3.1–3.13 | *(كما في النسخة الأصلية، مع تحديث 3.11)* | 36–122 |
| | **خاتمة عامة** | 123 |
| | **قائمة الاختصارات** | 125 |
| | **المراجع** | 127 |
| | **الملاحق** | 131 |

> ملاحظة: الأرقام أعلاه أرقام إرشادية مؤقتة فقط — بعد لصق كل الأقسام الجديدة في Word، استخدم **مرجع ← جدول المحتويات ← تحديث الحقل (F9)** ليُعاد ترقيم الصفحات تلقائياً بدقة.

---

# قائمة الأشكال

| # | عنوان الشكل | الصفحة |
|---|-------------|--------|
| شكل (1-1) | الهيكل التنظيمي لشعبة الموارد البشرية | 2 |
| شكل (2-1) | مخطط معمارية Microservices للنظام | 12 |
| شكل (2-2) | طبقات DDD في كل خدمة | 15 |
| شكل (2-3) | نموذج Event-Driven Integration | 17 |
| شكل (2-4) | مخطط تسلسل — تسجيل الدخول عبر OIDC | 27 |
| شكل (2-5) | مخطط تسلسل — Directory Sync عند IEmployeeCreated | 29 |
| شكل (3-1) | مخطط Use Case Diagram | 36 |
| شكل (3-2) | مخطط Component Diagram | 37 |
| شكل (3-3) | مخطط Deployment Diagram (Docker Compose) | 38 |
| شكل (3-4) | ER Diagram — HR.Core | 41 |
| شكل (3-5) | ER Diagram — HR.Contracts | 42 |
| شكل (3-6) | ER Diagram — Payroll | 43 |
| شكل (3-7) | ER Diagram — العلاقات المنطقية بين الخدمات | 44 |
| شكل (3-8) | Activity Diagram — مسار التعيين (Hire) | 53 |
| شكل (3-9) | Activity Diagram — مسار الفصل (Separation) | 54 |
| شكل (3-10) | Activity Diagram — دورة الرواتب | 55 |
| شكل (3-11) | Sequence Diagram — IEmployeeCreated | 56 |
| شكل (3-12) | Sequence Diagram — تسجيل الدخول عبر Gateway | 57 |
| شكل (3-13) | Flowchart — موافقة الإجازة | 58 |
| شكل (3-14) | مخطط RBAC — الأدوار والسياسات | 49 |
| شكل (3-15) | Class Diagram — BaseEntity و Shared Kernel | 61 |
| شكل (3-16) | واجهة لوحة البوابة الرئيسية | 77 |
| شكل (3-17) | واجهة معالج التعيين HireWizard | 78 |

---

# قائمة الجداول

| # | عنوان الجدول | الصفحة |
|---|--------------|--------|
| جدول (1-1) | أهداف المشروع التفصيلية | 3 |
| جدول (2-1) | مقارنة Microservices vs Monolith | 13 |
| جدول (2-2) | التقنيات المستخدمة ومبرراتها | 19 |
| جدول (2-3) | إعدادات Keycloak في HRManagement | 26 |
| جدول (3-1) | الخدمات والمنافذ وقواعد البيانات | 35 |
| جدول (3-2) | جداول HR.Core الرئيسية | 41 |
| جدول (3-3) | جداول HR.Contracts الرئيسية | 42 |
| جدول (3-4) | جداول Payroll الرئيسية | 43 |
| جدول (3-5) | أدوار Keycloak وصلاحياتها | 48 |
| جدول (3-6) | سياسات ASP.NET Core Authorization | 50 |
| جدول (3-7) | مصفوفة الأحداث (Event Matrix) | 81 |
| جدول (3-8) | REST APIs الصريحة | 73 |
| جدول (3-9) | نطاق v1 vs المؤجّل | 6 |
| جدول (3-10) | نتائج الاختبارات | 89 |

---

# الفصل الأول: المقدمة

## 1.1 مقدمة

شهدت المؤسسات المدنية في العقود الأخيرة تحولاً رقمياً متسارعاً يفرض عليها إعادة هيكلة أنظمة المعلومات الإدارية لتلبية متطلبات الكفاءة والشفافية والامتثال. وفي صميم هذه الأنظمة يبرز **نظام إدارة الموارد البشرية (Human Resources Management System — HRMS)** بوصفه العمود الفقري لإدارة القوى البشرية: من استقطاب الكفاءات وتوظيفها، مروراً بإدارة عقودها وخدمتها وتدريبها وتحريكها، وصولاً إلى معالجة رواتبها وأرشفة سجلاتها والتعامل مع شكاواها.

تُعد **شعبة التنظيم والموارد البشرية** — وفق الهيكل التنظيمي المدني المعتمد في متطلبات هذا المشروع — الجهة المركزية المسؤولة عن «تنظيم إمداد المؤسسة وفروعها بالقوى البشرية»، وهي مرجع التنظيم والملفات الشخصية والخدمة الوظيفية والتخطيط للقوى العاملة. وتضم الشعبة إدارات وفروعاً متعددة: إدارة الكادر الإشرافي، إدارة الموظفين التنفيذيين، إدارة السجلات المؤسسية، إدارة التطوير البرمجي وتحليل المعلومات، إدارة الهويات والبطاقات، إدارة الإدارة واللوجستيات، إدارة تقنية المعلومات، إدارة الامتثال والرقابة، وغيرها — كل منها يتولى مجالاً وظيفياً مستقلاً يتطلب أنظمة بيانات متخصصة.

**[مكان الشكل (1-1): الهيكل التنظيمي لشعبة الموارد البشرية — مخطط شجري يُستخرج من Requirements.md]**

رغم تنوع هذه الوحدات، تبقى **دورة حياة الموظف** عملية متكاملة: لا يمكن فصل «تعيين موظف» عن «إنشاء عقد» و«ربط راتب» و«إصدار بطاقة» و«أرشفة ملفه». وفي البيئات التقليدية، تُدار هذه العمليات عبر أنظمة منفصلة أو جداول Excel ومراسلات ورقية، مما يولّد تأخيراً وتكراراً وأخطاءً يصعب تتبعها.

انطلاقاً من هذا الواقع، يقدّم هذا المشروع **نظام HRManagement**: منظومة معلومات مؤسسية مبنية على **Microservices** و**Event-Driven Architecture**، تُترجم المتطلبات التنظيمية للشعبة إلى 14 خدمة برمجية مستقلة، مع بوابة مركزية ومصادقة موحّدة وواجهة عربية.

## 1.2 فكرة المشروع

تقوم فكرة المشروع على **إسقاط الهيكل التنظيمي للشعبة على حدود خدمات برمجية (Bounded Contexts)**، بحيث:

1. **كل فرع/مجال وظيفي = خدمة مستقلة** (مثلاً: HR.Core للملفات الشخصية، HR.Contracts للعقود والخدمة، Payroll للرواتب).
2. **كل خدمة تمتلك قاعدة بيانات PostgreSQL خاصة** (Database-per-Service) دون قيود FK عابرة.
3. **التكامل بين الخدمات يتم عبر أحداث RabbitMQ** (مثل `IEmployeeCreated`، `IContractActivated`، `ISeparationCompleted`) وليس عبر استعلامات مباشرة على قواعد بعضها.
4. **البوابة (Gateway)** توفّر نقطة دخول واحدة للمستخدم، مع **Keycloak** للمصادقة و**RBAC** للتفويض.
5. **مسارات العمل (Journeys)** — التعيين، الفصل، الإجازات، التنقلات — تُنفَّذ عبر معالجات (Wizards) وقوائم تحقق (Checklists) متعددة الخطوات مع تتبع Correlation ID.

بهذا، ينتقل النظام من «تطبيق واحد ضخم» إلى «منظومة خدمات متماسكة» تعكس طبيعة العمل الإداري: وحدات مستقلة تتعاون عبر أحداث موثّقة.

## 1.3 الهدف من المشروع

**الهدف العام:** بناء نظام معلومات متكامل لأتمتة عمليات شعبة التنظيم والموارد البشرية في مؤسسة مدنية.

**[جدول (1-1): أهداف المشروع التفصيلية]**

| # | الهدف | المؤشر |
|---|-------|--------|
| 1 | أتمتة إدارة الملفات الشخصية والسير | CRUD موظفين + هيكل تنظيمي + تقييمات |
| 2 | أتمتة العقود والخدمة | عقود، إجازات، سلف، فصل |
| 3 | تكامل التنقلات والتدريب | أحداث Transfer, Training → Payroll |
| 4 | دورة رواتب تشغيلية | Draft → Post + تعديلات inbound |
| 5 | إصدار الهويات والأرشفة | بطاقات + ArchiveItems |
| 6 | الامتثال والشكاوى | دورة شكوى + تأديب → Payroll |
| 7 | مصادقة وتفويض مركزي | Keycloak + 20 policy |
| 8 | بوابة موظف ESS | إجازات، سلف، حضور |
| 9 | بوابة توظيف عامة | `/careers` anonymous |
| 10 | قابلية التشغيل | Docker Compose + Health checks + CI |

## 1.4 أهمية المشروع

يُبرَر هذا المشروع من أربعة محاور: إداري، تقني، أكاديمي، وأمني. كل محور يُجيب على سؤال مختلف: **لماذا تحتاج الشعبة هذا النظام؟** و**لماذا اختيرت هذه المعمارية؟** و**ما قيمة المشروع للدراسة الجامعية؟**

**أهمية إدارية:** في الشعبة التقليدية، معاملة التعيين تمرّ بورقات بين فرع الخدمة، الشؤون الشخصية، الرواتب، الهويات، والأرشيف — ما يُطيل الأمد ويزيد احتمال فقدان مستند أو تعارض بين نسخ البيانات. **HRManagement** يُحوّل هذه المعاملة إلى **قائمة تحقق رقمية** (سبع خطوات من P1.1 إلى P7.1) تُحدَّث تلقائياً عند كل حدث (تفعيل عقد، إصدار بطاقة، ربط راتب...). الموظف المسؤول يرى في شاشة واحدة ما اكتمل وما تبقّى، دون الاتصال بكل فرع على حدة. كذلك، موافقة الإجازة تُطلق تحديثاً متزامناً في **Payroll** (تعديل راتب) و**HR.Core** (حالة OnLeave) و**Records.Archive** (أرشفة المعاملة) دون إعادة إدخال البيانات ثلاث مرات — وهذا يُقلّل الأخطاء البشرية ويُسرّع إنجاز المعاملات.

**أهمية تقنية:** المشروع يُطبّق **Microservices** و**DDD** و**Event-Driven Architecture** في مجال **HR** — مجال حساس يتطلب عزل بيانات صحية ومالية عن بعضها. اختيار **Database-per-Service** يعني أن تعديل مخطط الرواتب لا يُعطّل جداول الموظفين. اختيار **RabbitMQ** يعني أن خدمة **IDCard** يمكن أن تتعطل مؤقتاً دون إيقاف **HR.Core** عن إنشاء موظفين جدد. يُعدّ المشروع مرجعاً عملياً لطلاب **هندسة البرمجيات ونظم المعلومات** في **تحويل متطلبات مؤسسية** (أكثر من 2000 سطر في `Requirements.md`) إلى بنية قابلة للاختبار والنشر والتوسع الأفقي.

**أهمية أكاديمية:** وثائق المشروع (`Requirements.md`، مخططات ERD، مصفوفة 39 حدثاً) تُمثّل **خطاً أساساً** (Baseline) موثّقاً يمكن البناء عليه في الإصدارات اللاحقة: محرك رواتب قانوني كامل، نشر على **Kubernetes**، أو توسيع ESS. كما يُظهر المشروع كيف تُترجم مفاهيم المقررات (UML، أنماط التصميم، CI/CD) إلى كود فعلي — وليس مجرد نظريات معزولة.

**أهمية أمنية:** فصل **HR.Health** (بيانات طبية) و**Compliance** (شكاوى وتأديب) في قواعد PostgreSQL مستقلة، مع **RBAC** من **Keycloak** على خمس طبقات (Gateway → Module policy → Fine-grained → Org scope → ESS self-service)، يُقلّل مخاطر تسريب بيانات حساسة. موظف ESS يرى بياناته فقط؛ موظف **HR.Health** لا يصل لجداول الرواتب؛ مدير الفرع يرى موظفي وحدته فقط — وهذا يتوافق مع مبدأ **Least Privilege** في إدارة الهوية.

## 1.5 الجهة المستفيدة

**الجهة المالكة:** شعبة التنظيم والموارد البشرية في المؤسسة المدنية محل الدراسة — الجهة التي تُعرَّف المتطلبات وتُعتمد النتائج.

**مستفيدون مباشرون** — كل فرع يستفيد من خدمة أو أكثر:

| الفرع/الإدارة | الخدمة/الخدمات | نوع الاستفادة |
|---------------|----------------|---------------|
| فرع الخدمة والعقود | HR.Contracts | عقود، إجازات، تعيين، فصل |
| فرع الشؤون الشخصية | HR.Core, HR.Health | ملفات، هيكل، سجلات طبية |
| فرع التنقلات | HR.Movements | نقل، انتداب، إعارة |
| فرع التدريب | HR.Training | دورات، LMS، بعثات |
| فرع الرواتب | Payroll | دورات رواتب، ملفات أجور |
| إدارة الهويات | IDCard | بطاقات وصول |
| إدارة السجلات | Records.Archive | أرشفة معاملات |
| إدارة الامتثال | Compliance | شكاوى، تأديب |
| إدارة تقنية المعلومات | IT.Infra, Auth.Directory | تجهيز، حسابات |
| إدارة التحليلات | Dev.Analytics | مؤشرات وتقارير |
| الموظفون | ESS (عبر HR.Core/Contracts) | إجازة، سلف، حضور |
| المتقدمون | Public Careers | تقديم طلبات وظائف |

**ملاحظة حول السرية المؤسسية:**

حفاظاً على خصوصية بيانات الموظفين الصحية والمالية الواردة في متطلبات هذا المشروع، أُشير إلى الجهة المالكة بالتسمية العامة «المؤسسة المدنية محل الدراسة». اشتُقّت المتطلبات والهيكل التنظيمي الفعلي من وثيقة `Requirements.md` المعتمدة من الجهة، دون الإفصاح عن اسمها الرسمي بناءً على طلبها. لا يؤثر هذا الإخفاء على صحة التحليل أو واقعية الحالة الدراسية، إذ استُخدمت البيانات التنظيمية والوظيفية كما وردت دون تعديل جوهري.

## 1.6 حدود المشروع

يُحدِّد **نطاق v1** ما أُنجز فعلياً في المستودع، وما **مؤجّل** لإصدارات لاحقة — لتجنّب ادّعاء ميزات غير منفّذة:

**[جدول (3-9): نطاق v1 vs المؤجّل]**

| داخل النطاق (v1) | خارج النطاق (مؤجّل) |
|------------------|---------------------|
| CRUD + مسارات تعيين/فصل/إجازة | محرك رواتب قانوني كامل (ضرائب، تأمينات) |
| دورة رواتب تشغيلية + تعديلات أحداث | تكامل حضور كامل عبر أحداث |
| MassTransit + 39 حدث | بعض Dead-end events بدون مست consumer |
| Keycloak + Auth.Directory | Kubernetes production (Docker Compose فقط) |
| ESS جزئي (إجازة، سلف، عرض) | ESS خارجي كامل |
| ~100 شاشة MVC | كل كيان Domain له UI |

## 1.7 منهجية التنفيذ

اتُبعت منهجية **تكرارية** (Iterative) على سبع مراحل، من تحليل المتطلبات حتى التجهيز للإصدار:

1. **دراسة المتطلبات:** استندت مرحلة دراسة المتطلبات إلى مصدرين: (أ) مراجعة وثيقة `Requirements.md` المعتمدة من الشعبة (أكثر من 2000 سطر)، و(ب) **[مقابلات مع مسؤولي شعبة التنظيم والموارد البشرية / ملاحظة ميدانية لسير العمل الورقي الحالي / مراجعة أدلة إجرائية داخلية — حدّدوا ما ينطبق فعلياً]**. جرى تحليل هذه المصادر لاستخراج الهيكل التنظيمي (شكل 1-1)، ومسارات العمل الرئيسية (تعيين، فصل، إجازة)، والقيود التنظيمية على الصلاحيات.
2. **التصميم المعماري:** تحديد 14 Bounded Context، Database-per-Service، وعقود 39 حدثاً في MessageContracts.
3. **التنفيذ التكراري:** لكل خدمة: Domain (كيانات + قواعد) → Infrastructure (DbContext + Consumers) → Web (MVC).
4. **التكامل:** تسجيل MassTransit consumers، إعداد YARP Gateway، ربط Keycloak realm.
5. **الاختبار:** Unit (~30 ملفاً)، Integration (Testcontainers + PostgreSQL)، UI checklist يدوي.
6. **التوثيق:** README، IMPLEMENTED_BASELINE، THESIS_TECH_STACK، مخططات ERD لكل DbContext.
7. **التجهيز للإصدار:** RELEASE_READINESS_STAGING، PRODUCTION_RUNBOOK، docker-compose.production.

## 1.8 متطلبات النظام الوظيفية وغير الوظيفية

### 1.8.1 المتطلبات الوظيفية (Functional Requirements)

| الرمز | المتطلب | الخدمة المسؤولة |
|---|---|---|
| FR-01 | إدارة بيانات الموظفين (CRUD) والهيكل التنظيمي الشجري | HR.Core |
| FR-02 | تنفيذ دورة تعيين كاملة عبر معالج خطوات مع قائمة تحقق (P1.1–P7.1) | HR.Contracts |
| FR-03 | تنفيذ دورة فصل كاملة (استرداد أصول، تعطيل حسابات، أرشفة) | HR.Contracts + خدمات مرتبطة |
| FR-04 | تقديم طلبات إجازة/سلفة والموافقة عليها وفق سياسة ورصيد الموظف | HR.Contracts |
| FR-05 | احتساب دورة رواتب شهرية (Draft → Review → Approve → Post) | Payroll |
| FR-06 | إصدار بطاقة هوية وربطها بصلاحيات دخول | IDCard |
| FR-07 | أرشفة إلكترونية/فيزيائية للمعاملات المكتملة برقم مرجعي فريد | Records.Archive |
| FR-08 | تسجيل الشكاوى ومتابعتها حتى الإجراء التأديبي | Compliance |
| FR-09 | مصادقة موحّدة (SSO) وتفويض قائم على الأدوار عبر خمس طبقات | Gateway + Keycloak |
| FR-10 | بوابة خدمة ذاتية للموظف (ESS): إجازة، سلفة، حضور، مستندات | متعدد الخدمات عبر Gateway |
| FR-11 | بوابة توظيف عامة لتصفح الوظائف وتقديم طلب دون تسجيل دخول | HR.Core (`/careers`) |
| FR-12 | تتبّع تدقيقي (Audit Trail) لكل تعديل على بيانات حساسة | جميع الخدمات (EntityAuditLog) |

### 1.8.2 المتطلبات غير الوظيفية (Non-Functional Requirements)

| الرمز | المتطلب | معيار القبول المقترح | حالة القياس |
|---|---|---|---|
| NFR-01 | الأداء | زمن استجابة ≤ X ثانية لـ95% من الطلبات تحت Y مستخدم متزامن | **غير مُقاس بعد — يلزم اختبار حمل فعلي قبل المناقشة** |
| NFR-02 | التوفر | 99% Uptime لخدمتي HR.Core وPayroll عبر health checks | غير مُقاس بعد |
| NFR-03 | الأمان | HTTPS إلزامي في الإنتاج، لا كلمات مرور محلية، عزل بيانات Health/Compliance | مُطبَّق معمارياً |
| NFR-04 | قابلية التوسع | توسيع أفقي مستقل لكل خدمة (خصوصاً Payroll عند إغلاق الشهر) | مصمَّم، غير مُختبر تحت حمل حقيقي |
| NFR-05 | الاتساق | اتساق نهائي لا يتجاوز عدة ثوانٍ بين نشر الحدث واكتمال المعالجة | موثّق نظرياً (Outbox + Idempotency) |
| NFR-06 | سهولة الاستخدام | واجهة عربية RTL دون تدريب تقني متعمق | غير مُختبر مع مستخدمين حقيقيين (انظر 1.9) |
| NFR-07 | قابلية الصيانة | نشر/تحديث كل خدمة دون إيقاف كامل النظام | مُطبَّق معمارياً (Database-per-Service) |

> **توصية:** استخدموا أداة مثل **k6** أو **JMeter** لقياس NFR-01 وNFR-04 فعلياً، وأداة **coverlet** لقياس تغطية الكود في .NET، وأدرجوا الأرقام الحقيقية هنا قبل التسليم — لجنة المناقشة تُقدِّر الأرقام المُقاسة أكثر من الافتراضات النظرية.

## 1.9 دراسة الجدوى

### 1.9.1 الجدوى التقنية

يمتلك فريق العمل خبرة عملية بمنصة .NET وEF Core وDocker (موثّقة عبر تنفيذ 45+ مشروعاً في الحل). البنية التحتية المطلوبة (PostgreSQL، RabbitMQ، Keycloak) مفتوحة المصدر ومتاحة للنشر الذاتي دون قيود ترخيص، ما يجعل التبني التقني ممكناً داخل بيئة المؤسسة دون الاعتماد على مزوّد خارجي.

### 1.9.2 الجدوى الاقتصادية

جميع مكوّنات المكدّس التقني (`.NET`, `PostgreSQL`, `RabbitMQ`, `Keycloak`) مفتوحة المصدر ودون رسوم ترخيص سنوية — بخلاف الحلول التجارية الجاهزة (SAP SuccessFactors، Oracle HCM) التي تفرض اشتراكات سنوية لكل مستخدم. التكلفة الفعلية تنحصر في:

- وقت التطوير والصيانة (الموارد البشرية للفريق التقني).
- استضافة الخوادم الذاتية (Docker hosts).
- **[أدخلوا هنا تقديراً تقريبياً لتكلفة الحل البديل التجاري لعدد موظفي المؤسسة الفعلي، مقارنة بتكلفة التشغيل الذاتي المقدّرة — هذا الرقم يمنح القسم مصداقية كبيرة أمام اللجنة]**

### 1.9.3 الجدوى التشغيلية

يتطلب تشغيل النظام فريق دعم تقني (DevOps) قادراً على إدارة 14 خدمة و15 قاعدة بيانات ووسيط رسائل ونظام هوية — وهو تعقيد تشغيلي أعلى من نظام Monolith تقليدي (انظر مقارنة الأداء في القسم 2.12.1، مرجع [16]). يوصى بخطة تدريب لفريق تقني داخل المؤسسة قبل التسليم النهائي، أو التعاقد على دعم فني لفترة انتقالية.

## 1.10 توزيع المهام وخطة العمل الزمنية

> **[نموذج مقترح — عدّلوه ليعكس التوزيع الفعلي بينكم]**

### 1.10.1 توزيع المهام بين الطلاب

| الطالب | الخدمات/المكوّنات المسؤول عنها | المساهمة الرئيسية |
|---|---|---|
| عمر أحمد الخطاب | HR.Core, HR.Contracts, Gateway | التصميم المعماري العام + معالجات Hire/Separation Wizard |
| علي صالح أحمد الدحو | Payroll, HR.Health, HR.Movements | منطق الرواتب وقواعد الفترة، عزل البيانات الصحية |
| علي مصطفى السعيد | Auth.Directory, IDCard, IT.Infra, Admin.Logistics | تكامل Keycloak، دورة حياة الهوية والأصول |
| عبدالرحمن هيثم طحان | HR.Training, Records.Archive, Compliance, Dev.Analytics | التدريب، الأرشفة، الامتثال، التحليلات |
| الفريق كاملاً | MessageContracts, SharedKernel, ServiceDefaults, الاختبارات، التوثيق | مسؤولية مشتركة |

### 1.10.2 الخطة الزمنية (مطابقة لمراحل المنهجية في 1.7)

| المرحلة | الوصف | المدة التقديرية | الحالة |
|---|---|---|---|
| 1 | دراسة المتطلبات والهيكل التنظيمي | **[X] أسابيع** | مكتملة |
| 2 | التصميم المعماري (14 Bounded Context + عقود الأحداث) | **[X] أسابيع** | مكتملة |
| 3 | التنفيذ التكراري لكل خدمة | **[X] أسابيع** | مكتملة |
| 4 | التكامل (MassTransit, YARP, Keycloak) | **[X] أسابيع** | مكتملة |
| 5 | الاختبار (Unit + Integration) | **[X] أسابيع** | مكتملة جزئياً — يلزم استكمال اختبار الأداء |
| 6 | التوثيق (الأطروحة والملفات المرجعية) | **[X] أسابيع** | جارية |
| 7 | التجهيز للإصدار (Staging Readiness) | **[X] أسابيع** | جارية |

*(أدخلوا التواريخ/المدد الفعلية؛ يمكن تحويل هذا الجدول لاحقاً إلى مخطط Gantt بصري في PowerPoint أو Excel لعرضه أثناء المناقشة.)*

---

# الفصل الثاني: الدراسة النظرية

## 2.1 المفاهيم الأساسية

يُعدّ فهم المفاهيم النظرية التي يستند إليها مشروع **HRManagement** شرطاً أساسياً لقراءة الفصل التطبيقي؛ إذ تُترجم هذه المفاهيم إلى قرارات معمارية ملموسة: تقسيم النظام إلى أربع عشرة خدمة، عزل قواعد البيانات، وتبادل الأحداث عبر **RabbitMQ**. يستعرض هذا القسم المفاهيم المركزية وعلاقتها المباشرة بمتطلبات شعبة التنظيم والموارد البشرية في المؤسسة المدنية محل الدراسة.

### 2.1.1 نظام إدارة الموارد البشرية (HRMS)

**نظام إدارة الموارد البشرية (Human Resources Management System — HRMS)** هو منظومة معلومات تدعم دورة حياة القوى البشرية: التخطيط، الاستقطاب، التوظيف، إدارة الأداء، التعويضات، التدريب، التنقلات، والامتثال [13]. في البيئات المدنية الكبيرة، لا يقتصر HRMS على «سجل موظف» بل يشمل فروعاً متخصصة — الشؤون الشخصية، الخدمة والعقود، الصحية، الرواتب، الأرشيف — كما ينعكس في الهيكل التنظيمي للشعبة.

في مشروع **HRManagement**، يُوسَّع مفهوم HRMS ليشمل **أربعة عشر مجالاً وظيفياً تُمثَّل بخدمات مستقلة، إضافة إلى بوابة مركزية (Gateway)**: **HR.Core** للملفات المرجعية، **HR.Contracts** للعقود والإجازات، **Payroll** للرواتب، **HR.Health** للشؤون الصحية، **Compliance** للشكاوى والتأديب، وغيرها. يغطي النظام مساراً متكاملاً من **الاستقطاب** (`PublicJobPosting`, `CitizenApplication`) إلى **الأرشفة** (`ArchiveItem`)، مع بوابة موظف (**ESS**) وبوابة توظيف عامة (`/careers`).

### 2.1.2 Bounded Context (السياق المحدود)

في **Domain-Driven Design**، يُعرَّف **Bounded Context** بأنه حدود لغوية ونموذجية تضمن أن كل مصطلح له معنى واحد داخل السياق [1]. مثلاً: كيان **Employee** في **HR.Core** يحمل الاسم الكامل والرقم الوطني والهيكل التنظيمي، بينما **EmployeeId** في **Payroll** مرجع مالي فقط (`EmployeePayProfile`) دون تكرار بيانات حساسة.

يُطبِّق المشروع هذا المبدأ عبر **Database-per-Service**: كل DbContext — `HRCoreDbContext`, `ContractsDbContext`, `PayrollDbContext` — يملك جداوله دون **Foreign Keys** عابرة للقواعد. التكامل يتم بمراجع **Guid** (`EmployeeId`, `OrgUnitId`) وأحداث تكامل (`IEmployeeCreated`, `IContractActivated`) [4].

### 2.1.3 Eventual Consistency (الاتساق النهائي)

في الأنظمة الموزعة، لا تُضمَن المعاملة الذرية (**ACID transaction**) عبر جميع الخدمات في لحظة واحدة؛ بل يُقبَل **تأخير قصير** حتى تُستهلك الأحداث وتُحدَّث الخدمات **المتأثرة لاحقاً** [4]. عند تعيين موظف، ينشر **HR.Core** حدث `IEmployeeCreated`؛ وتستهلكه **Payroll** لإنشاء `EmployeePayProfile` و**Auth.Directory** لإنشاء مستخدم **Keycloak** — قد يمرّ بضع ثوانٍ قبل اكتمال الربط.

يُخفِّف **HRManagement** مخاطر إعادة التسليم عبر: **Outbox Pattern** (كتابة الحدث مع المعاملة في نفس DB)، **Idempotency** عبر جدول `IntegrationMessageReceipt` بمفتاح `(MessageId, ConsumerType)` فريد، و**Correlation ID** (`X-Correlation-ID`) للتتبع [13].

### 2.1.4 RBAC (Role-Based Access Control)

**التحكم بالوصول القائم على الأدوار (RBAC)** يفصل **المصادقة** (Authentication — «من أنت؟») عن **التفويض** (Authorization — «ماذا يُسمح لك؟») [15]. في الأنظمة الإدارية، لا يكفي «تسجيل الدخول» — موظف ESS يجب ألا يرى رواتب زملائه.

في **HRManagement**: **Keycloak** يُصدِر **19 دوراً** (`hr-core`, `payroll`, `hr-employee`, `branch-manager`...) تُضمَّن في JWT. مشروع `HrManagementAuthorizationExtensions` يُحوّلها إلى **20 سياسة ASP.NET Core** (`Module.HrCore`, `Payroll.Process`...).

**خمس طبقات تطبيق:**
1. **Gateway** — رفض غير المسجّلين
2. **GatewayPathAuthorizationMiddleware** — Module policy على المسار
3. **HrModuleAuthorizationFilter** — فحص لكل Controller action
4. **`[Authorize(Policy)]`** — صلاحيات دقيقة (HireExecute, Post...)
5. **Cadre/Org scoping** — مدير فرع يرى `scope_org_unit_id` فقط

هذا يُحقِّق **Least Privilege** — أقل صلاحية لأداء المهمة.

---

## 2.2 معمارية الخدمات المصغّرة (Microservices)

### 2.2.1 التعريف والمبررات

**معمارية الخدمات المصغّرة (Microservices Architecture)** تقسّم التطبيق إلى خدمات صغيرة مستقلة قابلة للنشر والتوسع بمعزل عن بعضها، تتواصل عبر HTTP أو **Message Broker** [2][3]. اختير هذا النمط لأن متطلبات شعبة HR — أكثر من 2000 سطر في `Requirements.md` — تضم مجالاتاً وظيفية متباعة الحساسية: بيانات صحية (**HR.Health**) منفصلة عن شكاوى (**Compliance**) ورواتب (**Payroll**).

**[شكل (2-1): مخطط معمارية Microservices للنظام]**

يُمثِّل الشكل (2-1) **المخطط المعماري الكلي** للنظام: كيف يرى المستخدم منظومة واحدة، بينما خلف الكواليس تعمل خدمات مستقلة. فهم هذا المخطط ضروري قبل قراءة تفاصيل كل خدمة في الفصل الثالث.

**طبقة العميل (Client Layer):** متصفح الويب يتصل بعنوان واحد (`http://localhost:5000`). المستخدم قد يكون موظف شعبة HR، موافقاً إدارياً، موظفاً عادياً (ESS)، أو مواطناً على بوابة `/careers` — دون معرفة عدد الخدمات الداخلية.

**طبقة البوابة (API Gateway — YARP):** تستقبل كل الطلبات، تتحقق من الهوية عبر **Keycloak** (OIDC)، تطبّق **RBAC**، ثم تُوجِّه `/ui/hr-core/...` إلى خدمة HR.Core على المنفذ 5010، وهكذا. هذا يُخفِي تعقيد المنافذ عن المستخدم.

**طبقة الخدمات (Application Services):** أربع عشرة خدمة **ASP.NET Core MVC**، كل منها ثلاث طبقات (Domain, Infrastructure, Web). مثال: **HR.Core (5010)** للملفات، **Payroll (5060)** للرواتب.

**طبقة التكامل (Integration Layer):** **RabbitMQ** مع **MassTransit** — 39 حدثاً يربط الخدمات دون استعلامات مباشرة على قواعد بعضها.

**طبقة البيانات (Data Layer):** خمس عشرة قاعدة **PostgreSQL** — قاعدة لكل خدمة + تدقيق البوابة.

**طبقة الهوية (Identity Layer):** **Keycloak** realm `hrmanagement` — مصدر الأدوار (`hr-core`, `payroll`...).

**رمزية الأسهم:** أسهم صلبة = HTTP عبر YARP؛ أسهم متقطعة = أحداث AMQP. **لا يوجد** سهم FK بين قواعد البيانات — التكامل عبر `EmployeeId` وأحداث فقط.

**[جدول (2-1): مقارنة Microservices vs Monolith]**

| المعيار | Monolith | Microservices (HRManagement) |
|---------|----------|------------------------------|
| النشر | وحدة واحدة | 14 خدمة + Gateway مستقلة |
| قاعدة البيانات | DB واحدة | 15 DB (Database-per-Service) |
| التوسع | عمودي | أفقي لكل خدمة (مثلاً Payroll) |
| العزل | ضعيف | قوي (Health ≠ Payroll ≠ Compliance) |
| التعقيد التشغيلي | منخفض في البداية | مرتفع (15 DB + RabbitMQ + Keycloak) |
| الملاءمة | مشاريع صغيرة | 13+ مجال HR مؤسسي |

### 2.2.2 قاعدة بيانات لكل خدمة (Database-per-Service)

كل خدمة تملك **DbContext** و**Migrations** مستقلة على قاعدة PostgreSQL منفصلة (منفذ **5433** في Docker Compose). مثال: `hr_core_db` لـ **HRCoreDbContext**، `payroll_db` لـ **PayrollDbContext**. 

**لماذا هذا مهم؟** لأن تعديل جدول رواتب لا يُعطّل جدول موظفين. كما يُمكّن فرقاً مختلفة — فريق التدريب وفريق الرواتب — من العمل بالتوازي دون تعارض على مخطط واحد [4]. ثمن ذلك: التكامل يصبح عبر **أحداث** لا عبر JOIN SQL.

---

## 2.3 Domain-Driven Design (DDD)

### 2.3.1 الطبقات في كل خدمة

في **Domain-Driven Design**، يُفضَّل فصل **قواعد العمل** عن **البنية التحتية** عن **واجهة المستخدم** — حتى يمكن اختبار منطق التعيين أو الرواتب دون تشغيل متصفح أو قاعدة بيانات حقيقية. يُكرَّر هذا الهيكل في كل واحدة من الخدمات الأربع عشرة.

**[شكل (2-2): طبقات DDD في كل خدمة]**

يُبيِّن الشكل (2-2) — **DDD Layered Architecture** — الهيكل المتكرر في كل خدمة:

```
{Service}.Domain/         ← Entities, Enums, Domain Services, Business Rules
{Service}.Infrastructure/ ← DbContext, Consumers, Repositories, Migrations
{Service}.Web/            ← Controllers, Razor Views, Program.cs
```

**طبقة النطاق (Domain Layer):** تحتوي «قلب» الخدمة — الكيانات (`Employee`, `Contract`, `PayrollRun`) وقواعد العمل الخالصة. مثال: `HireWorkflowRules` تتحقق من اكتمال خطوات قائمة التحقق (P1.1–P7.1) قبل إغلاق معاملة التعيين؛ و`PayrollPeriodRules` تحسب أيام العمل والعطل والتناسب. هذه الطبقة **لا تعرف** EF Core ولا RabbitMQ — فقط C# ومنطق HR.

**طبقة البنية التحتية (Infrastructure Layer):** تُنفِّذ ما تحتاجه Domain من تقنيات: **EF Core** للتخزين، مستهلكو **MassTransit** للأحداث، **Outbox** لضمان النشر الآمن. مثال: `EmployeeCreatedConsumer` في **Payroll.Infrastructure** يستقبل `IEmployeeCreated` وينشئ `EmployeePayProfile`.

**طبقة الويب (Web Layer):** واجهات **MVC** العربية باتجاه **RTL** مع **AdminLTE 3** و**HRManagement.Web.Shared**. Controllers هنا «رفيعة»: تستقبل الطلب، تستدعي MediatR أو Domain Service، وتُعيد View — دون منطق أعمال معقد.

**لماذا هذا التقسيم؟** لأن تغيير تصميم شاشة التعيين لا يجب أن يكسر حساب الرواتب؛ وتغيير مخطط PostgreSQL لا يجب أن يغيّر قواعد الموافقة على الإجازة.

### 2.3.2 Shared Kernel (النواة المشتركة)

في **DDD**، يُعرَّف **Shared Kernel** بأنه **جزء محدود ومُتفَق عليه** من النموذج يُشاركه أكثر من Bounded Context — دون أن يتحول إلى «قاعدة بيانات مشتركة» أو «مكتبة منطق أعمال ضخمة» [1]. المشكلة التي يحلها: في نظام موزّع، كل خدمة تحتاج **نفس شكل المفتاح الأساسي** (`Guid Id`) و**نفس تعريف الحالة الوظيفية** (`EmploymentStatus.Active`) — لكن لا يجب أن تشارك **Employee** ككيان كامل بين HR.Core وPayroll.

**مشروع HRManagement.SharedKernel** يُحقِّق هذا التوازن: يوفّر **بنية تحتية مشتركة** دون **Domain logic مشترك**.

**BaseEntity** — الصنف الأب لجميع الكيانات:

| الخاصية | الدور في النظام |
|---------|-----------------|
| `Id` (Guid) | مفتاح أساسي عالمي — يُستخدم كـ `EmployeeId` عبر الخدمات |
| `TenantId` | دعم تعدد المستأجرين مستقبلاً (مؤسسة/فرع) |
| `CreatedAt`, `UpdatedAt` | طوابع زمنية UTC — موحّدة عبر كل الجداول |

**AuditableEntity** — يرث من `BaseEntity` ويضيف `CreatedBy` و`UpdatedBy` (Guid?) لتتبع **من** أنشأ أو عدّل السجل — مطلب إداري في الشعبة للمراجعة والمساءلة.

**EntityAuditLog** — جدول/كيان تدقيق مستقل: يسجّل `EntityType`, `EntityId`, `Action` (Created/Updated/Deleted), `ChangedFields` (JSON), `UserId`. يُكرَّر في كل DbContext لتمكين **تتبع التغييرات** دون JOIN بين قواعد.

**IntegrationMessageReceipt** — يدعم **Idempotency**: مفتاح فريد `(MessageId, ConsumerType)` يمنع معالجة نفس حدث RabbitMQ مرتين — حاسم عند إعادة تسليم الرسائل في Payroll.

**Enums مشتركة** — قيم موحّدة عبر الخدمات:

| Enum | القيم | لماذا مشترك؟ |
|------|-------|--------------|
| `EmploymentStatus` | Active, OnLeave, Separated... | HR.Core وPayroll وAuth.Directory يفهمون «موظف نشط» بنفس المعنى |
| `Gender`, `MaritalStatus` | — | بيانات ديموغرافية موحّدة في التقارير |
| `EmployeeCadre` | — | فلترة الصلاحيات (كادر إشرافي/تنفيذي) |
| `ApprovalStatus`, `DocumentType`, `ContactType` | — | تجنّب تعارض التعريفات بين Contracts وCore |

**ما لا يدخله Shared Kernel (عمداً):** لا يوجد فيه `Employee`, `Contract`, `PayrollRun` — هذه كيانات Domain خاصة بكل خدمة. لا يوجد فيه `HireWorkflowRules` — قواعد التعيين تخص **HR.Contracts** فقط. هذا **يمنع تسرب Domain** بين الخدمات: إذا احتاجت Payroll «حالة موظف»، تستهلك حدث `IEmployeeStatusChanged` ولا تستورد كيان Employee من HR.Core.

**الفائدة العملية:** عند إضافة خدمة جديدة (مثلاً v2)، تُورِث كياناتها من `AuditableEntity` فوراً — دون إعادة تعريف Id وCreatedAt. وعند مراجعة لجنة المناقشة، مخطط **Class Diagram** في الشكل (3-15) يُظهر هذه الوراثة بوضوح.

### 2.3.3 Domain Services في المشروع

**Domain Services** هي أصناف C# تحمل قواعد أعمال **لا تنتمي لكيان واحد** — مثل حساب أيام الفترة أو التحقق من اكتمال قائمة تحقق. في **HRManagement**، تُوزَّع على الخدمات حسب Bounded Context:

| Domain Service | الخدمة | الوظيفة |
|----------------|--------|---------|
| `EmployeeLifecycleService` | HR.Core | إدارة دورة حياة الموظف والحالات |
| `ApprovalWorkflowService` | HR.Core | سير موافقات عام (`ApprovalRequest/Step`) |
| `ContractWorkflowRules`, `HireWorkflowRules` | HR.Contracts | قواعد التعيين والعقود |
| `PayrollPeriodRules` | Payroll | أيام الفترة، العطل، التناسب |
| `SuccessionReadinessRules`, `ShiftAllowanceRules` | HR.Core | التعاقب والمناوبات |
| `LearningCompletionRules` | HR.Training | إتمام التعلم الإلكتروني |

**مثال تطبيقي:** عند موافقة إدارة على التعيين، `HireWorkflowRules` تفحص أن بنود P1.1–P7.1 مكتملة قبل السماح بنشر `IHireCaseCompleted` — وهذا المنطق **لا يعيش في Controller** بل في Domain حيث يمكن اختباره بـ Unit Tests.

---

## 2.4 Event-Driven Architecture

### 2.4.1 نموذج التكامل بالأحداث

عندما تُكمِل **HR.Core** إنشاء موظف، يجب أن تعلم **Payroll** و**Auth.Directory** و**IDCard** — لكن **لا يجوز** لـ Payroll أن تستعلم مباشرة من `hr_core_db`. **Event-Driven Architecture** تحل هذا: الخدمة الناشرة تُعلِن «حدثاً» (`IEmployeeCreated`) والخدمات الأخرى **تختار** الاستجابة.

**[شكل (2-3): نموذج Event-Driven Integration]**

يُظهر الشكل (2-3) تدفقاً نموذجياً من أربع خطوات:

1. **خدمة الناشر (Publisher):** مثلاً **HR.Core** تنفّذ معاملة DB (INSERT Employee) وتكتب **OutboxMessage** في نفس commit — ضمان أن الحدث لا يُنشر إلا إذا حُفظ الموظف فعلاً.
2. **MassTransit:** يقرأ Outbox وينشر الحدث إلى **RabbitMQ Exchange** المخصص لنوع الرسالة.
3. **مستهلكون متعددون (Consumers):** **HR.Contracts**, **Payroll**, **IDCard**... يستقبلون **بالتوازي** — لا ينتظر أحدهم الآخر.
4. **Idempotency:** كل Consumer يفحص `IntegrationMessageReceipt` قبل المعالجة — إذا وُجد `(MessageId, ConsumerType)` يتخطى التنفيذ.

**الفرق عن HTTP:** HTTP مناسب للاستعلام الفوري («ما اسم هذا الموظف؟»)؛ الأحداث مناسبة للإعلان («وُلِد موظف جديد — حدّثوا أنفسكم»).

### 2.4.2 عقود الأحداث

**HRManagement.MessageContracts** مشروع مستقل يحتوي **39 واجهة حدث** في مجلد `Events/` — مثل `IEmployeeCreated`, `ILeaveApproved`, `ISeparationCompleted`. كل واجهة تُعرِّف **عقداً** (Contract): ما البيانات التي يحملها الحدث؟ هذا يُمكّن Publisher وConsumer من الاتفاق دون coupling على DbContext.

**SchemaVersion:** كل حدث يحمل رقم إصدار — إذا أُضيف حقل جديد في v2، المستهلكون القديمون لا ينكسرون.

**Outbox Pattern:** خدمات **HR.Core**, **HR.Contracts**, **Payroll** تستخدم `AddPostgresOutbox` — جداول `OutboxMessage` و`OutboxState` في قاعدة الخدمة نفسها. يحل مشكلة: «حُفظ الموظف لكن فشل نشر الحدث» أو العكس [4][7].

**Idempotency:** جدول `IntegrationMessageReceipt` — UK: `(MessageId, ConsumerType)` — في **HR.Core**, **Payroll**, **HR.Health**, **Admin.Logistics**, **IT.Infra**. يمنع أن يُنشأ `EmployeePayProfile` مرتين لنفس `IEmployeeCreated`.

**Correlation:** رأس HTTP `X-Correlation-ID` يُمرَّر من Gateway عبر كل الطلبات إلى Serilog و`/Home/Operations` — يربط سجل Gateway بسجلات الخدمات والأحداث في **تتبع تشغيلي واحد**.

---

## 2.5 .NET 10 و ASP.NET Core MVC

### 2.5.1 منصة .NET 10

يُبنى مشروع **HRManagement** على **.NET 10 SDK** [5] — أحدث إصدار LTS من منصة Microsoft الموحّدة التي تجمع **Runtime**، **مكتبات BCL**، و**أدوات البناء** في حزمة واحدة. اختيرت هذه المنصة لأسباب عملية مرتبطة بطبيعة المشروع:

| المعيار | لماذا .NET 10؟ |
|---------|----------------|
| **Kestrel** | خادم ويب عالي الأداء يتحمل 14 خدمة MVC متزامنة |
| **OpenID Connect** | حزم `Microsoft.AspNetCore.Authentication.OpenIdConnect` جاهزة لـ Gateway |
| **YARP** | مكتبة reverse proxy رسمية من Microsoft متوافقة مع ASP.NET Core |
| **EF Core 10** | ORM موحّد لـ 15 DbContext مع Migrations مستقلة |
| **C# 13** | primary constructors، collection expressions — تقليل boilerplate |

**بنية الحل `HRManagement.slnx`:**

```
HRManagement.slnx
├── src/Services/{Module}/     ← 14 مجال × 3 طبقات (Domain, Infrastructure, Web)
├── src/Gateway/               ← HRManagement.Gateway (YARP + OIDC)
├── src/Shared/                ← SharedKernel, ServiceDefaults, MessageContracts, Web.Shared
└── src/Tests/                 ← Unit, Integration, E2E
```

أكثر من **45 مشروعاً** في الحل — كل خدمة **مستقلة قابلة للنشر** (deployable unit) لكنها تشترك في **ServiceDefaults** و**SharedKernel** لتجنب تكرار البنية التحتية. في التطوير، `dotnet run --project src/Services/HRCore/HR.Core.Web` يشغّل خدمة واحدة على منفذها (5010–5140)؛ في Docker، نفس المشروع يُبنى عبر **multi-stage Dockerfile** ويُشغَّل على 8080 داخل الحاوية.

**الغرض الأكاديمي:** .NET 10 ليست «أحدث إصدار» لذاتها — بل منصة تُمكّن **Microservices** على .NET دون تشتت إطارات: MVC، JWT، MassTransit، وHealth Checks من نفس النظام البيئي.

### 2.5.2 ASP.NET Core MVC

اختير **ASP.NET Core MVC** [5] لأن واجهات HR **إدارية** وليست تسويقية: نماذج معقدة (عشرات الحقول في ملف الموظف)، جداول بيانات قابلة للفرز والتصفية، ومعالجات متعددة الخطوات (**HireWizard** — 5 خطوات، **SeparationWizard** — 4 خطوات). **Server-side Rendering (SSR)** بـ **Razor Views** يناسب v1 أكثر من SPA لأن:

1. **الأمان:** التحقق من RBAC يتم على الخادم قبل إرسال HTML — لا يمكن للمتصفح تجاوز `[Authorize]`.
2. **البساطة:** فريق التطوير (4 طلاب) يعمل بنمط واحد (Controller → View) دون إدارة state منفصل في React/Vue.
3. **AntiForgery:** نماذج POST تُحمى تلقائياً بـ `@Html.AntiForgeryToken()` — أمر حاسم في نماذج HR الحساسة (موافقة إجازة، ترحيل راتب).

**Pipeline كل خدمة Web** (مثال: `HR.Core.Web/Program.cs`):

| # | الخطوة | الغرض |
|---|--------|-------|
| 1 | `UseHrManagementSerilog()` | structured logging مع CorrelationId |
| 2 | `AddHrModuleAuthorizeFilter(Module.HrCore)` | RBAC على كل action |
| 3 | `AddRepositories<TContext>()` | Repository + Unit of Work |
| 4 | `AddServiceMassTransit()` | RabbitMQ + Outbox + Consumers |
| 5 | `AddModuleAuthentication()` | JWT Bearer (خلف Gateway) |
| 6 | `AddHrServiceHealthChecks<TContext>()` | `/health`, `/health/live`, `/health/ready` |
| 7 | `MapControllerRoute` | `{controller}/{action}/{id?}` |
| 8 | `UseHrCorrelationId()` | ربط HTTP ↔ RabbitMQ في السجلات |

**Serilog** [13] يُسجّل **structured logs** (JSON-like properties) — كل طلب HTTP يحمل **CorrelationId** يُمرَّر تلقائياً إلى headers رسائل MassTransit عبر `CorrelationIdConsumeFilter`. في `/Home/Operations` يعرض المشغّل آخر الأخطاء وعمق طوابير `*_error` — دون الدخول إلى RabbitMQ Management UI.

**PathBase:** عند التشغيل خلف Gateway، `ASPNETCORE_PATHBASE=/ui/hr-core` يضمن أن روابط `_Layout` وstatic files تعمل تحت البادئة `/ui/hr-core/...` — وإلا تُكسَر الصور والروابط عند reverse proxy.

### 2.5.3 AdminLTE و Web.Shared

**AdminLTE 3** إطار واجهة Bootstrap جاهز مع دعم **RTL** وخط **ITF Qomra Arabic** — يوفّر `_Layout.cshtml` موحّداً (شريط جانبي، تنبيهات، بطاقات). **HRManagement.Web.Shared** (RCL — Razor Class Library) يُشارَك بين 14 خدمة:

- **`_HrSharedFooter`** — تذييل موحّد (إصدار، حقوق)
- **`_HrAlerts`** — عرض `TempData["Success"]` / `TempData["Error"]` بعد POST
- **`_HrBreadcrumb`** — مسار التنقل (Dashboard → Employees → Details)
- **Partial views** للبطاقات والأزرار — نفس المظهر في Payroll وHR.Core

**Sidebar ديناميكي:** `_Layout.cshtml` يقرأ claims المستخدم (`ClaimTypes.Role`) ويعرض روابط Controllers **حسب Module policy** فقط — موظف بدور `payroll` يرى `/ui/payroll` ولا يرى `/ui/hr-health`. هذا **UX + أمان**: إخفاء الرابط لا يكفي (RBAC على Controller يمنع الوصول)، لكن يقلل الارتباك.

**لماذا AdminLTE وليس Blazor؟** v1 يستهدف **استقراراً** و**سرعة تطوير** — AdminLTE + Razor Views ناضج، يدعم RTL، ولا يتطلب WebAssembly أو SignalR circuit لكل صفحة. SignalR يُستخدم فقط لـ **NotificationHub** (تنبيهات فورية) في HR.Core.

### 2.5.4 MediatR و CQRS

في تطبيقات MVC التقليدية، يميل **Controller** إلى أن «ينتفخ» — يجمع بين التحقق من المدخلات، قواعد العمل، حفظ قاعدة البيانات، ونشر الأحداث في ملف واحد. هذا يصعّب **الاختبار** و**إعادة الاستخدام**: منطق «موافقة الإجازة» لا يمكن استدعاؤه من Consumer أو من API دون تكرار الكود.

**MediatR** [14] يطبّق نمط **Mediator**: Controller لا ينفّذ المنطق بنفسه، بل يُرسل **رسالة** (Command أو Query) إلى **Handler** مسجّل في DI:

```
Controller → mediator.Send(Command) → Handler → Domain/Repository → (EventPublisher)
```

**لماذا هذا مناسب لـ HRManagement؟**

| الفائدة | مثال في المشروع |
|---------|-----------------|
| Controller رفيع | `LeaveRequestsController` يستدعي `ApproveLeaveRequestCommand` فقط |
| اختبار منفصل | `ApproveLeaveRequestHandler` يُختبر بـ Unit Test دون HTTP |
| فصل قراءة/كتابة | `CalculatePayrollRunCommand` (كتابة) vs استعلامات Index (قراءة) |
| إعادة استخدام | نفس Handler من Controller وIntegration Test |

**أمثلة Commands في المشروع:**

| Command | الخدمة | ماذا يفعل Handler؟ |
|---------|--------|---------------------|
| `ApproveLeaveRequestCommand` | HR.Contracts | يتحقق من الرصيد والسياسة → يحدّث `LeaveRequest` → ينشر `ILeaveApproved` |
| `PublishHireCaseCommand` | HR.Contracts | يتحقق من قائمة التحقق → ينشر `IHireCaseCompleted` |
| `CalculatePayrollRunCommand` | Payroll | يستدعي `PayrollPeriodRules` → يولّد `SalaryLine` لكل موظف |
| `PostPayrollRunCommand` | Payroll | يرحّل الدورة → ينشر `IPayrollProcessed` |

**مثال تدفق — موافقة إجازة:**

1. موظف HR يضغط «موافقة» في `LeaveRequestsController.Approve`.
2. Controller: `await _mediator.Send(new ApproveLeaveRequestCommand(id, approverId))`.
3. **ApproveLeaveRequestHandler** يحمّل `LeaveRequest`، يفحص `LeaveBalance` و`LeavePolicy`.
4. عند النجاح: يحدّث الحالة، يحفظ عبر Repository، ينشر `ILeaveApproved` عبر `IEventPublisher`.
5. Consumers في Payroll وHR.Core وArchive يستقبلون الحدث **بعد** commit المعاملة (Outbox).

**CQRS (Command Query Responsibility Segregation)** هنا **جزئي** [4]: أوامر الكتابة (Commands) منفصلة عن استعلامات القراءة (Queries في Index/Details)، لكن **قاعدة بيانات واحدة** لكل خدمة — لا read replica منفصلة. هذا يوفّر وضوحاً في الكود دون تعقيد بنية CQRS الكاملة، وهو مناسب لـ v1.

**Anti-pattern يتجنبه المشروع:** وضع `if (leave.Balance < days)` داخل Controller — هذا المنطق في Handler أو Domain Service حيث يُختبر بمعزل عن HTTP.

---

## 2.6 Entity Framework Core و PostgreSQL

### 2.6.1 EF Core Code-First

**Entity Framework Core 10** [6] هو ORM (Object-Relational Mapper) يربط بين أصناف C# وجداول PostgreSQL. اختير نمط **Code-First** لأن المطور يُعرِّف الكيانات في **Domain Layer** أولاً، ثم يُولِّد **Migrations** تلقائياً — وهذا يتوافق مع **DDD** حيث قواعد العمل تسبق المخطط الفيزيائي.

كل خدمة لها **Migrations** مستقلة — مثلاً `20260301120000_InitialHRCore` في `HR.Core.Infrastructure/Migrations/`. لا Views ولا Stored Procedures في v1؛ كل المنطق في **Domain Services** بـ C# [13] لسهولة الاختبار والصيانة.

في `OnModelCreating` تُعرَّف سلوكيات **FK** حسب طبيعة العلاقة: **Cascade** للملفات الفرعية (Documents, Qualifications) — حذف الموظف يحذف مؤهلاته؛ **Restrict** للعقود — لا يُحذف موظف له عقد نشط؛ **SetNull** للوحدة التنظيمية عند حذفها — الموظف يبقى دون وحدة مؤقتاً.

**UtcDateTimeNormalization** في `SaveChangesAsync` يضمن أن `CreatedAt/UpdatedAt` تُخزَّن بـ UTC — أمر حاسم عند التكامل بين خدمات في مناطق زمنية مختلفة.

### 2.6.2 PostgreSQL 16

**PostgreSQL 16** [8] قاعدة بيانات علائقية مفتوحة المصدر، مناسبة للاستضافة الذاتية في المؤسسات الحكومية. تعمل على Docker منفذ **5433** (خارجياً) وتوفر **ACID**، دعم **JSON**، وفهارس جزئية (**partial indexes**) لتحسين الاستعلامات.

سلسلة الاتصال في `appsettings.Development.json` لكل خدمة منفصلة: `Host=localhost;Port=5433;Database=hr_core_db;...` — **15 connection string** دون schema مشترك، تطبيقاً لـ **Database-per-Service**.

### 2.6.3 Repository Pattern و Unit of Work

**Repository Pattern** [14] يُخفِي تفاصيل **EF Core** (`DbSet`, `Include`, `AsNoTracking`) عن **Domain** و**Handlers** — فيطبّق **Persistence Ignorance**: كيان `Employee` لا يعرف أنه يُخزَّن في PostgreSQL.

```csharp
// ServiceCollectionExtensions.cs — AddRepositories<TContext>()
services.AddScoped<DbContext>(sp => sp.GetRequiredService<TContext>());
services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
services.AddScoped<IUnitOfWork, EfUnitOfWork<TContext>>();
```

| المكوّن | الواجهة | الدور في HRManagement |
|---------|---------|------------------------|
| `IRepository<T>` | `GetByIdAsync`, `FindAsync`, `AddAsync`, `UpdateAsync` | CRUD على `BaseEntity` (Guid) |
| `IUnitOfWork` | `SaveChangesAsync` | **commit موحّد** — rollback عند أي فشل |
| `EfRepository<T>` | تنفيذ EF | لا SQL خام في v1 |
| `EfUnitOfWork<TContext>` | يلف `SaveChangesAsync` | `UtcDateTimeNormalization` قبل الحفظ |

**مثال — `ApproveLeaveRequestHandler`:**

1. يحمّل `LeaveRequest` عبر `IRepository<LeaveRequest>`.
2. يطبّق قواعد Domain (الرصيد، السياسة).
3. `_unitOfWork.SaveChangesAsync()` **مرة واحدة** — Outbox ينشر `ILeaveApproved` في نفس المعاملة.

**متى DbContext مباشرة؟** بعض **Consumers** (مثل `LeaveApprovedConsumer` في Payroll) تحتاج JOIN أو `IdempotentConsumerFilter` — تستخدم `DbContext` لتجنب طبقة زائدة على مسار القراءة.

**Unit of Work** يمنع «نصف موافقة»: Approved دون خصم `LeaveBalance` — معاملة واحدة أو rollback كامل.

### 2.6.4 Migrations و CI

كل خدمة تُدير **Migrations** في `*.Infrastructure/Migrations/`. مثال الإنشاء:

```bash
dotnet ef migrations add InitialHRCore -p HR.Core.Infrastructure -s HR.Core.Web
```

**سياسة الإنتاج** (`DATABASE_MIGRATION_POLICY.md`):

| البيئة | متى تُطبَّق Migrations؟ |
|--------|-------------------------|
| Development | `TryMigrateAsync<TContext>()` عند startup |
| Production | `bash scripts/run-migrations.sh` **قبل** restart الحاويات |
| Production | `Database__RunMigrationsAtStartup=false` |

**خط CI** (`CI_AND_TESTING.md`):

| المرحلة | الغرض |
|---------|-------|
| `dotnet build -c Release` | تجميع الحل |
| Unit tests (429+) | منطق Domain وHandlers |
| Integration + Testcontainers | Migrations على DB معزولة |
| `EndToEndReleaseWorkflowSmokeIntegrationTests` | Contract → Leave → Payroll → Separation |
| `docker compose build` | Dockerfiles |

Migrations مستقلة = **Database-per-Service**: تغيير Payroll لا يوقف HR.Core.


---

## 2.7 RabbitMQ و MassTransit

يحتاج **HRManagement** إلى **وسيط رسائل** لأن 14 خدمة لا يمكن أن تتصل بقواعد بعضها مباشرة. **RabbitMQ** هو ذلك الوسيط؛ و**MassTransit** طبقة .NET التي تُبسّط النشر والاستهلاك — دون كتابة كود AMQP منخفض المستوى.

### 2.7.1 RabbitMQ

**RabbitMQ 3.13** [10] وسيط رسائل (**Message Broker**) يعمل ببروتوكول **AMQP** — في Docker Compose على **5673** (host) → 5672 (container)، وواجهة إدارية على **15673**. عند `dotnet run` محلياً، التطبيق يتصل بـ `localhost:5673`. دوره في **HRManagement**: نقل **39 حدث تكامل** بين الخدمات دون أن تتصل خدمة **Payroll** مباشرة بقاعدة **HR.Core**.

يدعم **exchanges** لكل نوع رسالة، **Dead Letter Queues** (`*_error` — راجع `RABBITMQ_OPERATIONS.md`) للرسائل الفاشلة، و**persistent messages** لعدم فقدان الأحداث عند إعادة تشغيل الوسيط. بيانات الاعتماد تُقرأ من متغيرات البيئة في Docker Compose.

### 2.7.2 MassTransit

**MassTransit 8.x** [7] طبقة تجريد .NET فوق RabbitMQ — بدلاً من برمجة **AMQP** (channels, exchanges) يدوياً، يُسجَّل كل شيء في DI:

```csharp
builder.Services.AddServiceMassTransit(builder.Configuration, x =>
{
    x.AddConsumer<LeaveApprovedConsumer>();
    x.AddConsumer<SeparationCompletedConsumer>();
    // ...
}, x => x.AddPostgresOutbox<HRCoreDbContext>());
```

| المكوّن | الملف | الدور |
|---------|-------|-------|
| `AddServiceMassTransit` | `MassTransitExtensions.cs` | Host, Retry, CorrelationId |
| `AddPostgresOutbox<TDbContext>` | نفس الملف | **Transactional Outbox** |
| `MassTransitFaultLogger` | `IConsumeObserver` | يسجّل كل فشل consume |
| `CorrelationIdConsumeFilter` | filter | يربط HTTP ↔ RabbitMQ في Serilog |

**Transactional Outbox:** عند `SaveChangesAsync`، الحدث يُكتَب في جدول Outbox **داخل نفس معاملة PostgreSQL** — ثم `BusOutbox` ينشره إلى RabbitMQ. هذا يضمن: **لا حدث بدون commit** و**لا commit بدون حدث** (أو rollback معاً).

**MessageContracts:** 39 واجهة في `HRManagement.MessageContracts` — كل واحدة تحمل `EventSchemaVersion` لدعم **evolution** مستقبلي (إضافة حقول اختيارية دون كسر Consumers القديمة).

**مثال تسجيل Consumers في HR.Core:** `TrainingCompletedConsumer`, `TransferExecutedConsumer`, `LeaveApprovedConsumer`, `MedicalDecisionIssuedConsumer`... — كل Consumer في Infrastructure، منطق العمل في Domain Services.

### 2.7.3 Retry و Idempotency

**Retry (إعادة المحاولة):** في `MassTransitExtensions.cs`:

```csharp
cfg.UseMessageRetry(r => r.Interval(retryCount, TimeSpan.FromSeconds(retryIntervalSeconds)));
// افتراضي: 3 محاولات × 5 ثوانٍ
```

| المرحلة | ماذا يحدث؟ |
|---------|------------|
| 1–3 | إعادة consume تلقائياً (DB مؤقتاً down) |
| بعد الفشل | الرسالة → **Dead Letter Queue** `*_error` |
| تشغيل | `/Home/Operations` أو RabbitMQ UI على **15673** |

**Idempotency (عدم التكرار):** جدول `IntegrationMessageReceipts` في كل DbContext متكامل:

| العمود | الغرض |
|--------|-------|
| `MessageId` | معرف MassTransit للرسالة |
| `ConsumerType` | اسم Consumer (مثل `LeaveApprovedConsumer`) |
| UK `(MessageId, ConsumerType)` | منع التكرار |

**`IdempotentConsumerFilter<T>`:** قبل تنفيذ Consumer، `ShouldSkipAsync` — إن وُجد receipt سابق، يُتخطَّى التنفيذ. بعد النجاح، `TryRecordAsync` يُسجّل Receipt.

**لماذا حاسم في Payroll؟** إعادة deliver لـ `ILeaveApproved` بدون idempotency قد تُنشئ `PayrollAdjustment` مكرراً = **دفع مزدوج**. Retry + Idempotency معاً: transient failures تُعالَج؛ duplicates تُرفَض بأمان.

---

## 2.8 YARP API Gateway

في نظام من 14 خدمة، لا يمكن للمستخدم حفظ 14 عنوان URL. **YARP (Yet Another Reverse Proxy)** [11] في **HRManagement.Gateway** (المنفذ **5000** محلياً، **5200** في Docker full stack) يوفّر **واجهة واحدة**: يستقبل الطلب، يتحقق من JWT/RBAC، يُوجّه للخدمة المناسبة، ويُسجّل التدقيق.

### 2.8.1 التوجيه (Routing)

ملف `appsettings.json` يُعرِّف **Clusters** (مجموعات الخوادم) و**Routes** (قواعد التوجيه):

| Route Pattern | Upstream | Port |
|---------------|----------|------|
| `/ui/hr-core/{**catch-all}` | HR.Core | 5010 |
| `/ui/hr-contracts/{**catch-all}` | HR.Contracts | 5020 |
| `/ui/payroll/{**catch-all}` | Payroll | 5060 |
| `/api/files/{**catch-all}` | File.Storage | 5140 |

**Path transform** يُزيل البادئة `/ui/{module}` قبل إرسال الطلب للخدمة — فـ `/ui/hr-core/Employees` يصل HR.Core كـ `/Employees`.

### 2.8.2 Authorization Middleware

**GatewayPathAuthorizationMiddleware** (`ServiceDefaults`) يُنفَّذ **قبل** YARP reverse proxy — يفحص المسار `/ui/{segment}/...` و`/api/{segment}/...`:

```
Request → Authentication (Cookie/JWT) → GatewayPathAuthorization → YARP → Upstream
```

| المسار | السياسة | مثال |
|--------|---------|------|
| `/ui/hr-core/Employees` GET | `Module.HrCore` أو `HrCoreReadOnly` | قراءة قائمة |
| `/ui/hr-core/Employees` POST | `HrCoreWrite` | إنشاء موظف |
| `/ui/payroll/...` | `Module.Payroll` | دور `payroll` |
| `/ui/ess/...` | `Module.Ess` | موظف يرى بياناته فقط |
| `/careers` | **AllowAnonymous** | بوابة التوظيف العامة |

**قرار Middleware:**

1. `TryGetPolicy(path)` — يستخرج segment (مثل `hr-core`) والسياسة.
2. `AuthorizeAsync(context.User, effectivePolicy)` — ASP.NET RBAC.
3. غير مصادق → `ChallengeAsync()` → Keycloak login.
4. مصادق لكن بلا دور → **403 Forbidden** + رسالة «missing role».

**GatewayAccessTokenRefresh** + **GatewayAccessTokenTransform:**

- Cookie session للمتصفح (OIDC tokens مخزّنة مشفّرة).
- عند التوجيه upstream: `Authorization: Bearer {access_token}` — الخدمات تتحقق بـ `AddKeycloakJwtBearer`.
- `HrAuth:BehindGateway=true`: الخدمات **لا** تُدير OIDC — فقط JWT validation.

**Fine-grained (hr-core):** GET → `HrCoreReadOnly`؛ POST/PUT/DELETE → `HrCoreWrite` — فصل قراءة/كتابة على مستوى Gateway.

### 2.8.3 Audit و Upstream Health

**Gateway Audit** — كل طلب عبر YARP يُسجّل في `gateway_audit_db.gateway_audit_entries`:

| الحقل | الغرض |
|-------|-------|
| `CorrelationId` | ربط مع Serilog في الخدمات |
| `UserSub` | معرف Keycloak (`sub` claim) |
| `Path`, `Method` | `/ui/payroll/PayrollRuns/Approve` POST |
| `Decision` | Allowed / Denied / Challenge |
| `StatusCode` | 200, 403, 302... |
| `Timestamp` | UTC |

**الاستخدام:** مراجعة أمنية («من حاول الوصول لـ Payroll؟»)، تشخيص 403 («هل الدور ناقص في JWT؟»).

**Upstream Health Probes:** `GatewayModuleCatalog` يُعرِّف 14 module — كل واحد له `/health/upstreams/{slug}`. **GatewayHealthProbeService** يستعلم `/health/ready` لكل cluster دورياً؛ `/Home/UpstreamHealth` يعرض لوحة خضراء/حمراء — المشغّل يرى HR.Health down دون فحص 14 منفذاً يدوياً.

**E2E:** `GatewayUpstreamHealthE2ETests` يتحقق من `/health/upstreams/hr-core` → 200 عند stack سليم.


---

## 2.9 Keycloak و OpenID Connect

يُعدّ **الأمان** في **HRManagement** طبقة مستقلة عن منطق HR: الموظف يُعرَّف مرة في **HR.Core**، لكن **دخوله للنظام** يُدار في **Keycloak**. هذا القسم يشرح كيف تتحقق البوابة من هوية المستخدم، كيف تُمرَّر الصلاحيات للخدمات، وكيف تُزامَن حسابات Keycloak تلقائياً مع دورة حياة الموظف.

### 2.9.1 Keycloak IAM

**Keycloak 24** [9] خادم **Identity and Access Management (IAM)** مفتوح المصدر — يُدير **المستخدمين**، **الأدوار (Roles)**، **الجلسات (Sessions)**، و**سياسات كلمة المرور** خارج تطبيق HR. في Docker Compose: **8088→8080** (host→container)؛ داخل الشبكة: `http://keycloak:8080/realms/hrmanagement`.

**[جدول (2-3): إعدادات Keycloak في HRManagement]**

| المكوّن | القيمة | الدور |
|---------|--------|-------|
| **Realm** | `hrmanagement` | نطاق معزول: مستخدمون وأدوار HR فقط |
| **Client (Gateway)** | `hr-gateway-client` | تطبيق OIDC الذي يتصل به المتصفح |
| **Authority** | `http://localhost:8088/realms/hrmanagement` | عنوان التحقق من JWT |
| **Admin Client (Directory Sync)** | service account منفصل | إنشاء/تعطيل مستخدمين عبر Admin REST API |

**لماذا Keycloak وليس جدول Users في HR.Core؟**

1. **فصل الاهتمامات (Separation of Concerns)** [15]: تغيير كلمة مرور، تعطيل حساب، أو إضافة MFA لا يتطلب إعادة نشر 14 خدمة — يكفي تعديل Keycloak.
2. **SSO (Single Sign-On):** المستخدم يسجّل الدخول **مرة واحدة** عبر Gateway ويصل لكل الوحدات (`/ui/hr-core`, `/ui/payroll`...) دون تسجيل منفصل.
3. **RBAC مركزي:** 19 دوراً (`hr-core`, `payroll`, `hr-employee`, `branch-manager`...) تُضمَّن في JWT وتُترجم إلى 20 سياسة ASP.NET (انظر **2.1.4**).
4. **استضافة ذاتية:** مناسب للمؤسسات الحكومية التي لا ترسل بيانات الهوية إلى SaaS خارجي.

**مكونات Realm `hrmanagement`:**
- **Users** — حسابات الدخول (username, email, enabled)
- **Realm Roles** — أدوار HR (`hr-core`, `payroll`...)
- **Client Scopes** — `openid`, `profile`, `email`, `offline_access`
- **User Attributes** — `employeeId`, `scope_org_unit_id` (لربط ESS ومدير الفرع)

### 2.9.2 تدفق OIDC (OpenID Connect)

**OpenID Connect (OIDC)** طبقة هوية فوق **OAuth 2.0** — لا يكتفي بـ «هل لديك token؟» بل يُجيب «**من** هذا المستخدم؟» و«**ما أدواره؟»**. اختير **Authorization Code Flow** لأنه الأكثر أماناً لتطبيقات الويب: **code** قصير العمر يُستبدل بـ tokens على الخادم — وليس في URL أمام المستخدم.

**[شكل (2-4): مخطط تسلسل — تسجيل الدخول OIDC]**

**الغرض:** يوضّح تبادل الرسائل بين **المتصفح**، **Gateway**، **Keycloak**، و**خدمة upstream** (مثل HR.Core).

**شرح الخطوات بالتفصيل:**

| # | الخطوة | ماذا يحدث؟ | لماذا؟ |
|---|--------|-----------|--------|
| 1 | طلب صفحة محمية | المستخدم يفتح `/ui/hr-core/Employees` | Gateway يكتشف عدم وجود جلسة |
| 2 | Challenge → Keycloak | إعادة توجيه إلى `/Account/Login` ثم Keycloak | OIDC Challenge — لا نموذج login محلي |
| 3 | مصادقة Keycloak | المستخدم يُدخل username/password | Keycloak يتحقق من credentials |
| 4 | authorization code | Keycloak يُعيد code إلى Gateway callback | code لا يحمل صلاحيات — آمن في URL |
| 5 | Token exchange | Gateway يستبدل code + client_secret بـ **access_token** + **refresh_token** + **id_token** | على الخادم فقط — لا يُكشف secret للمتصفح |
| 6 | Cookie session | `AddGatewayKeycloakAuthentication` يخزّن tokens في **Cookie** مشفّر | المتصفح لا يحمل JWT في localStorage |
| 7 | KeycloakRoleMapping | `OnTokenValidated` يستخرج `realm_access.roles` من access_token → `ClaimTypes.Role` | ASP.NET RBAC يقرأ الأدوار |
| 8 | YARP + Bearer | `GatewayAccessTokenTransform` يُضيف `Authorization: Bearer {access_token}` للطلب upstream | HR.Core تتحقق من JWT بـ `AddJwtBearer` |
| 9 | Module authorization | `[Authorize(Policy = "Module.HrCore")]` على Controller | رفض إن لم يكن للمستخدم الدور |

**تفاصيل تقنية من `KeycloakExtensions.cs`:**
- **DefaultScheme:** `Gateway` — يختار Cookie للمتصفح أو JwtBearer إن وُجد header `Authorization: Bearer`
- **Scopes:** `openid`, `profile`, `email`, `offline_access` — الأخير يُمكّن **refresh token**
- **SaveTokens:** true — Gateway يحتفظ access_token لإعادة توجيهه
- **HrAuth:BehindGateway=true** (افتراضي): الخدمات **لا** تُدير OIDC بنفسها — فقط `AddKeycloakJwtBearer` — issuer = Authority

**Claims مهمة في JWT بعد `KeycloakRoleMapping`:**
- `sub` — معرف Keycloak (UserSub في gateway audit)
- `ClaimTypes.Role` — أدوار realm (`hr-core`, `payroll`...)
- `employee_id` — من attribute Keycloak — لـ ESS (يرى بياناته فقط)
- `scope_org_unit_id` — لـ `branch-manager` (فلترة وحدته)

**الفرق بين Cookie و Bearer في Gateway:**
- **Cookie session** — للمتصفح البشري (MVC forms)
- **Bearer JWT** — ما تستقبله الخدمات upstream عبر YARP
- **GatewayAccessTokenTransform** يُزيل cookies Gateway من الطلب upstream ويُبقي **AntiForgery** cookies فقط — لحماية POST forms

### 2.9.3 Directory Sync (مزامنة الدليل)

**المشكلة:** عند تعيين موظف في **HR.Core**، يحتاج ESS — كيف يحصل على حساب Keycloak دون إدخال يدوي من IT؟

**الحل:** خدمة **Auth.Directory** تستهلك أحداث دورة الحياة وتستدعي **Keycloak Admin REST API** عبر `KeycloakUserProvisioningService`.

**[شكل (2-5): مخطط تسلسل — Directory Sync عند IEmployeeCreated]**

**المسار:**
1. **HR.Core** ينشر `IEmployeeCreated` (EmployeeId, FullName, NationalId, PrimaryOrgUnitId)
2. **EmployeeCreatedConsumer** في Auth.Directory:
   - ينشئ صف **AuthUser** في `auth_directory_db` (Username من NationalId، Email مؤقت)
   - `PasswordHash = "EXTERNAL_KEYCLOAK"` — **لا كلمة مرور محلية**
   - يستدعي `TryEnsureUserAsync` → POST `/admin/realms/hrmanagement/users`
   - يُخزِّن `KeycloakUserId` المُعاد في AuthUser
   - يُعيِّن أدواراً افتراضية (`hr-employee` من `Keycloak:DirectorySync:DefaultRealmRoles`)
3. attributes في Keycloak: `employeeId`, `scope_org_unit_id` — تظهر لاحقاً في JWT

**أحداث المزامنة الأخرى:**

| الحدث | Consumer | الإجراء في Keycloak |
|-------|----------|---------------------|
| `IEmployeeCreated` | EmployeeCreatedConsumer | إنشاء user + AuthUser + أدوار افتراضية |
| `IEmployeeStatusChanged` | EmployeeStatusChangedConsumer | `TryEnableUserAsync` / `TryDisableUserAsync` |
| `ISeparationCompleted` | SeparationCompletedConsumer | `TryDisableUserAsync` + **إلغاء الجلسات** (`/users/{id}/logout`) |

**تفعيل Directory Sync:** يتطلب `Keycloak:DirectorySync:Enabled = true` + **Admin Client** (client_credentials) + `AdminClientSecret` من متغيرات البيئة — لا credentials في Git [13].

**AuthUser.KeycloakUserId** — رابط فريد UK بين الموظف (`EmployeeId`) وحساب Keycloak. عند الفصل: تعطيل في Keycloak **فوراً** يمنع ESS حتى لو تأخر تحديث HR.Core.

**لماذا AuthUser + Keycloak معاً؟**
- **Keycloak** — مصدر الحقيقة للمصادقة (password, sessions, roles)
- **AuthUser** — مرجع HR محلي (`EmployeeId`, audit, Directory sync state) دون FK عابر لـ HR.Core

---

## 2.10 Docker و Docker Compose

**Docker** [12] يُوحّد بيئة التطوير: «يعمل على جهازي» لا يُقبل في فريق 4 طلاب — كل مطور يحصل على **PostgreSQL 16 + RabbitMQ 3.13 + Keycloak 24 + MinIO** بنفس الإصدارات والمنافذ. في **HRManagement** فُصل **البنية التحتية** (infra) عن **تطبيقات .NET** (apps) عمداً:

| الطبقة | التطوير اليومي | Staging / Production |
|--------|----------------|----------------------|
| Infra | `docker compose up -d` | نفس الحاويات + overlays |
| Apps | `dotnet run` (5010–5140) | Dockerfiles + `docker-compose.services.yml` |
| Gateway | `dotnet run` (5000) | منفذ **5200** في compose الكامل |

**لماذا `dotnet run` في التطوير؟** breakpoints، hot reload، وSerilog مباشر في Terminal — أسرع من rebuild image لكل تغيير Razor.

**نموذجا التشغيل:**

| النمط | متى؟ | الأمر |
|-------|------|-------|
| Infra-only | تطوير يومي | `docker compose up -d` + `dotnet run` لكل خدمة |
| Full stack | staging / demo / E2E | `-f docker-compose.services.yml` |
| Production | تجهيز | + `production.yml` + `observability.yml` |
| Backup | إنتاج | `--profile backup` |

### 2.10.1 docker-compose.yml

ملف `docker-compose.yml` في جذر المستودع — **البنية التحتية + subset من الخدمات** (hr-core, hr-contracts, gateway):

| Service | Image | Ports (host→container) | Volume / Init |
|---------|-------|------------------------|---------------|
| **postgresql** | postgres:16-alpine | **5433→5432** | `postgres_data` + `init-databases.sql` |
| **rabbitmq** | rabbitmq:3.13-management-alpine | **5673→5672**, **15673→15672** | persistent queues |
| **keycloak** | keycloak:24.0 | **8088→8080** | DB = `keycloak_db` |
| **minio** | minio | **9000**, **9001** (console) | `minio_data` — File.Storage |
| hr-core-web | Dockerfile | 5010→8080 | outbox volume |
| hr-contracts-web | Dockerfile | 5020→8080 | — |
| hr-gateway | Dockerfile | **5200→8080** | `gateway_audit_db` |

**`docker/init-databases.sql`** — ينفّذ **مرة واحدة** عند أول تشغيل PostgreSQL:

```
keycloak_db, gateway_audit_db,
hr_core_db, hr_contracts_db, hr_health_db, hr_movements_db,
hr_training_db, payroll_db, idcard_db, archive_db, admin_db,
it_infra_db, analytics_db, compliance_db, auth_directory_db, file_storage_db
```

**16 قاعدة** — 15 للخدمات + `keycloak_db`. لا CREATE DATABASE يدوي.

**سلسلة depends_on:**

```
postgresql (healthy) → keycloak
postgresql + rabbitmq (healthy) → hr-core-web, hr-contracts-web
keycloak + services (started) → hr-gateway
```

**متغيرات البيئة الحساسة** (من `.env`، لا Git): `POSTGRES_PASSWORD`, `RABBITMQ_DEFAULT_PASS`, `KEYCLOAK_ADMIN_PASSWORD`, `KEYCLOAK_HR_GATEWAY_CLIENT_SECRET`.

**شبكة Docker vs localhost:** داخل الحاوية `Host=postgresql;Port=5432` — من المضيف `localhost:5433`. Keycloak داخل الشبكة `:8080`؛ من المتصفح `:8088`.

**`docker-compose.services.yml`:** يُكمّل الـ 11 خدمة المتبقية (5030–5140) — `docker compose -f docker-compose.yml -f docker-compose.services.yml up -d`.

### 2.10.2 Health Checks

**ServiceDefaults** — `AddHrServiceHealthChecks<TDbContext>()` + `MapHrHealthChecks()`:

| Endpoint | Predicate | الغرض | Kubernetes analogy |
|----------|-----------|-------|-------------------|
| `/health/live` | `_ => false` (always pass) | **Liveness** — العملية تعمل | restart if dead |
| `/health/ready` | tags `ready` | **Readiness** — DB + RabbitMQ | route traffic |
| `/health` | all checks | تشخيص كامل | ops dashboard |

**Readiness checks:**

- `AddDbContextCheck<TDbContext>("database")` — `SELECT 1` عبر EF
- `RabbitMqHealthCheck` — ping RabbitMQ (اختياري: `HealthChecks__IncludeRabbitMq`)

**Gateway aggregation:**

- `/Home/UpstreamHealth` — لوحة MVC
- `/health/upstreams/{slug}` — probe لكل module (5010–5140)
- `verify-staging-stack.ps1` — أتمتة التحقق

**RELEASE_READINESS_STAGING.md** — checklist قبل النشر:

| Check | Command |
|-------|---------|
| Build Release | `dotnet build -c Release` |
| Unit + Integration | `dotnet test` |
| Full stack | `docker compose ... up -d --wait` |
| E2E Playwright | `run-staging-e2e.ps1` (SSO, upstream health) |
| RabbitMQ | لا depth غير متوقع في `*_error` |

### 2.10.3 Production Overlay

```bash
docker compose -f docker-compose.yml -f docker-compose.services.yml \
  -f docker-compose.production.yml -f docker-compose.observability.yml up -d
```

**`docker-compose.production.yml`** — overrides رئيسية:

| الإعداد | القيمة | السبب |
|---------|--------|-------|
| `ASPNETCORE_ENVIRONMENT` | Production | لا DeveloperExceptionPage |
| `Database__RunMigrationsAtStartup` | false | migrations عبر script |
| Keycloak `command` | `start` (not `start-dev`) | إنتاج |
| `KC_PROXY: edge` | HTTPS termination | reverse proxy أمام Keycloak |
| Ports postgresql/rabbitmq | `!override []` | لا expose DB للعامة |
| `deploy.resources.limits` | CPU 1.0, RAM 512M/app | منع OOM |
| healthcheck | `curl -f /health/ready` | start_period 60s |

**Observability overlay:** Seq (`Serilog__SeqUrl`), OpenTelemetry collector — traces عبر 14 خدمة.

**PRODUCTION_RUNBOOK.md** — تسلسل التشغيل:

1. Secrets من `.env` أو secret manager
2. Infra healthy (PostgreSQL, RabbitMQ)
3. Keycloak realm `hrmanagement` + clients
4. `bash scripts/run-migrations.sh` **قبل** restart apps
5. Module containers → `/health/ready` = 200
6. Gateway **أخيراً** — OIDC + `/Home/UpstreamHealth`
7. Pilot: `Gateway__EnabledModules__0=hr-core` ثم توسيع تدريجي

**عمليات:** `BACKUP_POSTGRES.md` (pg_dump), `RABBITMQ_OPERATIONS.md` (purge `*_error`), تصدير realm Keycloak JSON, rollback عبر image tag سابق.


---

## 2.11 أنماط التصميم (Design Patterns)

يُطبِّق **HRManagement** أنماطاً كلاسيكية من هندسة البرمجيات [14] — لحل مشاكل محددة في نظام موزّع:

| Pattern | التطبيق في HRManagement | المشكلة التي يحلها |
|---------|-------------------------|---------------------|
| Repository | `EfRepository<T>` | إخفاء تفاصيل EF Core عن Domain |
| Unit of Work | `EfUnitOfWork<TContext>` | commit موحّد لكل طلب |
| Mediator | MediatR handlers | Controllers رفيعة |
| Outbox | MassTransit Postgres Outbox | نشر آمن للأحداث مع المعاملة |
| Gateway | YARP + middleware | نقطة دخول واحدة + RBAC |
| Dependency Injection | ASP.NET Core DI | فك coupling بين الطبقات |
| Shared Kernel | SharedKernel project | توحيد Id وEnums دون مشاركة Domain |
| Integration Events | MessageContracts interfaces | تكامل مرن بين الخدمات |

**مبادئ SOLID:** **Single Responsibility** — كل خدمة = مجال HR واحد؛ **Open/Closed** — إضافة policy دون تعديل Controllers؛ **Liskov** — `IRepository<T>` قابل للاستبدال؛ **Interface Segregation** — واجهات ضيقة؛ **Dependency Inversion** — الاعتماد على abstractions [14].

**[جدول (2-2): التقنيات المستخدمة ومبرراتها]**

| التقنية | المبرر |
|---------|--------|
| .NET 10 | أحدث منصة، MVC ناضج، أداء محسّن |
| PostgreSQL 16 | ACID، مفتوح المصدر، استضافة ذاتية |
| RabbitMQ 3.13 | AMQP ناضج، تكامل MassTransit |
| Keycloak 24 | OIDC، RBAC، استضافة ذاتية |
| YARP | وكيل عكسي أصلي .NET |
| Docker | بيئة dev/staging قابلة للتكرار |
| Serilog | تسجيل منظم + Correlation ID |
| xUnit + Testcontainers | اختبار آلي مع PostgreSQL حقيقي |

---

## 2.12 الدراسات السابقة والأنظمة المشابهة

يهدف هذا القسم إلى تموضع مشروع **HRManagement** ضمن الأدبيات العلمية والأنظمة القائمة، لتحديد نقاط التقاطع والتفرّد في المساهمة.

### 2.12.1 الدراسات الأكاديمية ذات الصلة

| # | الدراسة | أوجه التقاطع مع HRManagement | الفرق الجوهري |
|---|---------|-------------------------------|----------------|
| 1 | Kolyeva, Kortenko & Radkovskaya (2023) [17] | تقترح تقسيم عمليات HR (توظيف، عقود، أرشفة) إلى خدمات مصغّرة مستقلة — مبدأ مشابه لـBounded Context المعتمد هنا | دراسة تصميمية دون تفاصيل تنفيذ كاملة (لا عدد أحداث، لا مخطط قاعدة بيانات)؛ يقدّم HRManagement تفصيلاً تنفيذياً أعمق (39 حدث تكامل، 15 قاعدة بيانات مستقلة). |
| 2 | Gautam, Padole & Bodkhe (2025) [18] | هدف مشترك: نظام HR قابل للتوسع مع RBAC وتدفق بيانات محسّن | يعتمد MERN Stack بخدمات محدودة، وليس معمارية Database-per-Service وEvent-Driven الكاملة المُتّبعة في HRManagement. |
| 3 | Blinowski, Ojdowska & Przybyłek (2022) [16] | مرجع تجريبي مهم لتقييم تكلفة/فائدة اختيار Microservices | أثبتت الدراسة تجريبياً أن **أداء Monolith على جهاز واحد يتفوّق على Microservices**، وأن تطبيقات .NET أقل استهلاكاً للموارد من Java عند التوسع الرأسي. هذه النتيجة تفرض سؤالاً مشروعاً: هل يبرر حجم «المؤسسة المدنية محل الدراسة» فعلاً 14 خدمة منفصلة، أم كان الأنسب البدء بـ Modular Monolith والانتقال التدريجي لاحقاً؟ (نُناقش هذا صراحة في القسم 3.12). |
| 4 | إبراهيم (2018) [19] | دراسة ميدانية باستبانة على 163 موظفاً حكومياً تُثبت إحصائياً أن تطبيق نظم معلومات HR يحسّن الأداء والتخطيط للقوى العاملة في القطاع الحكومي | دليل تجريبي داعم لأهمية المشروع (القسم 1.4) من زاوية الأثر التنظيمي، دون تناول الجانب المعماري/التقني. |

### 2.12.2 مقارنة مع أنظمة HR قائمة (تجارية ومفتوحة المصدر)

| النظام | النوع | نقاط القوة | لماذا لم يُعتمد بدلاً من بناء نظام مخصّص |
|---|---|---|---|
| **Odoo HR** | مفتوح المصدر، معياري | جاهز، مجاني، وحدات متعددة | تخصيص هيكل تنظيمي شعباتي معقّد يتطلب تعديل النواة، ما يُضعف قابلية الترقية مستقبلاً؛ لا يعزل البيانات الصحية بنفس الصرامة المطلوبة. |
| **OrangeHRM** | مفتوح المصدر (نسخة مجتمعية محدودة) | خفيف وسهل النشر | النسخة المجانية بلا Workflow متعدد الخطوات ولا تكامل Event-Driven؛ النسخة المدفوعة تعيد إشكالية الترخيص التجاري. |
| **SAP SuccessFactors / Oracle HCM Cloud** | SaaS تجاري | نضج عالٍ وامتثال قانوني كامل | تكلفة ترخيص مرتفعة، واستضافة سحابية خارجية قد تتعارض مع سياسات سيادة البيانات لمؤسسة مدنية حساسة. |
| **ZenHR وأنظمة إقليمية مشابهة** | SaaS إقليمي | امتثال محلي وواجهة عربية جاهزة | مصمَّم للشركات الخاصة، لا للهيكل الشعباتي الحكومي المعقّد الوارد في `Requirements.md`؛ استضافة خارجية أيضاً. |

**خلاصة التموضع العلمي:** يقع HRManagement بين طرفين — أكثر سيادة على البيانات وتخصيصاً من الحلول الجاهزة، وأكثر عمقاً تنفيذياً من الدراسات الأكاديمية المشابهة التي بقيت غالباً عند حدود التصميم النظري. في المقابل، يتحمّل المشروع كلفة تعقيد تشغيلي حقيقية أثبتتها الأدبيات تجريبياً [16]، وهو ما يُناقَش صراحة في حدود المشروع (1.6) وتحدياته (3.12) بدل تجاهله.

---

# الفصل الثالث: التطبيق العملي

## 3.1 بيئة العمل وأدوات التطوير

يُشغَّل **HRManagement** في بيئة تطوير محلية تجمع بين **.NET 10** (الخدمات) و**Docker** (البنية التحتية). هذا الفصل يصف كيف يُبنى ويُشغَّل النظام فعلياً.

### 3.1.1 متطلبات البيئة

| المكوّن | الإصدار | الغرض |
|---------|---------|-------|
| .NET SDK | 10 | بناء وتشغيل الحل (45+ مشروع) |
| Docker Desktop | — | PostgreSQL 16, RabbitMQ 3.13, Keycloak 24 |
| IDE | VS / VS Code / Rider | تطوير، تصحيح، Migrations |
| Git + GitHub Actions | — | CI/CD واختبار آلي |

**ملاحظة:** الخدمات الـ 14 تُشغَّل بـ `dotnet run` محلياً؛ فقط البنية التحتية (DB, MQ, IAM) في Docker — لتسريع دورة التطوير.

### 3.1.2 هيكل الحل (Solution Structure)

```
HRManagement.slnx
src/Gateway/HRManagement.Gateway
src/Shared/{SharedKernel, MessageContracts, ServiceDefaults, Web.Shared}
src/Services/{HRCore, HRContracts, HRHealth, HRMovements, HRTraining,
              Payroll, IDCard, RecordsArchive, AdminLogistics,
              ITInfrastructure, DevAnalytics, Compliance,
              AuthDirectory, FileStorage}/
  {Service}.Domain/
  {Service}.Infrastructure/
  {Service}.Web/
src/Tests/{UnitTests, IntegrationTests}
```

### 3.1.3 أوامر التشغيل

```bash
docker-compose up -d          # تشغيل PostgreSQL + RabbitMQ + Keycloak
dotnet build HRManagement.slnx
cd src/Gateway/HRManagement.Gateway && dotnet run --urls="http://localhost:5000"
```

بعد التشغيل: Gateway على **5000**، Keycloak على **8080**، PostgreSQL على **5433**. المستخدم يفتح `http://localhost:5000` ويسجّل الدخول عبر Keycloak.

---

## 3.2 تحليل النظام

### 3.2.0 الهيكل التنظيمي وعلاقته بالنظام

**[شكل (1-1): الهيكل التنظيمي لشعبة الموارد البشرية]**

يُمثِّل الشكل (1-1) — **Organizational Structure Tree Diagram** — الهيكل التنظيمي للشعبة كما ورد في `Requirements.md`:

- **الجذر:** شعبة التنظيم والموارد البشرية — «تنظيم إمداد المؤسسة بالقوى البشرية».
- **الفروع الرئيسية (→ Bounded Context):**
  - إدارة الكادر / الشؤون الشخصية → **HR.Core**, **HR.Health**
  - فرع الخدمة والعقود → **HR.Contracts**
  - فرع التنقلات → **HR.Movements**
  - فرع التدريب → **HR.Training**
  - فرع الرواتب → **Payroll**
  - إدارة الهويات → **IDCard**
  - إدارة السجلات → **Records.Archive**
  - الإدارة واللوجستيات → **Admin.Logistics**
  - تقنية المعلومات → **IT.Infra**, **Auth.Directory**
  - التحليلات → **Dev.Analytics**
  - الامتثال → **Compliance**
- **خطوط пунктية:** تدفق المعاملات (تعيين، فصل) عبر أكثر من فرع — ما يُبرّر اختيار **Event-Driven Architecture** بدلاً من نظام monolith واحد.

هذا الإسقاط التنظيمي→تقني هو أساس تقسيم 14 Microservice.

### 3.2.1 الخدمات والمنافذ

**[جدول (3-1): الخدمات والمنافذ وقواعد البيانات]**

| # | الخدمة | Port | Database | DbContext |
|---|--------|------|----------|-----------|
| 1 | HR.Core | 5010 | hr_core_db | HRCoreDbContext |
| 2 | HR.Contracts | 5020 | hr_contracts_db | ContractsDbContext |
| 3 | HR.Health | 5030 | hr_health_db | HealthDbContext |
| 4 | HR.Movements | 5040 | hr_movements_db | MovementsDbContext |
| 5 | HR.Training | 5050 | hr_training_db | TrainingDbContext |
| 6 | Payroll | 5060 | payroll_db | PayrollDbContext |
| 7 | IDCard | 5070 | idcard_db | IDCardDbContext |
| 8 | Records.Archive | 5080 | archive_db | ArchiveDbContext |
| 9 | Admin.Logistics | 5090 | admin_db | AdminLogisticsDbContext |
| 10 | IT.Infra | 5100 | it_infra_db | ITInfraDbContext |
| 11 | Dev.Analytics | 5110 | analytics_db | AnalyticsDbContext |
| 12 | Compliance | 5120 | compliance_db | ComplianceDbContext |
| 13 | Auth.Directory | 5130 | auth_directory_db | AuthDirectoryDbContext |
| 14 | File.Storage | 5140 | file_storage_db | FileStorageDbContext |
| — | Gateway | 5000 | gateway_audit_db | GatewayAuditDbContext |

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

**الغرض:** يُظهر **المخطط المنطقي بين الخدمات** كيف ترتبط البيانات **دون FK فيزيائي** بين قواعد PostgreSQL. **EmployeeId** (Guid) هو المحور: **HR.Core** المصدر المرجعي الوحيد لبيانات الموظف؛ **HR.Contracts**, **Payroll**, **HR.Health**, **IDCard** تحتفظ بـ `EmployeeId` كمرجع منطقي فقط. الأسهم المنقطة تمثّل **أحداث RabbitMQ** التي تُحدِّث هذه المراجع عند التغيير.

**ملاحظة على وصف الجداول:** يلي في الأقسام 3.3.2–3.3.16 وصف **كل جدول** في النظام (~180 جدولاً). لكل جدول: **PK** (المفتاح الأساسي)، **FKs** (العلاقات)، **UK/Index** عند الحاجة، ثم **شرح إداري** يوضّح دور الجدول في عمل الشعبة — وليس مجرد ترجمة اسم الجدول. الأعمدة الموروثة من `BaseEntity` (Id, TenantId, CreatedAt, UpdatedAt) و`AuditableEntity` (CreatedBy, UpdatedBy) تُكرَّر في كل جدول ولا تُذكر إلا عند الحاجة.

### 3.3.2 جداول HRCoreDbContext (32 جدول)

**HR.Core** هي قاعدة **hr_core_db** — أكبر قاعدة في النظام. تحتوي على **السجل المرجعي للموظف** والهيكل التنظيمي والحضور والاستقطاب. الجداول التالية مرتبة حسب الوظيفة الإدارية:

**Employee** — PK: Id. FKs: PrimaryOrgUnitId→OrgUnit. UK: NationalId — **الجدول المركزي** في النظام بأكمله: يُعرِّف هوية الموظف (الاسم، الرقم الوطني، الحالة الوظيفية، نوع الكادر). كل خدمة أخرى تحتفظ بـ `EmployeeId` يشير إلى هذا السجل دون نسخ بياناته.

**OrgUnit** — PK: Id. FKs: ParentId→OrgUnit, HeadEmployeeId→Employee. UK: Code — وحدة تنظيمية في الهيكل الشجري للمؤسسة.

**EmployeeProfile** — PK: Id. FKs: EmployeeId→Employee (Cascade) — تفاصيل شخصية إضافية: الجنسية، محل الولادة، العنوان.

**EmploymentRecord** — PK: Id. FKs: EmployeeId→Employee, OrgUnitId→OrgUnit — سجل وظيفي تاريخي للمنصب والفترة.

**EmployeeDocument** — PK: Id. FKs: EmployeeId→Employee — وثائق مرفقة (هوية، شهادات) مع رابط تخزين.

**Qualification** — PK: Id. FKs: EmployeeId→Employee — مؤهلات علمية ومهنية للموظف.

**ContactInfo** — PK: Id. FKs: EmployeeId→Employee — وسائل اتصال (هاتف، بريد) مع تمييز أساسي.

**EmploymentStatusHistory** — PK: Id. FKs: EmployeeId→Employee, EmploymentRecordId→EmploymentRecord — سجل تغيّرات الحالة (Active, OnLeave, Separated).

**PerformanceEvaluation** — PK: Id. FKs: EmployeeId→Employee — تقييم أداء سنوي بالدرجة والحالة.

**ApprovedCV** — PK: Id. FKs: EmployeeId→Employee — سيرة ذاتية معتمدة مرتبطة بالملف.

**IntegrationMessageReceipt** — PK: Id. UK: (MessageId, ConsumerType) — سجل idempotency لاستهلاك أحداث التكامل.

**WorkforcePlan** — PK: Id. FKs: OrgUnitId→OrgUnit — خطة قوى عاملة سنوية للوحدة التنظيمية.

**HrStatisticReport** — PK: Id. FKs: OrgUnitId→OrgUnit — تقرير إحصائي HR لفترة محددة.

**DailyAttendanceRecord** — PK: Id. FKs: EmployeeId→Employee. UK: (EmployeeId, AttendanceDate) — سجل حضور يومي.

**FieldInspectionVisit** — PK: Id. — زيارة تفتيش ميداني للالتزام بالدوام.

**DiwanCorrespondence** — PK: Id. FKs: RelatedEmployeeId→Employee — مكاتبة ديوان مرتبطة بموظف أو معاملة.

**RecruitmentApplication** — PK: Id. FKs: HiredEmployeeId→Employee — طلب توظيف داخلي يُربط بالموظف بعد التعيين.

**ApprovalRequest** — PK: Id. Index: (EntityType, EntityId), Status — طلب موافقة عام متعدد الخطوات.

**ApprovalStep** — PK: Id. FKs: ApprovalRequestId→ApprovalRequest. UK: (ApprovalRequestId, Level) — خطوة موافقة بمستوى وتوقيع.

**Shift** — PK: Id. UK: Code — تعريف وردية (صباحي، مسائي).

**ShiftRoster** — PK: Id. FKs: OrgUnitId→OrgUnit. Index: (OrgUnitId, PeriodStart) — جدول مناوبات لفترة.

**ShiftAssignment** — PK: Id. FKs: ShiftRosterId, ShiftId, EmployeeId. Index: (EmployeeId, WorkDate) — إسناد موظف لوردية بتاريخ.

**CareerPath** — PK: Id. — مسار مهني تنظيمي.

**CareerPathStep** — PK: Id. FKs: CareerPathId→CareerPath — مرحلة في المسار الوظيفي.

**KeyPosition** — PK: Id. FKs: OrgUnitId→OrgUnit — منصب حرج يتطلب تعاقباً.

**SuccessionPlan** — PK: Id. FKs: KeyPositionId→KeyPosition — خطة تعاقب للمنصب.

**SuccessorCandidate** — PK: Id. FKs: SuccessionPlanId, EmployeeId — مرشح خليفة محتمل.

**InternalVacancy** — PK: Id. FKs: OrgUnitId→OrgUnit — شاغر داخلي للتنقل.

**InternalApplication** — PK: Id. FKs: InternalVacancyId, EmployeeId. UK: (InternalVacancyId, EmployeeId) — طلب موظف للشاغر.

**PublicJobPosting** — PK: Id. — إعلان وظيفة للبوابة العامة `/careers`.

**CitizenApplication** — PK: Id. FKs: PublicJobPostingId. UK: ReferenceNumber — طلب مواطن للوظيفة العامة.

**EntityAuditLog** — PK: Id. — سجل تدقيق تغييرات الكيانات في HR.Core.

### 3.3.3 جداول ContractsDbContext (20 جدول)

**HR.Contracts** (`hr_contracts_db`) — قاعدة **الخدمة الوظيفية والعقود**: كل معاملة (تعيين، إجازة، فصل، سلفة) تمر عبر `ServiceTransaction`. جداول Outbox هنا ضرورية لأن Contracts **تنشر** أحداثاً حاسمة (`ILeaveApproved`, `ISeparationCompleted`).

**ContractType** — PK: Id. UK: Code — نوع عقد (دائم، مؤقت) بالأسماء العربية والإنجليزية.

**Contract** — PK: Id. FKs: ContractTypeId→ContractType. Ref: EmployeeId — عقد موظف بحالة (Draft, Active, Terminated).

**ContractSignature** — PK: Id. FKs: ContractId→Contract — توقيعات العقد بأنواع مختلفة.

**ProbationRecord** — PK: Id. FKs: ContractId→Contract. Ref: EmployeeId — فترة اختبار ونتيجتها.

**SeparationRecord** — PK: Id. FKs: ContractId→Contract. Ref: EmployeeId — سجل فصل (استقالة، تقاعد، فصل).

**LeaveRequest** — PK: Id. Ref: EmployeeId — طلب إجازة بنوع ومدة وحالة موافقة.

**AdvanceRequest** — PK: Id. Ref: EmployeeId — طلب سلفة مالية.

**AdministrativeDecision** — PK: Id. — قرار إداري مرتبط بالخدمة.

**CommencementRecord** — PK: Id. FKs: ContractId→Contract — سجل مباشرة العمل بعد التعيين.

**ServiceTransaction** — PK: Id. — معاملة خدمة موحّدة (تعيين، إجازة، فصل) بمرجع وحالة.

**ServiceTransactionStateHistory** — PK: Id. FKs: TransactionId→ServiceTransaction — تاريخ انتقالات حالة المعاملة.

**LeaveBalance** — PK: Id. Ref: EmployeeId. UK: (EmployeeId, LeaveType, Year) — رصيد إجازات سنوي.

**LeavePolicy** — PK: Id. UK: LeaveType — سياسة إجازات (أيام مستحقة).

**AdvanceInstallmentPlan** — PK: Id. FKs: AdvanceRequestId→AdvanceRequest — خطة أقساط السلفة.

**SeparationChecklist** — PK: Id. FKs: SeparationRecordId. UK: (SeparationRecordId, Step) — خطوة قائمة تحقق الفصل.

**HireChecklist** — PK: Id. FKs: ServiceTransactionId. UK: (ServiceTransactionId, Step) — خطوة قائمة تحقق التعيين (P1.1–P7.1).

**OrgUnitSnapshot** — PK: Id. — لقطة مُكرَّرة (denormalized) للهيكل التنظيمي وقت المعاملة — للاحتفاظ بالوضع التاريخي.

**EntityAuditLog** — PK: Id. — تدقيق تغييرات HR.Contracts.

**OutboxMessage** — PK: SequenceNumber. — رسالة Outbox MassTransit pending للنشر.

**OutboxState** — PK: OutboxId. — حالة Outbox MassTransit.

### 3.3.4 جداول HealthDbContext (13 جدولاً)

**HR.Health** (`hr_health_db`) — **عزل البيانات الطبية** في قاعدة مستقلة عن HR.Core. تشمل الملفات الطبية، اللجان، التأمين، الحوادث. التوصيات الطبية تُرسل إلى Compliance عبر `IMedicalRecommendationIssued`.

**MedicalRecord** — PK: Id. Ref: EmployeeId — ملف طبي أساسي للموظف.

**MedicalCommitteeReport** — PK: Id. FKs: MedicalRecordId→MedicalRecord — تقرير لجنة طبية.

**PeriodicMedicalCheck** — PK: Id. Ref: EmployeeId — فحص طبي دوري.

**InsuranceCard** — PK: Id. FKs: InsuranceContractId→InsuranceContract. Ref: EmployeeId — بطاقة تأمين صحي.

**HealthStatusEntry** — PK: Id. Ref: EmployeeId — إدخال حالة صحية.

**MedicalRecommendation** — PK: Id. FKs: ReportId→MedicalCommitteeReport — توصية طبية (تُنشر `IMedicalRecommendationIssued`).

**HealthRetirementRecord** — PK: Id. Ref: EmployeeId — سجل تقاعد صحي.

**InsuranceContract** — PK: Id. — عقد تأمين جماعي.

**WorkplaceAccident** — PK: Id. Ref: EmployeeId. Index: AccidentDate — حادث عمل.

**SafetyInspection** — PK: Id. Index: InspectionDate — تفتيش سلامة مكان العمل.

**FirstAidTrainingRecord** — PK: Id. Ref: EmployeeId — تدريب إسعافات أولية.

**IntegrationMessageReceipt** — PK: Id. UK: (MessageId, ConsumerType) — idempotency لـ `IEmployeeCreated`.

**EntityAuditLog** — PK: Id. — تدقيق HR.Health.

### 3.3.5 جداول MovementsDbContext (10 جداول)

**HR.Movements** (`hr_movements_db`) — **حركة القوى البشرية**: النقل بين الوحدات، الانتداب، الإعارة. عند تنفيذ النقل (`Execute`) يُنشر `ITransferExecuted` فيُحدَّث الهيكل في HR.Core والأثر المالي في Payroll.

**TransferRequest** — PK: Id. Ref: EmployeeId, FromOrgUnitId, ToOrgUnitId — طلب نقل بين وحدات.

**MovementNotice** — PK: Id. — إشعار حركة تنظيمية.

**DistributionPlan** — PK: Id. FKs: TransferRequestId→TransferRequest — خطة توزيع مرتبطة بنقل.

**MovementExecutionRecord** — PK: Id. FKs: TransferRequestId/SecondmentRequestId/LoanAssignmentRequestId — سجل تنفيذ حركة.

**SecondmentRequest** — PK: Id. Ref: EmployeeId — طلب انتداب مؤقت.

**LoanAssignmentRequest** — PK: Id. Ref: EmployeeId — طلب إعارة لجهة أخرى.

**TransferApproval** — PK: Id. FKs: TransferRequestId→TransferRequest — موافقة على النقل.

**TransferFinancialImpact** — PK: Id. FKs: TransferRequestId→TransferRequest — أثر مالي للنقل.

**OrgUnitSnapshot** — PK: Id. — لقطة هيكل تنظيمي.

**EntityAuditLog** — PK: Id. — تدقيق HR.Movements.

### 3.3.6 جداول TrainingDbContext (14 جدول)

**HR.Training** (`hr_training_db`) — **التأهيل والتعلم**: دورات حضورية، LMS إلكتروني، خطط سنوية، بعثات. إتمام دورة 100% ينشر `ITrainingCompleted`.

**TrainingCourse** — PK: Id. FKs: ProviderId→TrainingProvider. UK: Code — دورة تدريبية.

**TrainingEnrollment** — PK: Id. FKs: CourseId→TrainingCourse. Ref: EmployeeId — تسجيل موظف بدورة.

**QualificationCode** — PK: Id. UK: Code — رمز مؤهل معتمد مرتبط بمتطلبات الترقية.

**TrainingResult** — PK: Id. FKs: EnrollmentId→TrainingEnrollment (1:1) — نتيجة الدورة.

**MissionFile** — PK: Id. Ref: EmployeeId — ملف مهمة/انتداب تدريبي.

**TrainingNomination** — PK: Id. FKs: CourseId. Ref: EmployeeId — ترشيح للتدريب.

**TrainingProvider** — PK: Id. — جهة تقديم التدريب.

**TrainingPlan** — PK: Id. Index: (Year, OrgUnitId) — خطة تدريب سنوية.

**ELearningCourse** — PK: Id. UK: Code — دورة تعلم إلكتروني.

**CourseModule** — PK: Id. FKs: ELearningCourseId→ELearningCourse — وحدة في الدورة.

**Lesson** — PK: Id. FKs: CourseModuleId→CourseModule — درس داخل الوحدة.

**LearningEnrollment** — PK: Id. FKs: ELearningCourseId. Ref: EmployeeId. UK: (ELearningCourseId, EmployeeId) — تسجيل e-learning.

**LearningCertificate** — PK: Id. FKs: LearningEnrollmentId. UK: CertificateNo — شهادة إتمام.

**EntityAuditLog** — PK: Id. — تدقيق HR.Training.

### 3.3.7 جداول PayrollDbContext (26 جدولاً)

**Payroll** (`payroll_db`) — **الأكثر حساسية مالياً**: دورات رواتب، ملفات أجور، تعديلات واردة من أحداث (إجازة، فصل، تأديب). v1 **تشغيلي** — ليس محركاً قانونياً كاملاً للضرائب.

**PayrollRun** — PK: Id. — دورة رواتب (Draft→Post) لفترة.

**SalaryLine** — PK: Id. FKs: PayrollRunId→PayrollRun. Ref: EmployeeId — سطر راتب موظف.

**CompensationType** — PK: Id. UK: Code — نوع تعويض (أساسي، بدل).

**AllowanceRecord** — PK: Id. FKs: AllowanceTypeId→CompensationType. Ref: EmployeeId — بدل مستحق.

**DeductionRecord** — PK: Id. FKs: AppliedInRunId→PayrollRun. Ref: EmployeeId — خصم مطبّق.

**PayrollAdjustment** — PK: Id. FKs: PayrollRunId. Ref: EmployeeId — تعديل inbound من حدث.

**PayrollAuditRecord** — PK: Id. FKs: PayrollRunId→PayrollRun — سجل تدقيق دورة.

**PaymentBatch** — PK: Id. FKs: PayrollRunId→PayrollRun — دفعة دفع بنكية.

**SalaryScale** — PK: Id. UK: Code — سلم رواتب.

**PayGrade** — PK: Id. FKs: SalaryScaleId→SalaryScale. UK: (SalaryScaleId, GradeCode) — درجة في السلم.

**DeductionType** — PK: Id. UK: Code — نوع خصم.

**EmployeePayProfile** — PK: Id. FKs: PayGradeId→PayGrade. Ref: EmployeeId. Index: (EmployeeId, IsActive) — ملف أجور نشط.

**SalaryLineComponent** — PK: Id. FKs: SalaryLineId→SalaryLine — مكوّن (بدل/خصم) في السطر.

**EndOfServiceSettlement** — PK: Id. Ref: EmployeeId. Index: EmployeeId — تسوية نهاية خدمة.

**PayrollConfiguration** — PK: Id. UK: Code — إعدادات نظام رواتب (v1 operational).

**TaxBracket** — PK: Id. FKs: PayrollConfigurationId→PayrollConfiguration — شريحة ضريبة.

**OfficialHoliday** — PK: Id. FKs: PayrollConfigurationId — عطلة رسمية.

**EmployeePayProfileHistory** — PK: Id. Ref: EmployeeId. Index: EmployeeId — تاريخ تغيّرات ملف الأجور.

**PayrollRunApproval** — PK: Id. FKs: PayrollRunId→PayrollRun — موافقة على دورة.

**PayslipDelivery** — PK: Id. FKs: SalaryLineId→SalaryLine — تسليم كشف راتب.

**AbsenceDeduction** — PK: Id. Ref: EmployeeId — خصم غياب.

**OrgUnitSnapshot** — PK: Id. — لقطة هيكل للرواتب.

**IntegrationMessageReceipt** — PK: Id. UK: (MessageId, ConsumerType) — idempotency.

**OutboxMessage** — PK: SequenceNumber. — Outbox MassTransit.

**OutboxState** — PK: OutboxId. — حالة Outbox.

**EntityAuditLog** — PK: Id. — تدقيق Payroll.

### 3.3.8 جداول IDCardDbContext (8 جداول)

**IDCard** (`id_card_db`) — **إصدار بطاقات الهوية والوصول**. خطوة P8.1 في قائمة التعيين تُعلَّم تلقائياً عند `ICardIssued`.

**CardDesign** — PK: Id. — قالب تصميم بطاقة الموظف.

**IssuedCard** — PK: Id. FKs: CardDesignId→CardDesign, DeliveryRecordId→CardDeliveryRecord. Ref: EmployeeId — بطاقة صادرة.

**CardRequest** — PK: Id. Ref: EmployeeId — طلب إصدار/تجديد بطاقة.

**CardDeliveryRecord** — PK: Id. — سجل تسليم البطاقة.

**OfficialSealRecord** — PK: Id. — سجل ختم رسمي.

**AccessPermission** — PK: Id. FKs: IssuedCardId→IssuedCard — صلاحية دخول مرتبطة بالبطاقة.

**CardPermissionSet** — PK: Id. — مجموعة صلاحيات بطاقة.

**EntityAuditLog** — PK: Id. — تدقيق IDCard.

### 3.3.9 جداول ArchiveDbContext (8 جداول)

**Records.Archive** (`records_archive_db`) — **أرشفة المعاملات والوثائق** بعد إتمامها. يستقبل أحداث `IHireCaseCompleted`, `ILeaveApproved`, `ISeparationCompleted` ويُنشئ `ArchiveItem` لكل معاملة.

**DocumentClassification** — PK: Id. UK: Code — تصنيف وثائق أرشيفية.

**ArchiveItem** — PK: Id. FKs: ClassificationId→DocumentClassification. UK: ReferenceNumber — عنصر أرشيف رئيسي.

**PhysicalFileRecord** — PK: Id. FKs: ArchiveItemId→ArchiveItem — ملف فيزيائي (رف، صندوق).

**DigitalFileRecord** — PK: Id. FKs: ArchiveItemId→ArchiveItem — ملف رقمي (مرجع File.Storage).

**LoanRecord** — PK: Id. FKs: ArchiveItemId→ArchiveItem — استعارة وثيقة.

**ArchiveIndexEntry** — PK: Id. FKs: ArchiveItemId→ArchiveItem — فهرس بحث.

**DisposalRecord** — PK: Id. FKs: ArchiveItemId→ArchiveItem — إتلاف/نقل نهائي.

**EntityAuditLog** — PK: Id. — تدقيق Records.Archive.

### 3.3.10 جداول AdminLogisticsDbContext (9 جداول)

**Admin.Logistics** (`admin_logistics_db`) — **إدارة الأصول واللوجستيات**: أجهزة، مركبات، استرداد عند الفصل. يستهلك `ISeparationCompleted` لاسترداد أصول الموظف.

**Asset** — PK: Id. UK: TagNumber — أصل/logistics (جهاز، مركبة).

**AssetAssignment** — PK: Id. FKs: AssetId→Asset. Ref: EmployeeId — إسناد أصل لموظف.

**SupplyOrder** — PK: Id. — طلب توريد.

**InventoryItem** — PK: Id. UK: Sku — صنف مخزون.

**MaintenanceRecord** — PK: Id. FKs: AssetId→Asset — صيانة أصل.

**Vendor** — PK: Id. — مورّد.

**ReceiptRecord** — PK: Id. FKs: SupplyOrderId→SupplyOrder — استلام توريد.

**IntegrationMessageReceipt** — PK: Id. UK: (MessageId, ConsumerType) — idempotency.

**EntityAuditLog** — PK: Id. — تدقيق Admin.Logistics.

### 3.3.11 جداول ITInfraDbContext (9 جداول)

**IT.Infra** (`it_infra_db`) — **تتبع البنية التحتية التقنية**: خوادم، أجهزة، markers تجهيز موظفين جدد. يستهلك `IEmployeeCreated` لتسجيل marker تجهيز.

**ServerDevice** — PK: Id. UK: Hostname — خادم بنية تحتية.

**UserDevice** — PK: Id. Ref: EmployeeId — جهاز مستخدم.

**BackupJob** — PK: Id. — مهمة نسخ احتياطي.

**SecurityIncident** — PK: Id. — حادث أمني.

**SystemPermissionRecord** — PK: Id. Ref: EmployeeId — صلاحية نظام IT.

**ITSupportTicket** — PK: Id. Ref: EmployeeId — تذكرة دعم.

**EnvironmentConfiguration** — PK: Id. — إعداد بيئة.

**IntegrationMessageReceipt** — PK: Id. UK: (MessageId, ConsumerType) — idempotency.

**EntityAuditLog** — PK: Id. — تدقيق IT.Infra.

### 3.3.12 جداول AnalyticsDbContext (8 جداول)

**Dev.Analytics** (`dev_analytics_db`) — **تجميع أحداث HR للتحليل**: يستهلك معظم أحداث التكامل (`IEmployeeCreated`, `IPayrollProcessed`...) ويُخزّن snapshots للتقارير.

**Report** — PK: Id. — تقرير تحليلي.

**Dashboard** — PK: Id. — لوحة KPI.

**ETLJob** — PK: Id. — مهمة ETL.

**ReadModel** — PK: Id. — نموذج قراءة مُكرَّر للتقارير السريعة.

**KPI** — PK: Id. UK: Code — مؤشر أداء.

**AnalyticsEvent** — PK: Id. — حدث تحليلي مستهلك من التكامل.

**DataRefreshSchedule** — PK: Id. FKs: TargetReadModelId→ReadModel — جدولة تحديث.

**EntityAuditLog** — PK: Id. — تدقيق Dev.Analytics.

### 3.3.13 جداول ComplianceDbContext (8 جداول)

**Compliance** (`compliance_db`) — **الشكاوى والتأديب** في قاعدة معزولة. قرارات تأديبية تنشر `IDisciplinaryActionTaken` → Payroll (خصم).

**Complaint** — PK: Id. Ref: EmployeeId — شكوى موظف/مواطن.

**ComplaintFollowUp** — PK: Id. FKs: ComplaintId→Complaint — متابعة شكوى.

**AuditFinding** — PK: Id. — نتيجة تدقيق.

**ComplianceRecommendation** — PK: Id. FKs: FindingId, ComplaintId — توصية امتثال.

**LegalCase** — PK: Id. — قضية قانونية.

**LegalReferral** — PK: Id. FKs: ComplaintId→Complaint — إحالة قانونية.

**DisciplinaryAction** — PK: Id. FKs: ComplaintId. Ref: EmployeeId — إجراء تأديبي (ينشر `IDisciplinaryActionTaken`).

**EntityAuditLog** — PK: Id. — تدقيق Compliance.

### 3.3.14 جداول AuthDirectoryDbContext (9 جداول)

**Auth.Directory** (`auth_directory_db`) — **ربط الموظفين بحسابات Keycloak**. لا كلمات مرور محلية — Keycloak يحتفظ بها. يزامن عند `IEmployeeCreated`, `ISeparationCompleted`.

**AuthUser** — PK: Id. UK: Username, Email. Ref: EmployeeId, KeycloakUserId — مستخدم مرتبط بـ Keycloak.

**Role** — PK: Id. — دور نظام.

**UserRole** — PK: Id. FKs: AuthUserId, RoleId. UK: (AuthUserId, RoleId) — ربط مستخدم–دور.

**AuthToken** — PK: Id. FKs: UserId→AuthUser — رمز/session.

**DirectoryEntry** — PK: Id. Ref: EmployeeId — إدخال دليل.

**DirectorySyncRecord** — PK: Id. — سجل مزامنة Keycloak.

**PermissionPolicy** — PK: Id. — سياسة صلاحية.

**AuthAuditLog** — PK: Id. — تدقيق مصادقة.

**EntityAuditLog** — PK: Id. — تدقيق Auth.Directory.

### 3.3.15 جداول FileStorageDbContext (4 جداول)

**File.Storage** (`file_storage_db`) — **تخزين مركزي للمرفقات** (binary + metadata). الخدمات الأخرى تحتفظ بـ `FileId` كمرجع دون تخزين الملف في قواعدها.

**Files** — PK: Id. UK: (Bucket, ObjectKey). Index: Status, UploadedAt — ملف مخزّن binary/metadata.

**FileReferences** — PK: Id. FKs: FileId→Files. UK: (FileId, HolderKey) — مرجع من خدمة holder.

**FileAccessAuditLog** — PK: Id. FKs: FileId. Index: OccurredAt — تدقيق وصول ملف.

**EntityAuditLog** — PK: Id. — تدقيق File.Storage.

### 3.3.16 جداول GatewayAuditDbContext (1 جدول)

**Gateway** (`gateway_audit_db`) — **تدقيق كل طلب** يمر عبر YARP: من طلب؟ أي مسار؟ مُسمح أم مرفوض؟ CorrelationId للربط مع سجلات الخدمات.

**gateway_audit_entries** — PK: Id. Index: OccurredAtUtc, CorrelationId, StatusCode — سجل طلبات Gateway (Method, Path, Decision, UserSub).

---

## 3.4 تحليل المستخدمين والصلاحيات

يُطبّق **HRManagement** نموذج **RBAC** (Role-Based Access Control) على خمس طبقات متتابعة: المصادقة في Keycloak، التفويض على Gateway، سياسات الوحدة في كل خدمة، صلاحيات دقيقة على العمليات، ونطاق تنظيمي للمديرين. هذا التدرج يضمن أن موظف ESS لا يرى رواتب زملائه، وأن مدير فرع يرى موظفي وحدته فقط.

**[شكل (3-14): مخطط RBAC — الأدوار والسياسات]**

**الغرض من المخطط:** يُبيّن كيف تنتقل الصلاحية من **Keycloak** (19 دوراً) إلى **ASP.NET Policies** (20 policy)، ثم إلى فحص Gateway ثم الخدمة.

**طبقات التفويض:**
1. **Keycloak Roles:** `hr-admin`, `hr-core`, `payroll`, `hr-employee`... — تُضمَّن في JWT
2. **Module Policies:** `Module.HrCore`, `Module.Payroll`... — Gateway يفحصها قبل التوجيه
3. **Fine-grained:** `Payroll.Process`, `HrContracts.HireExecute` — الخدمة تفحصها على POST
4. **Org Scope:** claim `scope_org_unit_id` — مدير الفرع يرى وحدته فقط
5. **ESS Self-service:** claim `employee_id` — الموظف يرى بياناته فقط

**[جدول (3-5): أدوار Keycloak وصلاحياتها]**

| الدور | Module Policy | Fine-grained |
|-------|---------------|--------------|
| hr-admin | All | Delete everywhere |
| hr-core | Module.HrCore | Write, Approve |
| hr-contracts | Module.HrContracts | Approve, HireExecute |
| hr-management-approver | — | HrManagement.Approve |
| payroll | Module.Payroll | Process, Approve, Post |
| hr-employee | Module.Ess | ESS only |
| branch-manager | Module.HrCore | قراءة فقط ضمن نطاق الوحدة التنظيمية |

**[جدول (3-6): سياسات Authorization]**

20 policy في `HrManagementAuthorizationExtensions` تربط أدوار Keycloak بعمليات محددة:

| مجموعة السياسات | العدد | أمثلة |
|-----------------|-------|-------|
| `Module.*` | 14 | Module.HrCore, Module.Payroll — وصول للوحدة |
| `HrCore.*` | 4 | Write, Approve, Delete — عمليات Core |
| `HrContracts.*` | 2 | Approve, HireExecute — تعيين وفصل |
| `Payroll.*` | 3 | Process, Approve, Post — دورة رواتب |
| أخرى | 2 | HrManagement.Approve, Gateway.Dashboard |

**طبقات التطبيق (Enforcement):**
1. **Gateway:** `[Authorize]` — يجب تسجيل الدخول لأي `/ui/*`
2. **GatewayPathAuthorizationMiddleware** — Module policy + fine-grained على المسارات
3. **HrModuleAuthorizationFilter** — فحص Module policy لكل MVC action
4. **`[Authorize(Policy)]`** — Write/Approve/Process/Post على POST/PUT/DELETE
5. **HrCadreVisibility / HrOrgScopeVisibility** — فلترة بيانات HR.Core حسب الكادر والوحدة

---

## 3.5 تحليل العمليات وسير العمل

يُعدّ تحليل سير العمل في **HRManagement** محوراً لفهم كيف تتكامل الخدمات المستقلة لإنجاز معاملة إدارية واحدة — كالتعيين أو الفصل — دون أن يضطر المستخدم إلى التنقل يدوياً بين أربعة عشر نظاماً. في الواقع، يبدأ الموظف المختص إجراءً في واجهة واحدة (مثل معالج التعيين)، بينما تتولى الأحداث المنشورة عبر **RabbitMQ** تحديث الخدمات الأخرى: الرواتب، الهوية، الأرشيف، دليل المستخدمين. يستعرض هذا القسم أهم المسارات التشغيلية المنفّذة في الإصدار v1.

### 3.5.1 مسار التعيين (Hire Journey)

**[شكل (3-8): مخطط النشاط — مسار التعيين]**

**الغرض من المخطط:** يوضّح **Activity Diagram** تسلسل الأنشطة من قبول مرشح حتى إغلاق ملف التعيين وأرشفته، مع نقاط القرار (موافقة إدارية، اكتمال قائمة التحقق) والأنشطة المتوازية التي تُفعَّل بالأحداث.

**شرح المسار بالتفصيل:**

1. **استقبال المرشح:** يبدأ المسار في **HR.Core** عند تسجيل `RecruitmentApplication` (حالة: مُقدَّم → قيد المراجعة → مُختار). عند قرار التوظيف، يُنشأ سجل **Employee** ويُنشر حدث `ICandidateAccepted` يليه `IEmployeeCreated`.

2. **تهيئة السياقات الأخرى:** حدث `IEmployeeCreated` لا يقتصر على **HR.Core**؛ بل يُطلق سلسلة استجابات: **HR.Contracts** تُجهّز معاملة تعيين وقائمة تحقق، **Auth.Directory** يُنشئ حساباً، **Payroll** يُهيّئ ملف أجور، **IDCard** يُفعّل طلب بطاقة، **IT.Infra** يُسجّل marker للتجهيز. هذا يُمثّل **Fork** في مخطط النشاط — مسارات متوازية.

3. **معالج التعيين (HireWizard):** في **HR.Contracts**، يمرّ مسؤول الخدمة بأربع خطوات: اختيار المعاملة، مراجعة قائمة التحقق السباعية (P1.1–P7.1)، موافقة الإدارة العليا (`HrManagement.Approve`)، ثم الإكمال (`PublishHireCase`).

4. **تفعيل العقد والربط المالي:** بعد **CommencementRecord** وتفعيل العقد (`ActivateContract`)، يُنشر `IContractActivated` فيُفعَّل **EmployeePayProfile** في **Payroll** — نقطة ربط حاسمة بين الخدمة والمالية.

5. **إصدار الهوية والإغلاق:** عند إصدار البطاقة (`ICardIssued`) تُحدَّث خطوة P8.1. وعند `IHireCaseCompleted` تُؤرشف المعاملة في **Records.Archive** وتُسجَّل مراسلة ديوان عبر **HR.Core**.

| رمز الخطوة | المعنى الإداري | الخدمة المسؤولة |
|------------|----------------|-----------------|
| P1.1 | استلام المرشح ومعاملة التعيين | HR.Contracts |
| P1.2 | تصنيف وتفعيل العقد | HR.Contracts |
| P2.1 | إنشاء/اكتمال الملف الشخصي | HR.Core |
| P6.1 | ربط الراتب | Payroll |
| P5.1 | اعتماد السيرة الوظيفية | HR.Core |
| P8.1 | إصدار بطاقة الهوية | IDCard |
| P7.1 | أرشفة الملف | Records.Archive |

### 3.5.2 مسار الفصل (Separation Journey)

**[شكل (3-9): مخطط النشاط — مسار الفصل]**

**الغرض:** يبيّن كيف تُدار معاملة إنهاء الخدمة عبر **SeparationWizard** بأربع خطوات، وكيف يُطلق حدث `ISeparationCompleted` موجة تحديثات في سبع خدمات.

**شرح المسار:** في **الخطوة الأولى** يُنشأ `SeparationRecord` و`ServiceTransaction` من نوع فصل مع قائمة تحقق افتراضية. **الخطوة الثانية** تُكمِل بنود القائمة (استرداد أصول، إغلاق حسابات...). **الخطوة الثالثة** تنشر الفصل (`PublishSeparation`). **الخطوة الرابعة** تعرض تأكيداً. بعد النشر، تتولى **Payroll** تسوية نهاية الخدمة، **IDCard** تعطيل البطاقة، **Admin.Logistics** استرداد الأصول، **Auth.Directory** تعطيل الحساب، **Records.Archive** أرشفة الوثائق، **IT.Infra** و**Dev.Analytics** التسجيل التحليلي.

### 3.5.3 دورة الرواتب (Payroll Run Lifecycle)

**[شكل (3-10): مخطط النشاط — دورة الرواتب]**

**الغرض:** يوضّح مراحل دورة رواتب شهرية — من المسودة حتى الترحيل — مع نقاط الموافقة والتدقيق.

**شرح المسار:** يُنشأ `PayrollRun` بحالة **Draft**. عند **Calculate** تُولَّد `SalaryLine` لكل موظف وفق `EmployeePayProfile` و`PayrollPeriodRules` (أيام العمل، العطل، التناسب). بعد **Review** و**Approve**، يُرحَّل (**Post**) الدورة ويُنشر `IPayrollProcessed`. التعديلات الواردة من أحداث (إجازة، تنقل، تأديب) تُسجَّل كـ `PayrollAdjustment` قبل أو أثناء الدورة. يُذكر أن v1 يوفّر دورة **تشغيلية** وليس محركاً قانونياً كاملاً للضرائب والتأمينات.

### 3.5.4 مخططات التسلسل ومخططات التدفق

**[شكل (3-11): مخطط التسلسل — حدث IEmployeeCreated]**

**الغرض:** يُظهر **Sequence Diagram** ترتيب الزمن بين **HR.Core** (الناشر) والخدمات المستهلكة، مع رسائل AMQP متوازية.

**العناصر:** `HR.Core` (lifeline)، `RabbitMQ` (broker)، `HR.Contracts`, `Payroll`, `Auth.Directory`... (lifelines). السهم 1: حفظ Employee + Outbox. السهم 2: نشر الحدث. الأسهم 3–n: استهلاك متوازٍ مع فحص Idempotency.

**[شكل (3-12): مخطط التسلسل — تسجيل الدخول]**

**الغرض:** يوضّح تدفق **OIDC** من المتصفح عبر **Gateway** إلى **Keycloak** ثم عودة الجلسة وتمرير **JWT** للخدمات.

**الخطوات بالعربية:** (1) المستخدم يطلب صفحة محمية. (2) Gateway يُوجّه إلى Keycloak. (3) Keycloak يُصادق ويُعيد code. (4) Gateway يستبدل code بـ tokens. (5) Cookie session + Bearer للخدمة upstream. (6) الخدمة تتحقق من JwtBearer وتطبّق Module policy.

**[شكل (3-13): مخطط تدفق — موافقة الإجازة]**

**الغرض:** **Flowchart** لمسار طلب إجازة من الإنشاء حتى التأثير على ثلاث خدمات.

**المسار:** إنشاء `LeaveRequest` (يدوياً أو عبر ESS) → فحص `LeavePolicy` و`LeaveBalance` → قرار الموافقة → `ApproveLeaveRequestCommand` → نشر `ILeaveApproved` → **Payroll** (تعديل)، **HR.Core** (حالة OnLeave + حضور)، **Records.Archive** (أرشفة المعاملة).

---

## 3.6 شرح الخدمات (Modules)

**[شكل (3-15): مخطط الأصناف — BaseEntity و Shared Kernel]**

**الغرض من المخطط:** يُظهر **Class Diagram** للطبقة المشتركة التي ترث منها جميع كيانات النظام، ما يضمن توحيد المفاتيح الأساسية وسجلات التدقيق.

**شرح العناصر:** `BaseEntity` يحتوي `Id` (Guid)، `TenantId`، `CreatedAt`، `UpdatedAt`. `AuditableEntity` يضيف `CreatedBy` و`UpdatedBy` لتتبع المستخدم. `EntityAuditLog` يسجّل التغييرات. `IntegrationMessageReceipt` يدعم Idempotency. كيانات كل خدمة (Employee, Contract, PayrollRun...) ترث من هذه الأصناف دون مشاركة جداول بين الخدمات.

فيما يلي شرح تفصيلي لكل خدمة: دورها في الشعبة، مسؤولياتها، وكيف تتكامل مع غيرها.

### 3.6.1 HR.Core — الملفات الشخصية والسير (المنفذ 5010)

**HR.Core** هي **النواة المرجعية** للنظام: المصدر الرسمي الوحيد لسؤال «من هو الموظف؟» و«أين يعمل؟». تحتفظ بالهيكل التنظيمي (`OrgUnit`)، السجل الوظيفي، المؤهلات، التقييمات، الحضور، الاستقطاب الداخلي، وبوابة التوظيف العامة. أي خدمة أخرى تحتاج معرفة موظف لا تنسخ بياناته بل تحتفظ بـ `EmployeeId` كمرجع.

**دورها في دورة الحياة:** عند التعيين تُنشأ هنا بيانات الموظف وتُنشر `IEmployeeCreated`. عند الموافقة على إجازة تُحدَّث الحالة إلى OnLeave. عند التنقل تُحدَّث `PrimaryOrgUnitId` بعد `ITransferExecuted`.

| البند | الشرح |
|-------|--------|
| **الهدف** | إدارة البيانات المرجعية للموظفين والهيكل التنظيمي |
| **المسؤوليات الرئيسية** | CRUD موظفين، شجرة وحدات، حضور، استقطاب، موافقات، ديوان، ESS، التوظيف العام، المناوبات، التعاقب |
| **المدخلات** | نماذج MVC، استيراد CSV، أحداث (إجازة، تنقل، تدريب، فصل) |
| **المخرجات** | واجهات عربية، Lookup API، أحداث تكامل |
| **التبعيات** | `hr_core_db`، RabbitMQ، File.Storage للمرفقات |
| **أحداث ينشرها** | `IEmployeeCreated`, `IEmployeeStatusChanged`, `ICandidateAccepted`, `ITransactionRegisteredInDiwan` |
| **أحداث تستهلكها** | `ILeaveApproved`, `ITransferExecuted`, `ITrainingCompleted`, `IHireCaseCompleted`, `ISeparationCompleted` |
| **قاعدة البيانات** | `hr_core_db` — 32 جدولاً |

**المتحكمات (Controllers) الرئيسية:** `EmployeesController`, `OrgUnitsController`, `RecruitmentApplicationsController`, `HireWizard` (عبر Contracts)، `EssPortalController`, `PublicCareersController`, `ApprovalsController`, `DailyAttendanceRecordsController`.

### 3.6.2 HR.Contracts — العقود والخدمة (المنفذ 5020)

**HR.Contracts** تمثّل **الجانب الإداري–القانوني** للعلاقة بين المؤسسة والموظف: العقود، المباشرة، الإجازات، السلف، القرارات الإدارية، والفصل. تُدير **ServiceTransaction** كمعاملة موحّدة تربط كل طلب (تعيين، إجازة، فصل) بسجل واحد قابل للتتبع.

**دورها في دورة الحياة:** معالج التعيين (`HireWizard`) ومعالج الفصل (`SeparationWizard`) يعملان هنا. موافقة الإجازة تنشر `ILeaveApproved` الذي يُحدّث Payroll وHR.Core.

| البند | الشرح |
|-------|--------|
| **الهدف** | أتمتة العقود ومعاملات الخدمة الوظيفية |
| **المسؤوليات** | عقود، إجازات، سلف، فصل، قوائم تحقق، قرارات إدارية |
| **المدخلات** | نماذج HR، أحداث (EmployeeCreated, CandidateAccepted, Medical/Legal) |
| **المخرجات** | معالجات متعددة الخطوات، أحداث تُستهلك في الخدمات اللاحقة (Payroll, Archive...) |
| **التبعيات** | `hr_contracts_db`، Outbox، مراجع HR.Core |
| **أحداث ينشرها** | `IContractActivated`, `ILeaveApproved`, `IAdvanceApproved`, `ISeparationCompleted`, `IHireCaseCompleted` |
| **أحداث تستهلكها** | `IEmployeeCreated`, `ICandidateAccepted`, `ICardIssued`, `ILegalJudgmentIssued`, `IMedicalDecisionIssued` |
| **قاعدة البيانات** | `hr_contracts_db` — 20 جدولاً |

### 3.6.3 HR.Health — الصحة والسلامة (المنفذ 5030)

**HR.Health** تعزل **البيانات الصحية الحساسة** في قاعدة مستقلة، وفق متطلبات الخصوصية. تشمل الملفات الطبية، اللجان، التأمين، الحوادث، والسلامة المهنية. التوصيات الطبية تُرسل إلى **Compliance** عبر `IMedicalRecommendationIssued`.

| البند | الشرح |
|-------|--------|
| **الهدف** | إدارة الشؤون الصحية والسلامة المهنية |
| **المسؤوليات** | سجلات طبية، فحوص دورية، تأمين، حوادث عمل، تدريب إسعاف |
| **المدخلات** | `IEmployeeCreated`، إدخال يدوي عبر MVC |
| **المخرجات** | `IMedicalRecommendationIssued`, `IMedicalDecisionIssued` |
| **قاعدة البيانات** | `hr_health_db` — 14 جدولاً |

### 3.6.4 HR.Movements — التنقلات (المنفذ 5040)

**HR.Movements** تُنظّم **حركة القوى البشرية** داخل المؤسسة: النقل بين الوحدات، الانتداب، الإعارة، وخطط التوزيع. عند **Execute** للنقل يُنشر `ITransferExecuted` فيُحدَّث الهيكل في HR.Core والدليل في Auth.Directory والأثر المالي في Payroll.

| البند | الشرح |
|-------|--------|
| **الهدف** | إدارة التنقلات والانتدابات والإعارات |
| **المسؤوليات** | طلبات نقل، موافقات، تنفيذ، أثر مالي، نشرة تنقلات |
| **أحداث ينشرها** | `ITransferExecuted`, `ISecondmentExecuted`, `ILoanAssignmentExecuted` |
| **قاعدة البيانات** | `hr_movements_db` — 10 جداول |

### 3.6.5 HR.Training — التدريب (المنفذ 5050)

**HR.Training** تغطي **التأهيل والتعلم**: دورات حضورية، تعلم إلكتروني (LMS)، خطط سنوية، رموز تأهيل، وملفات بعثات. عند إكمال دورة 100% يُنشر `ITrainingCompleted` فيُحدَّث السيرة في HR.Core وقد يُؤثّر على Payroll.

| البند | الشرح |
|-------|--------|
| **الهدف** | إدارة التدريب والتعلم الإلكتروني والبعثات |
| **المسؤوليات** | دورات، التحاق، ترشيحات، شهادات، بعثات |
| **أحداث ينشرها** | `ITrainingCompleted`, `IMissionApproved` |
| **قاعدة البيانات** | `hr_training_db` — 14 جدولاً |

### 3.6.6 Payroll — الرواتب (المنفذ 5060)

**Payroll** هي **الخدمة المالية** للنظام: سلالم الأجور، ملفات الأجور، دورات الرواتب، التعديلات، ونهاية الخدمة. تستهلك أحداثاً من Contracts وMovements وCompliance دون الوصول المباشر لقاعدة HR.Core.

| البند | الشرح |
|-------|--------|
| **الهدف** | معالجة الرواتب والتعويضات (دورة تشغيلية v1) |
| **المسؤوليات** | دورات رواتب، ملفات أجور، تعديلات، تسوية نهاية خدمة |
| **أحداث ينشرها** | `IPayrollProcessed` |
| **أحداث تستهلكها** | `IContractActivated`, `ILeaveApproved`, `ITransferExecuted`, `ISeparationCompleted`, وغيرها (9+ أنواع) |
| **قاعدة البيانات** | `payroll_db` — 25 جدولاً |

### 3.6.7 IDCard — الهويات (المنفذ 5070)

**IDCard** تدير **دورة حياة بطاقة الهوية**: تصميم، طلب، إصدار، طباعة، تسليم، صلاحيات دخول. حدث `ICardIssued` يُكمِل خطوة P8.1 في قائمة تحقق التعيين.

| البند | الشرح |
|-------|--------|
| **الهدف** | إصدار وإدارة بطاقات الهوية الوظيفية |
| **قاعدة البيانات** | `idcard_db` — 8 جداول |

### 3.6.8 Records.Archive — الأرشيف (المنفذ 5080)

**Records.Archive** تحفظ **الذاكرة المؤسسية** للمعاملات: تعيين، فصل، إجازات، شكاوى. تستقبل أحداثاً وتُنشئ `ArchiveItem` بـ `ReferenceNumber` فريد.

| البند | الشرح |
|-------|--------|
| **الهدف** | أرشفة الوثائق الورقية والرقمية |
| **المسؤوليات** | تصنيف، إعارة، إتلاف، فهرسة |
| **قاعدة البيانات** | `archive_db` — 8 جداول |

### 3.6.9 Admin.Logistics — اللوجستيات (المنفذ 5090)

**Admin.Logistics** تُتابع **الأصول والمخزون**: أجهزة، أثاث، طلبات تموين. عند الفصل أو النقل تُسترد الأصول المُسندة للموظف.

| البند | الشرح |
|-------|--------|
| **الهدف** | إدارة الأصول والمخزون والتموين |
| **قاعدة البيانات** | `admin_db` — 9 جداول |

### 3.6.10 IT.Infra — تقنية المعلومات (المنفذ 5100)

**IT.Infra** تربط **البنية التحتية التقنية** بالموارد البشرية: أجهزة المستخدمين، النسخ الاحتياطي، التذاكر، الحوادث الأمنية. تستقبل `IEmployeeCreated` لتهيئة تجهيزات و`ISeparationCompleted` للإلغاء.

| البند | الشرح |
|-------|--------|
| **الهدف** | دعم IT للموظفين والبنية التحتية |
| **قاعدة البيانات** | `it_infra_db` — 9 جداول |

### 3.6.11 Dev.Analytics — التحليلات (المنفذ 5110)

**Dev.Analytics** تجمع **أحداث التكامل** في نماذج قراءة (ReadModels) ولوحات KPI. لا تُعدّ مصدراً للحقيقة بل طبقة تحليل فوق الخدمات التشغيلية.

| البند | الشرح |
|-------|--------|
| **الهدف** | تحليلات وKPIs وETL |
| **قاعدة البيانات** | `analytics_db` — 8 جداول |

### 3.6.12 Compliance — الامتثال (المنفذ 5120)

**Compliance** تدير **الشكاوى والتأديب والقضايا القانونية**. دورة الشكوى (فتح → تحقيق → قرار → إغلاق) تُنشر `IComplaintResolved`. الإجراءات التأديبية تُرسل خصومات إلى Payroll.

| البند | الشرح |
|-------|--------|
| **الهدف** | الرقابة والامتثال ومعالجة الشكاوى |
| **قاعدة البيانات** | `compliance_db` — 8 جداول |

### 3.6.13 Auth.Directory — المصادقة والدليل (المنفذ 5130)

**Auth.Directory** هي **جسر الهوية** بين HR.Core وKeycloak: تُنشئ `AuthUser` و`DirectoryEntry` وتُزامِن الأدوار عند التنقل أو الفصل.

| البند | الشرح |
|-------|--------|
| **الهدف** | دليل المستخدمين ومزامنة Keycloak |
| **قاعدة البيانات** | `auth_directory_db` — 9 جداول |

### 3.6.14 File.Storage — الملفات (المنفذ 5140)

**File.Storage** منصة **مركزية للمرفقات**: وثائق موظفين، أختام، ملفات أرشيف. الخدمات الأخرى تحتفظ بـ `FileId` لا بالملف نفسه.

| البند | الشرح |
|-------|--------|
| **الهدف** | تخزين ملفات موحّد مع تدقيق وصول |
| **قاعدة البيانات** | `file_storage_db` — 4 جداول |

### 3.6.15 Gateway — البوابة (المنفذ 5000)

**Gateway** ليست خدمة أعمال بل **نقطة الدخول الوحيدة**: مصادقة، تفويض، توجيه YARP، لوحة رئيسية، وتدقيق الطلبات.

| البند | الشرح |
|-------|--------|
| **الهدف** | بوابة موحّدة وSSO |
| **قاعدة البيانات** | `gateway_audit_db` — جدول تدقيق واحد |

---

## 3.8 شرح واجهات المستخدم

تُبنى واجهات **HRManagement** على **ASP.NET Core MVC** مع **AdminLTE 3** ودعم **RTL** الكامل. الهدف توفير تجربة عربية موحّدة لموظفي الشعبة، مع فصل واضح بين لوحة البوابة المركزية وواجهات كل وحدة.

### 3.8.1 لوحة البوابة الرئيسية

**[شكل (3-16): واجهة لوحة البوابة الرئيسية]**

**الوظيفة:** بعد تسجيل الدخول عبر Keycloak، يرى المستخدم **لوحة تحكم** (`/` على المنفذ 5000) تعرض روابط الوحدات الأربع عشرة بحسب صلاحياته — فلا يرى موظف ESS وحدات Payroll مثلاً.

**مكوّنات الشاشة:**
- **شريط علوي:** شعار المؤسسة، اسم المستخدم، زر خروج.
- **شبكة الوحدات:** بطاقة لكل خدمة (HR.Core، Contracts، Payroll...) تفتح `/ui/{module}/`.
- **لوحة الصحة:** حالة كل خدمة (تعمل / متوقفة) من فحوص `/health`.
- **مؤشرات سريعة:** عدد المستخدمين النشطين، الموافقات المعلّقة.
- **رابط العمليات:** تتبع الأحداث بـ Correlation ID.

**اتجاه العرض:** من اليمين لليسار، خط **ITF Qomra Arabic**.

### 3.8.2 معالج التعيين (HireWizard)

**[شكل (3-17): واجهة معالج التعيين]**

**الوظيفة:** يُرشد مسؤول الخدمة خلال **معاملة تعيين** متكاملة في **HR.Contracts** (`/ui/hr-contracts/HireWizard`).

**الخطوات:**
1. **اختيار المعاملة:** ربط `ServiceTransaction` من نوع تعيين.
2. **قائمة التحقق:** سبع بنود (P1.1–P7.1) — بعضها يُعلَّم تلقائياً عند استلام الأحداث.
3. **موافقة الإدارة:** للمستخدمين ذوي دور `hr-management-approver`.
4. **الإكمال:** نشر `IHireCaseCompleted` وعرض تأكيد.

**عناصر واجهة:** شريط تقدم أفقي، رسائل نجاح/خطأ (`_HrAlerts`)، مسار تنقل (breadcrumb) عربي.

### 3.8.3 بوابة الخدمة الذاتية (ESS)

**الوظيفة:** تمكين **الموظف** (`hr-employee`) من خدمات محدودة دون الوصول لوحدات HR الكاملة.

**الشاشات:** تقديم إجازة، طلب سلفة، عرض حضور، عرض مستندات، التعلم الإلكتروني (`EssLearning`). كل شاشة تعرض **بيانات الموظف الحالي فقط** وفق claim `employee_id`.

### 3.8.4 بوابة التوظيف العامة

**الوظيفة:** تمكين **المواطن** من تصفّح الوظائف (`/careers`) وتقديم طلب دون تسجيل دخول، مع **رقم مرجعي** للمتابعة.

### 3.8.5 أنماط مشتركة

جميع الوحدات تتبع نمط **CRUD**: Index (قائمة) → Create → Edit → Details. يُشارك **Footer** و**تنبيهات** عبر `HRManagement.Web.Shared`. **SeparationWizard** يُطبّق نفس فكرة معالج التعيين (خطوات متتابعة + قائمة تحقق) لمسار الفصل. مدير الفرع (`branch-manager`) يرى قوائم مفلترة بحسب `scope_org_unit_id`.

### 3.8.6 الهوية البصرية

| العنصر | القيمة | الدلالة |
|--------|--------|---------|
| اللون الأساسي | #6b1f2a | بورغندي — الهوية السورية |
| اللون الثانوي | #428177 | أخضر مائي |
| الخط | ITF Qomra Arabic | وضوح عربي |
| الاتجاه | RTL | مطابقة للغة العربية |

## 3.9 آلية الاتصال بين الأنظمة

يتبع **HRManagement** نموذج **Hybrid Integration** (تكامل هجين): يجمع بين الاتصال **المتزامن** (HTTP) للاستعلامات الفورية، والاتصال **غير المتزامن** (RabbitMQ) لسير العمل الذي يشمل عدة خدمات [7][13].

**لماذا هجين؟** عندما يحتاج موظف HR.Core اسم موظف من Payroll فوراً، HTTP Lookup أسرع من انتظار حدث. لكن عند **تعيين موظف جديد**، سبع خدمات يجب أن تُحدَّث — نشر حدث `IEmployeeCreated` يُمكّنها من العمل **بالتوازي** دون أن تنتظر HR.Core كل واحدة.

### 3.9.1 التكامل المتزامن (HTTP)

| الآلية | المسار | الغرض |
|--------|--------|-------|
| YARP Proxy | `/ui/{module}/**`, `/api/{module}/**` | توجيه MVC وREST مع JWT |
| Lookup API | `/api/hr-core/api/lookup/*` | استعلام موظفين/وحدات (anonymous في v1) |
| File API | `/api/files` | رفع/تحميل مرفقات عبر `FileId` |
| Directory Sync | `/api/auth-directory/api/internal/directory/sync-employee` | مزامنة دليل بعد نقل |

### 3.9.2 التكامل غير المتزامن (Events)

**[جدول (3-7): مصفوفة الأحداث — Domain Events]**

| الحدث | الناشر | المستهلكون الرئيسيون | Idempotency |
|-------|--------|----------------------|-------------|
| IEmployeeCreated | HR.Core | Contracts, Health, IDCard, Auth, Payroll, IT, Analytics | IntegrationMessageReceipt / markers |
| IContractActivated | HR.Contracts | Payroll | AdjustmentReason marker |
| ISeparationCompleted | HR.Contracts | Payroll, Archive, IDCard, Admin, Auth, IT, Analytics | Archive ref / inbox |
| ITransferExecuted | HR.Movements | Payroll, Auth, HR.Core, Admin | marker / inbox |
| ILeaveApproved / IAdvanceApproved | HR.Contracts | Payroll, HR.Core, Archive | Outbox + markers |
| IHireCaseCompleted | HR.Contracts | Archive, HR.Core (Diwan) | ref `HIRE-{txn}` |
| IPayrollProcessed | Payroll | Dev.Analytics | payload referenceKey |
| IComplaintResolved | Compliance | Analytics, Archive | ref `CMP-RES-*` |
| ICardIssued | IDCard | Contracts (checklist), Analytics | checklist step |

**[جدول (3-7-ب): أحداث Workflow Integration]**

| الحدث | الناشر | المستهلكون |
|-------|--------|------------|
| ICandidateAccepted | HR.Core | HR.Contracts (عقد + Appointment) |
| ITrainingCompleted | HR.Training | HR.Core, Payroll |
| IMissionApproved | HR.Training | Payroll |
| ISecondmentExecuted | HR.Movements | Payroll, Auth.Directory |
| IDisciplinaryActionTaken | Compliance | Payroll (خصم) |
| ITransactionRegisteredInDiwan | HR.Core | HR.Contracts (أرشفة معاملة) |

**Outbox:** HR.Core، HR.Contracts، Payroll — يضمن نشر الحدث مع commit DB [7].  
**Retry:** 3×5 ثوانٍ افتراضياً؛ DLQ: `*_error` queues [13].  
**Correlation:** `X-Correlation-ID` → `/Home/Operations` في Gateway.

### 3.9.3 أحداث Dead-end (v1)

`ICommencementRecorded`, `IConfirmationApproved`, `IJobUpdateApplied`, `ITransactionArchived` — تُنشر من HR.Contracts دون مستهلك v1؛ موثّقة في REMAINING_IMPLEMENTATION [13].

## 3.10 نظام الأمان

يُعالج **HRManagement** بيانات حساسة (رواتب، سجلات طبية، شكاوى). يُطبَّق **Defense in Depth** — طبقات أمان من Keycloak حتى فلترة الصفوف.

### 3.10.1 المصادقة (Authentication)

**«من أنت؟»** — يُجيب **Keycloak** لا الخدمات:

- **Keycloak 24** realm `hrmanagement` — **OpenID Connect Authorization Code Flow** + Cookie session في Gateway [9].
- **JwtBearer** في الخدمات عند `HrAuth:BehindGateway=true` — Bearer token يُمرَّر عبر YARP.
- **AuthUser** يربط `EmployeeId` ↔ `KeycloakUserId` — **لا كلمات مرور** في `auth_directory_db`.
- **DirectorySync:** إنشاء/تعطيل حساب Keycloak عند `IEmployeeCreated` / `ISeparationCompleted`.

### 3.10.2 التفويض (Authorization)

**«ماذا يُسمح لك؟»** — خمس طبقات (تفصيل في **3.4**):

| الطبقة | المكوّن | الوظيفة |
|--------|---------|---------|
| 1 | Gateway `[Authorize]` | يجب تسجيل الدخول |
| 2 | GatewayPathAuthorizationMiddleware | Module + fine-grained على `/ui/*`, `/api/*` |
| 3 | HrModuleAuthorizationFilter | Module policy لكل MVC action |
| 4 | `[Authorize(Policy)]` | Write/Approve/Process/Post |
| 5 | HrCadreVisibility / HrOrgScopeVisibility | فلترة بيانات HR.Core |

19 دور Keycloak → 20 سياسة ASP.NET — **Least Privilege**: ESS يرى بياناته فقط [15].

### 3.10.3 التدقيق والامتثال

**«من غيّر ماذا ومتى؟»** — سجلات لا تُحذف:

- **EntityAuditLog:** في كل DbContext — CreatedBy/UpdatedBy على AuditableEntity.
- **GatewayAuditEntry:** Method, Path, StatusCode, CorrelationId, ElapsedMs.
- **AuthAuditLog:** عمليات Directory وSecondment markers.
- **SensitiveDataAudit:** Compliance (شكوى)، HR.Health (توصية طبية).
- **FileAccessAuditLog:** كل download/presigned URL.

### 3.10.4 أسرار الشبكة

- `Keycloak__ClientSecret` من متغيرات البيئة — KEYCLOAK_AND_SECRETS.md [13].
- **HTTPS** في Production عبر `AddHrProductionNetworking`.
- **Idempotency** يمنع تكرار خصومات Payroll أو إنشاء مستخدمين مزدوجين عند redelivery.

## 3.11 النتائج

بناءً على التحليل والتنفيذ الفعلي في مستودع HRManagement، تُلخَّص النتائج كالتالي:

**[جدول (3-10): نتائج الاختبارات]**

| النوع | العدد | أمثلة |
|-------|-------|-------|
| Unit tests | ~30 ملفاً | ContractWorkflowRulesTests, PayrollPeriodRulesTests, HireWorkflowRulesTests |
| Integration | 15+ | HireCaseIntegrationTests, MassTransitConsumerTests |
| CI | GitHub Actions | CI_AND_TESTING.md |

1. **تحقق الهدف المعماري:** نجح المشروع في تقسيم متطلبات شعبة HR إلى **14 Bounded Context** مستقلة، كل منها بـ DbContext وMigrations خاصة، مع **39 حدث تكامل** عبر MassTransit — دون FK عابرة للقواعد [13].

2. **أتمتة مسارات العمل:** مسار **التعيين** (سبع بنود قائمة تحقق) و**الفصل** (أربع خطوات في المعالج) و**الإجازات** (معاملة `ServiceTransaction` موحّدة) يعمل **من البداية للنهاية** مع مستهلكي الأحداث الموثّقين في MESSAGE_EVENT_MATRIX [13].

3. **دورة رواتب تشغيلية:** دورة PayrollRun (Draft→Post) مع **PayrollPeriodRules** للتناسب والعطل، وتعديلات واردة من أكثر من 9 أنواع أحداث — **تشغيلية** وليست محركاً قانونياً كاملاً (حد v1 صريح) [13].

4. **أمان متعدد الطبقات:** Keycloak (19 دور) + Gateway middleware + module filters + cadre/org scoping — يعكس حساسية بيانات HR [9][15].

5. **قابلية التشغيل:** Docker Compose + health checks + Correlation ID + ~45 اختباراً آلياً — جاهزية staging [13].

6. **واجهة عربية:** AdminLTE RTL + خط ITF Qomra Arabic + هوية بصرية (#6b1f2a, #428177) — أكثر من 100 شاشة MVC [13].

7. **فجوات v1 موثّقة:** محرك رواتب قانوني، تكامل حضور كامل، dead-end consumers — مسار v2 في REMAINING_IMPLEMENTATION [13].

**8. فجوة قياس فعلي في الأداء والتغطية:** الأرقام الواردة في جدول (3-10) عددية (عدد ملفات الاختبار) وليست نوعية. يوصى قبل المناقشة بـ: (أ) قياس نسبة تغطية الكود (Code Coverage) عبر `coverlet`، (ب) تنفيذ اختبار حمل فعلي على الأقل لخدمتي HR.Core وPayroll عبر `k6`، وتوثيق النتيجة الفعلية بدل الاكتفاء بالتصميم النظري القابل للتوسع.

**9. غياب اختبار قبول المستخدم (UAT):** لم يخضع النظام لتقييم فعلي من موظفي شعبة التنظيم والموارد البشرية المستهدفة. يوصى بجلسة عرض (Demo) قصيرة مع مستخدمين فعليين أو محاكاة سيناريوهات حقيقية معهم، وتوثيق ملاحظاتهم كملحق — إذ إن ذلك يرفع مصداقية الادعاء بأن النظام «تشغيلي» ويمنح اللجنة دليلاً على التحقق من صحة المتطلبات (Requirements Validation) لا الاكتفاء بتحققها التقني فقط (Verification).

## 3.12 تحديات تطبيق المشروع

1. **تعقيد التشغيل:** مراقبة 15 قاعدة بيانات و14 خدمة + RabbitMQ + Keycloak تتطلب أدوات DevOps (Kubernetes, observability) غير مدمجة بالكامل في v1 — Docker Compose مناسب للتطوير لا للإنتاج الكبير.

2. **الاتساق النهائي (Eventual Consistency):** بعد نشر حدث، قد يمرّ بضع ثوانٍ قبل ظهور التحديث في Payroll أو Auth.Directory — يتطلب تدريب المستخدمين وواجهات «حالة المزامنة».

3. **فجوة الرواتب القانونية:** v1 يوفّر دورة **تشغيلية** — حساب تناسب وأيام عمل — دون محرك ضرائب وتأمينات كامل وفق PAYROLL_RULES.md.

4. **أحداث بلا مستهلكين (Dead-end):** `IJobUpdateApplied`, `ICommencementRecorded` تُنشر دون consumer v1 — موثّقة في REMAINING_IMPLEMENTATION.

5. **متطلبات البيئة المحلية:** الاختبار التكاملي الكامل يتطلب Docker (PostgreSQL, RabbitMQ, Keycloak) — عائق للمطورين بدون Docker Desktop.

## 3.13 المقترحات والتوصيات

1. **Kubernetes + Helm** — نشر إنتاج مع auto-scaling لـ Payroll وHR.Core.
2. **محرك رواتب v2** — ضرائب، تأمينات، امتثال قانوني كامل.
3. **Redis** — تخزين مؤقت لـ Lookup APIs وتقليل حمل PostgreSQL.
4. **استكمال Dead-end consumers** — `ICommencementRecorded` وغيرها.
5. **OpenTelemetry** — تتبع موزّع عبر Gateway → Services → RabbitMQ.
6. **تطبيق ESS للهاتف** — عبر أحداث `ILeaveRequested`, `IAdvanceRequested` الموجودة في العقود.

### خاتمة الفصل الثالث

هدف هذا المشروع إلى بناء **نظام إدارة موارد بشرية متكامل** يُترجم الهيكل التنظيمي لشعبة التنظيم والموارد البشرية إلى **منظومة Microservices** حديثة. وقد أُنجز **إصدار v1 تشغيلي** يغطي دورة حياة الموظف من الاستقطاب إلى الأرشفة، مع **39 حدث تكامل** و**14 خدمة** و**~180 جدولاً** موزّعة. أثبتت **Database-per-Service** مع **RabbitMQ** جدوى التكامل في مجال HR المعقد، مع قبول **Eventual Consistency** مقابل **Loose Coupling**. تبقى فجوات v1 — محرك رواتب قانوني، تكامل حضور كامل — **موثّقة** في REMAINING_IMPLEMENTATION لمسار v2.

---

# خاتمة عامة

انطلق هذا المشروع من مشكلة إدارية واقعية: تشتّت بيانات الموارد البشرية بين فروع متعددة في مؤسسة مدنية كبيرة، وبطء تنسيق الإجراءات الإدارية بسبب غياب منظومة معلومات موحّدة. وقد استجاب فريق العمل لهذه المشكلة ببناء **HRManagement**، نظام معلومات مؤسسي مبني على معمارية الخدمات المصغّرة (14 خدمة مستقلة) والتكامل القائم على الأحداث (39 حدثاً عبر RabbitMQ/MassTransit)، مع مصادقة مركزية موحّدة عبر Keycloak وواجهة عربية كاملة الاتجاه (RTL).

حقّق المشروع أهدافه الأساسية: أتمتة دورة حياة الموظف الكاملة من الاستقطاب حتى الأرشفة، بناء دورة رواتب تشغيلية، وتطبيق نموذج صلاحيات متعدد الطبقات (RBAC) يحمي حساسية البيانات الصحية والمالية. كما أثبت التوثيق التفصيلي لأكثر من 180 جدولاً موزّعة على 15 قاعدة بيانات جدّية الفريق في تطبيق مبادئ Domain-Driven Design وDatabase-per-Service عملياً لا نظرياً فقط.

في المقابل، لا يدّعي هذا العمل الاكتمال. فكما بيّنت المقارنة مع الأدبيات العلمية (القسم 2.12)، تفرض معمارية الخدمات المصغّرة كلفة تعقيد تشغيلي حقيقية، ويبقى الإصدار الحالي (v1) محكوماً بحدود واضحة: محرك رواتب غير مكتمل قانونياً، غياب اختبارات أداء وتغطية كود موثّقة رقمياً، وعدم إخضاع النظام لتقييم فعلي من مستخدمين حقيقيين في الجهة المستفيدة. تمثّل هذه الفجوات، الموثّقة بصراحة بدل إخفائها، مساراً واضحاً للإصدار الثاني (v2): إكمال محرك الرواتب القانوني، تنفيذ اختبارات حمل وتغطية فعلية، والانتقال التدريجي إلى بيئة Kubernetes للإنتاج.

يخلص الفريق إلى أن المساهمة الرئيسية لهذا العمل ليست في ابتكار تقني جديد، بل في **الترجمة العملية الدقيقة** لهيكل تنظيمي حكومي معقّد إلى حدود خدمات برمجية متماسكة (Bounded Contexts) — وهي مهارة هندسية أساسية غالباً ما تُدرَّس نظرياً دون تطبيق بهذا الحجم في مشاريع التخرج.

---

# قائمة الاختصارات

| الاختصار | المعنى بالإنجليزية | المعنى بالعربية |
|----------|-------------------|-----------------|
| API | Application Programming Interface | واجهة برمجة التطبيقات |
| CRUD | Create, Read, Update, Delete | إنشاء، قراءة، تحديث، حذف |
| DDD | Domain-Driven Design | التصميم الموجّه بالنطاق |
| EF Core | Entity Framework Core | إطار الكيانات |
| ER | Entity-Relationship | كيان–علاقة |
| ESS | Employee Self-Service | الخدمة الذاتية للموظف |
| FK | Foreign Key | مفتاح أجنبي |
| HR | Human Resources | الموارد البشرية |
| HRMS | Human Resources Management System | نظام إدارة الموارد البشرية |
| HTTP | Hypertext Transfer Protocol | بروتوكول نقل النص الفائق |
| IAM | Identity and Access Management | إدارة الهوية والوصول |
| JWT | JSON Web Token | رمز الويب JSON |
| MVC | Model-View-Controller | نموذج–عرض–متحكم |
| OIDC | OpenID Connect | بروتوكول OpenID Connect |
| ORM | Object-Relational Mapping | تعيين كائن–علاقة |
| PK | Primary Key | مفتاح أساسي |
| RBAC | Role-Based Access Control | التحكم بالوصول القائم على الأدوار |
| REST | Representational State Transfer | نقل حالة التمثيل |
| RTL | Right-to-Left | من اليمين إلى اليسار |
| SSO | Single Sign-On | تسجيل الدخول الموحّد |
| UK | Unique Key | مفتاح فريد |
| YARP | Yet Another Reverse Proxy | بوابة عكسية |

---

# المراجع

1. Evans, E. (2003). *Domain-Driven Design: Tackling Complexity in the Heart of Software*. Addison-Wesley.

2. Newman, S. (2021). *Building Microservices* (2nd ed.). O'Reilly Media.

3. Fowler, M. (2014). Microservices. *martinfowler.com*. https://martinfowler.com/articles/microservices.html

4. Richardson, C. (2018). *Microservices Patterns*. Manning Publications.

5. Microsoft. (2025). *ASP.NET Core Documentation*. https://learn.microsoft.com/en-us/aspnet/core/

6. Microsoft. (2025). *Entity Framework Core Documentation*. https://learn.microsoft.com/en-us/ef/core/

7. MassTransit Project. (2025). *MassTransit Documentation*. https://masstransit.io/documentation

8. PostgreSQL Global Development Group. (2025). *PostgreSQL 16 Documentation*. https://www.postgresql.org/docs/16/

9. Keycloak Project. (2025). *Keycloak Documentation*. https://www.keycloak.org/documentation

10. RabbitMQ Team. (2025). *RabbitMQ Documentation*. https://www.rabbitmq.com/documentation.html

11. Microsoft. (2025). *YARP - Yet Another Reverse Proxy*. https://microsoft.github.io/reverse-proxy/

12. Docker Inc. (2025). *Docker Documentation*. https://docs.docker.com/

13. HRManagement Project Documentation. (2026). *Requirements.md, IMPLEMENTED_BASELINE.md, THESIS_TECH_STACK.md*. Internal repository docs.

14. Gamma, E., Helm, R., Johnson, R., & Vlissides, J. (1994). *Design Patterns: Elements of Reusable Object-Oriented Software*. Addison-Wesley.

15. NIST. (2020). *Role Based Access Control (RBAC)*. SP 800-162.

16. Blinowski, G., Ojdowska, A., & Przybyłek, A. (2022). Monolithic vs. Microservice Architecture: A Performance and Scalability Evaluation. *IEEE Access*, 10, 20357–20374. https://doi.org/10.1109/ACCESS.2022.3152803

17. Kolyeva, N., Kortenko, L., & Radkovskaya, E. (2023). Designing a microservice architecture on the example of an automated application for the HR department. *E3S Web of Conferences*, 402, 03043. https://doi.org/10.1051/e3sconf/202340203043

18. Gautam, D., Padole, D., & Bodkhe, S. (2025). A Microservices-Based, Scalable Human Resource Management System with Role-Based Access Control and Optimized Data Flow. *International Journal of Innovative Research in Technology*, 12(6), 5425–5430.

19. إبراهيم، آدم عباس آدم (2018). *أثر نظم معلومات الموارد البشرية على أداء المؤسسات الحكومية: دراسة حالة وزارة تنمية الموارد البشرية*. أطروحة دكتوراه، كلية الدراسات التجارية، جامعة السودان للعلوم والتكنولوجيا.

---

# الملاحق

## ملحق (أ): هيكل BaseEntity

```csharp
// HRManagement.SharedKernel
public abstract class BaseEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

## ملحق (ب): مثال حدث IEmployeeCreated

```csharp
public interface IEmployeeCreated
{
    Guid EmployeeId { get; }
    string FullNameAr { get; }
    string NationalId { get; }
    Guid? PrimaryOrgUnitId { get; }
    int SchemaVersion { get; }
}
```

## ملحق (ج): docker-compose services

- postgresql:5433
- rabbitmq:5672, 15672
- keycloak:8080

## ملحق (د): جدول الخدمات الكامل

*(انظر README.md و THESIS_PROJECT_ANALYSIS.md)*

## ملحق (هـ): Hire Checklist Steps

| Step | Code | Description |
|------|------|-------------|
| P1.1 | CandidateReceived | استلام مرشح |
| P1.2 | ContractActivated | تفعيل عقد |
| P2.1 | ProfileCreated | ملف شخصي |
| P6.1 | PayrollLinked | ربط راتب |
| P5.1 | CareerApproved | اعتماد سيرة |
| P8.1 | IdCardIssued | إصدار بطاقة |
| P7.1 | ArchiveCompleted | أرشفة |

## ملحق (و): إعدادات Keycloak (Development)

```json
"Keycloak": {
  "Authority": "http://localhost:8080/realms/hrmanagement",
  "ClientId": "hr-gateway-client"
}
```

---

*نهاية الأطروحة — جاهزة للتحويل إلى Microsoft Word*

---

# دليل التنسيق في Microsoft Word (وفق منهجية الجامعة)

| العنصر | المواصفة |
|--------|----------|
| حجم الورق | A4 |
| اللغة | العربية مع المصطلحات التقنية بالإنجليزية |
| عدد الصفحات | 50–100 (بما فيها الملاحق) |
| خط العربي | Simplified Arabic، 13.5 |
| خط الإنجليزي | Times New Roman، 12 |
| عنوان الفصل | 16، عريض، صفحة منفصلة، منتصف الصفحة |
| العناوين الرئيسية | 14.5، عريض |
| العناوين الجانبية | 14، عريض |
| العناوين الفرعية | 13.5، عريض |
| تباعد الأسطر | 1.15 |
| الهامش العلوي/السفلي/الأيسر | 2.54 سم |
| الهامش الأيمن | 3 سم (للتغليف) |
| ترقيم الأشكال | (رقم الفصل - رقم الشكل) أسفل الشكل |
| ترقيم الجداول | (رقم الفصل - رقم الجدول) أعلى الجدول |
| التوثيق | [رقم] في النص — المراجع في نهاية المشروع |
| الفهارس | تلقائية عبر Word: إدراج ← مرجع ← فهرس المحتويات |

**الغلاف:** النموذج العربي (نموذج-1) على الغلاف **الأيمن**، والإنجليزي (نموذج-2) على الغلاف **الأيسر**.
