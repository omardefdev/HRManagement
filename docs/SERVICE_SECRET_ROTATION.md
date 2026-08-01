# تدوير أسرار هوية الخدمة (IAM-004)

كل workload يملك سراً مستقلاً. تدوير سر واحد لا يوقف الخدمات الأخرى.

## أنواع الأسرار

| السر | أين | نطاق الأثر عند التدوير |
|------|-----|-------------------------|
| `ServiceIdentity__ClientSecret` / `SVC_*_CLIENT_SECRET` | Keycloak client + env للخدمة | تلك الخدمة فقط (token cache يُفرَّغ عند إعادة التشغيل) |
| `Keycloak__ClientSecret` | البوابة فقط | جلسات المستخدمين التفاعلية |
| `POSTGRES_*_APP_PASSWORD` | PostgreSQL role + connection string | خدمة واحدة |
| `RABBITMQ_*_PASS` / `mq_*` | RabbitMQ user | خدمة واحدة |
| `MINIO_FILESTORAGE_*` | File.Storage فقط | تخزين الملفات فقط — لا يمس DB/MQ |

## تدوير عميل Keycloak لخدمة

1. في Keycloak → Clients → `svc-<name>` → Credentials → Regenerate.
2. حدّث المتغير في مدير الأسرار / `.env` (مثلاً `SVC_PAYROLL_CLIENT_SECRET`).
3. أعد نشر **تلك الخدمة فقط**.
4. تحقق: health جاهز؛ نداء S2S ناجح؛ بقية الخدمات ما زالت healthy بلا إعادة تشغيل.

## تدوير كلمة مرور PostgreSQL لخدمة

1. `ALTER ROLE payroll_app PASSWORD '...';`
2. حدّث `POSTGRES_PAYROLL_APP_PASSWORD` وأعد نشر payroll فقط.
3. تحقق: `scripts/verify-postgres-service-roles.sh` ما زال يرفض CONNECT عبر القواعد.

## تدوير RabbitMQ

1. `rabbitmqctl change_password mq_payroll '...'` أو أعد تشغيل `scripts/init-rabbitmq-service-users.sh` بعد تحديث env.
2. أعد نشر الخدمة المعنية فقط.

## تدوير MinIO لـ File.Storage

1. أنشئ مفتاحاً جديداً عبر `scripts/init-minio-filestorage-user.sh` (أو UI).
2. حدّث `MINIO_FILESTORAGE_ACCESS_KEY` / `SECRET_KEY` على **file-storage-web فقط**.
3. عطّل المفتاح القديم بعد نجاح القراءة/الكتابة.

## إثبات عدم التوقف الجماعي

اختبار الوحدة `ClientCredentialsTokenProvider_RotationClearsCache_OtherClientsUnaffected` يثبت أن مسح/تدوير كاش عميل لا يبطل توكن عميل آخر.
