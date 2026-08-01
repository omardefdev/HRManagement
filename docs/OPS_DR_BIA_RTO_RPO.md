# OPS-004 — BIA ومصفوفة RTO/RPO

القرار: [ADR-OPS-004-dr-rto-rpo.md](./ADR-OPS-004-dr-rto-rpo.md). الطوبولوجيا: [OPS_DR_TOPOLOGY.md](./OPS_DR_TOPOLOGY.md).  
مراجع مكوّنات: [OPS_DATA_HA_TOPOLOGY.md](./OPS_DATA_HA_TOPOLOGY.md) (failover حي)، [OPS_BACKUP_TOPOLOGY.md](./OPS_BACKUP_TOPOLOGY.md) (نسخ/PITR).

**شرعية الأهداف:**  
- **measured** = من تدريب موثّق (HA/backup/DR).  
- **interim** = هدف تشغيلي معتمد بانتظار أول drill موقع.  
- **national-ceiling** = سقف مرجعي من نموذج الجاهزية الوطني (ليس بديلاً عن BIA).

سقف مرجعي وطني (إرشادي): RPO ≤ 5 دقائق / RTO ≤ 60 دقيقة للرحلات الحرجة — يُستخدم للمقارنة فقط.

---

## 1) BIA مختصر — أثر انقطاع المنصة

| رحلة أعمال | أثر انقطاع كامل للموقع الأساسي | أولوية استعادة |
|---|---|---|
| تسجيل الدخول / OIDC | توقف كل الوحدات المعتمدة على الجلسة | P0 |
| بوابة + Upstream health | لا وصول موحّد للوحدات | P0 |
| تعيين موظف (hire) | توقف onboarding وEVENTS المرتبطة | P1 |
| فصل / قائمة إنهاء | توقف رحلات قانونية حساسة | P1 |
| تشغيل رواتب | توقف دفعات الدورة؛ أثر مالي/قانوني | P0 موسمي |
| تحميل ملف (FileStorage) | توقف أدلة ومستندات الرحلات | P1 |

---

## 2) أهداف المكوّنات (من OPS-002/003)

| مكوّن | RPO حي (HA) | RTO حي (HA) | RPO نسخ | RTO استعادة معزولة | مصدر | شرعية |
|---|---|---|---|---|---|---|
| PostgreSQL (CNPG) | ≈ 0 | ≤ 60s | ≤ 5m (WAL) / ≤ 24h dump | ≤ 2h PITR drill | OPS-002/003 | interim→measured بعد drill |
| RabbitMQ quorum | 0 (بعد confirm) | ≤ 30s | — (إعادة بناء من apps/outbox) | ≤ 1h cluster recreate | OPS-002 | interim |
| Keycloak | جلسات حسب cache | ≤ 30s | DB عبر PITR + realm ≤ 24h | ≤ 1h | OPS-002/003 | interim |
| MinIO | 0 ضمن EC | ≤ 60s | ≤ 24h mirror | ≤ 4h | OPS-002/003 | interim |
| Gateway / Web apps | — | ≤ rolling | صور SHA | ≤ 30m بعد البيانات | OPS-001 | interim |

---

## 3) مصفوفة الرحلات × أهداف موقع (DR)

أهداف **فقد إقليم كامل** (warm standby + DNS) — أوسع من failover داخل AZ:

| رحلة | RPO موقع (هدف) | RTO موقع (هدف) | اعتماديات حرجة | شرعية |
|---|---|---|---|---|
| تسجيل الدخول | ≤ 5m (هوية في Postgres WAL) | ≤ 60m | Keycloak ← Postgres ← DNS | interim (national-ceiling) |
| بوابة / Upstream | ≤ 5m بيانات التدقيق | ≤ 60m | Gateway replicas ← DNS | interim |
| تعيين / فصل | ≤ 5m أحداث+DB | ≤ 90m | HR.Core + Contracts + Rabbit | interim |
| تشغيل رواتب | ≤ 5m أثناء الدورة؛ وإلا ≤ 24h dump | ≤ 120m خارج الذروة؛ ≤ 60m داخل نافذة الصرف إن أُعلن | Payroll + Postgres + MinIO أدلة | interim |
| تحميل ملف | ≤ 24h objects إن تأخر mirror؛ ≤ 5m metadata DB | ≤ 120m | FileStorage DB + MinIO | interim |

بعد أول `dr-failover-drill` ناجح تُحدَّث خلايا **measured** في [OPS_DR_VERIFICATION.md](./OPS_DR_VERIFICATION.md).

---

## 4) ما ليس تخميناً صامتاً

- أي رقم بلا عمود «شرعية» يُرفض عند المراجعة.
- أهداف HA داخل الإقليم **لا** تُحسب كدليل DR لموقع ثانوي.
- الفجوات المادية (طاقة/اتصال الموقع الثانوي) → **PHY-001**. الإجراءات اليدوية الورقية → **BCP-001**.
