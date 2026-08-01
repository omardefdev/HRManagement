# مسارات الاستثناء — FUN-004

| مسار | السلوك |
|---|---|
| HR عام يفتح Medical Details | Forbid (سياسة Clinical) أو Summary غير سريري |
| توصية بلا موافقة | رفض؛ EmergencyOverride + audit مسموح |
| بطاقة تأمين منتهية | MarkExpired / Revoke؛ soft-cancel |
| إتمام تدريب بلا خطوة Enrolled | رفض |
| شهادة غير معروفة | Verify → NotFound/Invalid |
| تعيين محقق = المبلغ عنه | رفض SoD |
| إصدار تأديب أثناء Appeal Pending | CanIssue يرفض |
| مرفق غير نظيف FILE-002 | RegisterReference يفشل — رفض الربط |
| إلغاء شكوى مغلقة | مرفوض؛ soft-cancel قبل الإغلاق |

راجع [ADR-FUN-004](ADR-FUN-004-health-training-compliance-closure.md).
