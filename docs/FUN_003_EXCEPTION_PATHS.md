# مسارات الاستثناء — FUN-003

| مسار | السلوك |
|---|---|
| تاريخ نفاذ مستقبلي (Transfer.EffectiveDate / StartDate) | رفض Execute/Activate برسالة واضحة؛ soft-cancel مسموح |
| تعارض حركة متزامنة | رفض Execute إن وُجد نقل/انتداب/إعارة Approved|Active|Executed لنفس الموظف |
| فصل/إجازة متداخلة (بوابة محلية) | نفس بوابة التعارض عبر حركات نشطة؛ تكامل Leave/Separation أعمق خارج هذه البطاقة |
| إلغاء قبل التنفيذ | soft-cancel (`IsCancelled`) — لا حذف |
| إلغاء بعد بدء saga | `ICancel*Saga` → Compensating → Cancelled |
| إنهاء انتداب | Close → Ended checklist + `ICancelSecondmentSaga` من Completed لاستعادة الأطراف |
| إنهاء إعارة | Close → `ILoanAssignmentEnded` |
| صلاحية مصدر/هدف | AND على From+To / Lending+Borrowing لكل مسار مطفر |
| SoD | Secondment/Loan Approve: لا self-approve؛ رفض forged approver |
| Intervention | Resume أو Cancel عبر `/api/process-interventions` |

راجع [ADR-FUN-003](ADR-FUN-003-movements-journey-closure.md) و [FUN_003_RECONCILIATION_CHECKLIST](FUN_003_RECONCILIATION_CHECKLIST.md).
