# نظام إدارة الموارد البشرية - شعبة التنظيم والموارد البشرية

HR Management Microservices System

## المتطلبات

- .NET 10 SDK
- Docker & Docker Compose
- PostgreSQL 16 (via Docker)
- RabbitMQ 3.13 (via Docker)
- Keycloak 24 (via Docker)

## التوثيق

- [مخططات قواعد البيانات (Mermaid ERD)](docs/database/README.md) — 13 قاعدة بيانات، علاقات داخلية ومنطقية بين الخدمات.
- [خط الأساس المنفّذ](docs/IMPLEMENTED_BASELINE.md) — ما تم إنجازه في الشيفرة.
- [ما يظل للمنتج الكامل](docs/REMAINING_IMPLEMENTATION.md) — فجوات ومسار إكمال.
- [تجهيز الإصدار / staging](docs/RELEASE_READINESS_STAGING.md) — تحقق تلقائي ويدوي قبل الإنتاج.
- [نطاق المنتج v1](docs/RELEASE_PRODUCT_SCOPE.md) — ما داخل الإصدار وما مؤجّل.
- [File Storage Platform](docs/FILE_SERVICE.md) — رفع الملفات عبر `/api/files` وإدارة FileId.

## البنية المعمارية

النظام مبني على معمارية الخدمات المصغرة (Microservices) مع 13 خدمة مستقلة:

| # | الخدمة | المنفذ | قاعدة البيانات |
|---|--------|--------|----------------|
| 1 | HR.Core - الملفات الشخصية والسير | 5010 | hr_core_db |
| 2 | HR.Contracts - العقود والخدمة | 5020 | hr_contracts_db |
| 3 | HR.Health - الصحة واللجان الطبية | 5030 | hr_health_db |
| 4 | HR.Movements - التنقلات والتوزيع | 5040 | hr_movements_db |
| 5 | HR.Training - التدريب والتأهيل | 5050 | hr_training_db |
| 6 | Payroll - الرواتب والتعويضات | 5060 | payroll_db |
| 7 | IDCard - الهويات والبطاقات | 5070 | idcard_db |
| 8 | Records.Archive - الأرشيف والملفات | 5080 | archive_db |
| 9 | Admin.Logistics - الإدارة واللوجستيات | 5090 | admin_db |
| 10 | IT.Infra - تقنية المعلومات | 5100 | it_infra_db |
| 11 | Dev.Analytics - التطوير والتحليل | 5110 | analytics_db |
| 12 | Compliance - الامتثال والرقابة | 5120 | compliance_db |
| 13 | Auth.Directory - المصادقة والدليل | 5130 | auth_directory_db |
| 14 | File.Storage - منصة الملفات | 5140 | file_storage_db |

## التشغيل

### 1. تشغيل البنية التحتية

```bash
docker-compose up -d
```

هذا سيشغل:
- PostgreSQL على المنفذ **5433** (على المضيف) لتفادي التعارض مع PostgreSQL المحلي على 5432؛ الجداول تُنشأ عند تشغيل كل خدمة عبر ترحيلات EF
- RabbitMQ على المنفذ 5672 (الإدارة: 15672)
- Keycloak على المنفذ 8080

إذا شغّلت **PostgreSQL محلياً** على المنفذ 5432 بدلاً من حاوية Docker، اضبط `Port=5432` في سلاسل الاتصال (أو User Secrets) لكل خدمة؛ حينها لا حاجة لخدمة `postgresql` في Compose.

### 2. بناء المشروع

```bash
dotnet build HRManagement.slnx
```

### 3. تشغيل خدمة معينة

```bash
cd src/Services/HRCore/HR.Core.Web
dotnet run --urls="http://localhost:5010"
```

### 4. تشغيل البوابة

```bash
cd src/Gateway/HRManagement.Gateway
dotnet run --urls="http://localhost:5000"
```

## هيكل المشروع

```
HRManagement/
  HRManagement.slnx              # ملف الحل
  docker-compose.yml              # البنية التحتية
  .env                            # متغيرات البيئة
  src/
    Gateway/                      # بوابة YARP
    Shared/
      HRManagement.SharedKernel/  # الكيانات الأساسية والتعدادات
      HRManagement.MessageContracts/ # عقود الرسائل (RabbitMQ)
      HRManagement.ServiceDefaults/  # إعدادات مشتركة
    Services/
      {ServiceName}/
        {Service}.Domain/         # الكيانات والتعدادات
        {Service}.Infrastructure/ # قاعدة البيانات والمستهلكون
        {Service}.Web/            # واجهة MVC
```

## التقنيات المستخدمة

- **ASP.NET Core MVC** مع Razor Views
- **Entity Framework Core** مع PostgreSQL (Npgsql)
- **MassTransit** مع RabbitMQ للتواصل غير المتزامن
- **YARP** كبوابة عكسية (Reverse Proxy)
- **Keycloak** للمصادقة عبر OpenID Connect
- **AdminLTE 3** مع دعم RTL للواجهة العربية
- **خط ITF Qomra Arabic** مع الهوية البصرية السورية

## الهوية البصرية

الألوان المستخدمة:
- بورغندي أساسي: `#6b1f2a`
- أخضر مائي: `#428177`
- ذهبي: `#b9a779`
- كريمي: `#edebe0`
