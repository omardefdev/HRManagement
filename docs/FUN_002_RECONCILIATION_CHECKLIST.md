# قائمة مصالحة رحلة الفصل (FUN-002-C)

استخدم بعد timeout / fault / cancel قبل Resume أو إغلاق التدخل.

| # | فحص | أين |
|---|---|---|
| 1 | SeparationRecord: IsCancelled؟ SeparationEventPublished؟ EffectiveDate | HR.Contracts |
| 2 | SeparationChecklist: خطوات إلزامية + EvidenceRef + OwnerRole | HR.Contracts |
| 3 | Employee status / LegalHold | HR.Core |
| 4 | Contract terminated؟ | HR.Contracts |
| 5 | EmployeePayProfile.IsActive=false؛ EOS Draft/Cancelled/Paid | Payroll |
| 6 | AuthUser.IsActive | Auth.Directory |
| 7 | بطاقة ملغاة / طلب revoke | IDCard |
| 8 | أصول مُعادة / تذكرة | Admin.Logistics |
| 9 | وصول IT مُلغى | IT.Infra |
| 10 | أرشيف قيد | Records.Archive |
| 11 | SeparationSagaState CurrentState | Contracts DB |
| 12 | ProcessIntervention مفتوح؟ | api/process-interventions |

## تعويض عند Cancel

`ICancelSeparationSaga` (أو CancelSeparation soft-cancel بعد النشر) يعيد إرسال `IExecuteSeparationParty` بـ`Compensate=true` للأطراف المكتملة (عكس الترتيب):

| طرف | تعويض v1 |
|---|---|
| Payroll | إلغاء مسودة EOS (`EosStatus.Cancelled`) — **ليس** PAY-003 Reverse لراتب مرحّل |
| Auth.Directory | إعادة تفعيل AuthUser إن عُطّل أثناء الـsaga |
| IDCard / Admin.Logistics / IT.Infra / Archive / HR.Core | ACK تعويض خفيف (وسم evidence) |

Resume يعيد Dispatch للأطراف المعلّقة فقط.
