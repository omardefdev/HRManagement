# REL-001 — حزمة UAT (ReadyForSign-off)

| # | سيناريو | نتيجة متوقعة | توقيع |
|---|---|---|---|
| U1 | تشغيل parity-diff | نجاح كل المقارنات | |
| U2 | فحص ingress staging | tls + ssl-redirect + host staging | |
| U3 | seed 1000 موظف اصطناعي | ملفات في data/rel001-seed بلا PII إنتاج | |
| U4 | رفض ProdDumpPath | خطأ صريح | |
| U5 | reset -DryRun | خطوات موثّقة بدون أثر | |
| U6 | reset lab (اختياري بيئة) | down→up→seed→verify | |
| U7 | verify-staging-stack | health OK | |
| U8 | E2E smoke / journeys | تحدي مصادقة أو نجاح persona | |
| U9 | Feature flags قائمة كاملة | لا وحدة زائدة | |
| U10 | Rehearsal checklist + نفس digest | جاهز للترقية DEP-001 | |

**الحالة:** ReadyForSign-off — التوقيع الخارجي ضمن UAT-001 / promote program.
