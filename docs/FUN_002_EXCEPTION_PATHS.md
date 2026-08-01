# FUN-002 — مسارات الاستثناء

| سيناريو | السلوك |
|---|---|
| تاريخ نفاذ مستقبلي | إنشاء السجل مسموح؛ **Publish مرفوض** حتى `EffectiveDate <= اليوم` |
| إلغاء قبل النشر | soft-cancel (`IsCancelled`) — **لا حذف** |
| إلغاء بعد بدء الـsaga وقبل النفاذ | soft-cancel + `ICancelSeparationSaga` → Compensate |
| إلغاء بعد النفاذ + منشور | مرفوض (`CanCancel=false`) |
| Legal Hold | Publish مرفوض عبر `IEmployeeLifecycleGate` |
| طرف معطل / timeout | `AwaitingIntervention` + ProcessInterventions؛ Resume بعد الإصلاح |
| استئناف (Appeal) | تصحيح الطرف ثم Resume — لا حذف تاريخ |
| إعادة توظيف (Rehire) | SeparationRecord (ملغى أو مكتمل) يبقى؛ تعيين لاحق عبر FUN-001 |
| تسوية مرحّلة Posted خاطئة | خارج saga — استخدم PAY-003 Reverse |
| NationalId / بيانات هوية | تصحيح عبر DATA-002؛ لا يعيد فتح فصل مكتمل تلقائياً |

قناة التدخل الدنيا: `GET/POST /api/process-interventions` على HR.Contracts.
