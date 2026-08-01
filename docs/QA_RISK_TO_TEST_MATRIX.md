# مصفوفة مخاطر → اختبار (QA-002)

مصدر المخاطر: [DATA_INVARIANTS_INVENTORY.md](./DATA_INVARIANTS_INVENTORY.md)، رحلات J1–J4، hire/payroll/RBAC.  
المالك الافتراضي: فريق المجال المذكور؛ مراجعة مسارات الاختبار عبر `.github/CODEOWNERS`.

| ID | المخاطر / Invariant | أولوية | الطبقة | اختبار نجاح | اختبار فشل | مالك | حالة |
|---|---|---|---|---|---|---|---|
| INV-01 | سطر راتب واحد لكل موظف/تشغيل | P0 | Integration (TC) | `SalaryLine_ConcurrentInsert_SecondFailsUnique` (يبقى سطر واحد) | نفس الاختبار — الإدراج الثاني UniqueViolation | Payroll | Covered |
| INV-02 | ملف دفع نشط واحد لكل موظف | P0 | Unit | `INV02_SingleActivePayProfile_Succeeds` | `INV02_SecondActivePayProfile_FailsUnique` | Payroll | Covered |
| INV-03 | تشغيل حي واحد لكل جهة/فترة/نوع | P0 | Integration (TC) | `PayrollRun_ConcurrentCreate_SamePeriod_OneWins` | نفس — الثاني يفشل | Payroll | Covered |
| INV-04 | دفعة بنكية فريدة لكل مرجع ضمن التشغيل | P0 | Unit | `INV04_PaymentBatch_UniqueRef_Succeeds` | `INV04_PaymentBatch_DuplicateRef_FailsUnique` | Payroll | Covered |
| INV-06 | عدّاد مراجع متسق | P0 | Integration | `FileReference_ConcurrentRegister_CountConsistent` / `File001` reconcile | تناقض العدد يُكشف عبر reconcile | File | Covered |
| INV-07 | لا حذف ملف وعليه مراجع | P0 | Integration | `File_PendingPurge_NoImmediateObjectDelete` | `File_Delete_WithRefs_Rejected` | File | Covered |
| INV-08 | رصيد إجازة لا يُسحب فوق الاستحقاق | P0 | Integration (TC) + Unit | `LeaveBalance_ConcurrentDebit_NoOverdraw`؛ `INV08_DebitWithinEntitlement_Succeeds` | `INV08_DebitAboveEntitlement_Rejected` | Contracts | Covered |
| INV-09 | طلب إجازة لا يُعتمد مرتين | P0 | Unit / Integration | اعتماد أول ينجح (`ApproveLeave` happy) | `Approve_AlreadyApproved_ReturnsRedirectWithError`؛ SoD leave | Contracts/HR.Core | Covered |
| INV-10 | طلب نقل لا يُنفَّذ مرتين | P0 | Integration (TC) | `Transfer_ConcurrentExecute_OneSucceeds` (wins=1) | المحاولات اللاحقة لا تغيّر الحالة | Movements | Covered |
| INV-11 | كمية مخزون لا تصبح سالبة | P1 | Integration (TC) | `Inventory_ConcurrentDecrement_NoNegative` | نفس — الكمية ≥ 0 | Admin | Covered |
| INV-12 | طلب اعتماد لا يُحدَّث بصمت | P1 | Unit | `ApprovalWorkflowTests` مسارات نجاح | `Approve_AlreadyApproved_ReturnsRedirectWithError` | HR.Core | Covered |
| INV-13 | إعادة POST لنفس العملية (Idempotency) | P0 | Integration (TC) + Unit | أول مفتاح يُحفظ | `IdempotencyKey_DuplicateKey_FailsUnique`؛ `INV13_*` | Platform | Covered |
| PAY-ENG | محرك صافي الراتب / تجميد النتائج | P0 | Unit + Mutation | `PayrollCalculation*` / lifecycle بعد `MarkValidated` | `ValidateCanModifyLines` بعد freeze؛ Stryker | Payroll | Covered |
| SOD | فصل الصلاحيات maker/checker | P1 | Integration | `SoDPolicyIntegrationTests` | رفض نفس الممثل | HR.Core | Covered |

## Mutation

| نطاق | Workflow | عتبة | صف مصفوفة |
|---|---|---|---|
| `Payroll.Domain` | `.github/workflows/mutation.yml` + `tests/stryker-payroll.json` | ≥ 60% | PAY-ENG |

## ملاحظات

- اختبارات `Category=Testcontainers` تُشغَّل على `main` أو وسم `integration`؛ بوابات الوحدة تغطي قواعد المجال المكافئة حيث وُثّق أعلاه.
- أي صف جديد P0/P1 يُضاف هنا قبل الدمج، مع مالك ومعرّف اختبار فشل.
