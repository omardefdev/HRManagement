# ADR-OPS-005 — Infrastructure as Code

| الحقل | القيمة |
|---|---|
| الحالة | Accepted |
| البطاقة | OPS-005 |
| التاريخ | 2026-07-28 |

## السياق

المنصة لديها Kustomize لـHA/النسخ/DR stubs (OPS-001..004) دون طبقة توفير سحابي. الأسرار placeholders بقيم؛ لا remote state ولا بوابة PR للبنية؛ موقع DR الثانوي ينتظر عنقوداً حقيقياً.

## القرار

1. **Terraform ≥ 1.5** كأداة IaC الوحيدة في الموجة الأولى.
2. **AWS أولاً:** VPC، EKS، S3 (نسخ + state)، Route53 stubs، KMS، IRSA — مع وحدات قابلة لامتداد Azure لاحقاً.
3. **Remote state:** S3 + DynamoDB lock؛ لا state في git.
4. **طبقتان:** Terraform = شبكة/عنقود/مخزن/DNS/جسر أسرار؛ Kustomize overlays الحالية = أحمال العمل (لا Helm في هذه البطاقة).
5. **أسرار = مراجع** عبر External Secrets Operator → AWS Secrets Manager/SSM؛ placeholders للتطوير المحلي فقط (`dev-only`).
6. **Production apply** عبر PR + GitHub Environment approval فقط.
7. **prevent_destroy** على موارد البيانات/النسخ؛ drift عبر `terraform plan -detailed-exitcode`.
8. **IAM-004:** لا حساب admin موحّد للتطبيقات — secrets منفصلة لكل دور خدمة.

## البدائل المرفوضة

| بديل | سبب الرفض |
|---|---|
| Pulumi/Ansible كمظلة أولى | توحيد مع مجتمع المنصة وCI الحالي |
| تحويل كل المانيفستات إلى Helm الآن | خارج نطاق البطاقة؛ overlays جاهزة |
| Active-active متعدد السحب | OPS-004 رفضه؛ يكفي secondary env |

## العواقب

- `terraform/envs/dr-secondary` يوفّر العنقود الذي تفترضه overlays OPS-004.
- REL-001 يستهلك staging الناتج عن clean-room apply.
