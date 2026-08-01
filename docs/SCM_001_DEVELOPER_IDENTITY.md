# SCM-001 — هوية المطورين وإلغاء الوصول

## MFA

- MFA إلزامي على كل حساب GitHub في المنظمة (org setting: Require two-factor authentication).
- حسابات الخدمة (bots) تستخدم fine-grained PATs أو GitHub Apps بلا كلمة مرور تفاعلية.

## الهوية والعزو

| المتطلب | التفاصيل |
|---|---|
| Commit author | يطابق حساب GitHub المعروف |
| Signed commits | انظر [SCM_001_SIGNING_POLICY.md](./SCM_001_SIGNING_POLICY.md) |
| CODEOWNERS | مالك مسار + مراجع مستقل للمسارات الحرجة |
| Audit | GitHub audit log يُراجع ربع سنوياً |

## Offboarding (مغادرة مطور)

نفَّذ خلال **ساعة عمل واحدة** من آخر يوم:

1. إزالة من GitHub org / الفرق / CODEOWNERS.
2. إبطال SSH keys و GPG keys و PATs.
3. إبطال وصول Keycloak (إن وُجد حساب تطوير).
4. إبطال VPN / bastion / cloud IAM.
5. تدوير أي أسرار شارك فيها (SEC-004).
6. تسجيل الحدث في سجل الحوكمة (STR-005 decision log أو security incident log).

قائمة تحقق: `docs/scm-governance/offboarding-checklist.md`

## ربط SRE-001

مناوبو الإنتاج يحصلون على وصول تشغيلي منفصل (انظر [SRE_001_ACCESS_MATRIX.md](./SRE_001_ACCESS_MATRIX.md))؛ إلغاء وصول التطوير لا يعني بالضرورة إلغاء مناوبة — يُنسَّق مع Service Owner.
