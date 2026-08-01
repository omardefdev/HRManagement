# NFR-001 — ميزانية الاختناقات (أعلى 3)

مبني على هندسة PERF/OPS/OBS وتشغيل harness المخبري. لا يُشترط إصلاح كل اختناق داخل البطاقة؛ يُشترط توثيق أثره وسلوك التعطيل الآمن.

## 1) كتابات دفعة الرواتب / أقفال PostgreSQL

| الحقل | القيمة |
|---|---|
| العرض | ارتفاع `partition_duration` وانتظار IO عند رفع `BulkWriteSize`×DOP |
| الدليل | Meter `hrm.payroll.run`؛ انتظار DB أثناء `payroll-run` |
| الميزانية | الإبقاء على `MaxDegreeOfParallelism≤4` و`PartitionQueueCapacity` كـbackpressure |
| Degradation | إطالة زمن النافذة؛ لا تكرار أثر بفضل القيود الفريدة + checkpoint |
| فرضية تحسين | ضبط DOP حسب أنوية staging؛ فهارس SalaryLines؛ فصل read replicas للتقارير |

## 2) بوابة YARP + Rate limiting

| الحقل | القيمة |
|---|---|
| العرض | P95 يقترب من 2s أو 429 تحت spike/stress |
| الدليل | `http_req_duration` k6 + `http_server_request_duration_seconds_*` |
| الميزانية | PermitLimit البوابة؛ HPA Gateway 3→6 (OPS) |
| Degradation | 429 بدل انهيار؛ black-box J1 يبقى منفصلاً على `/ready` |
| فرضية تحسين | فصل health عن limiter؛ توسيع replicas قبل رفع VUs |

## 3) Outbox / RabbitMQ freshness (J3)

| الحقل | القيمة |
|---|---|
| العرض | `outbox_pending_messages` أو عمر الرسالة يرتفع أثناء عاصفة أحداث مع الرواتب |
| الدليل | مقاييس OBS-001 outbox أثناء load مختلط |
| الميزانية | عتبة كتالوج: pending &lt; 100 وعمر &lt; 300s لمعظم الدقائق |
| Degradation | تأخير تكاملات؛ لا فقد بفضل outbox (INT-001) |
| فرضية تحسين | زيادة consumers؛ دفعات نشر؛ عزل vhost للأحمال الموسمية |

## ترتيب المعالجة المقترح

1. تثبيت حدود PERF تحت الحمل (لا رفع DOP عشوائياً).
2. قياس Gateway P95 قبل زيادة VUs الوطنية.
3. مراقبة outbox في نافذة الرواتب قبل CAP soak 72h.
