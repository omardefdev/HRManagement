# ADR-REL-001 — Staging مماثل للإنتاج وبيانات اصطناعية

| الحقل | القيمة |
|---|---|
| الحالة | Accepted |
| البطاقة | REL-001 (البطاقة 56) |
| التاريخ | 2026-07-30 |
| الاعتماديات | OPS-005 Done، QA-004 Done |

## السياق

مسارات staging (Kustomize/TF/compose) وE2E/NFR موجودة، لكن بلا مصفوفة parity موثّقة، بلا reset اصطناعي موحّد، وبلا بوابة rehearsal إغلاق تحت REL-001. Drift TLS/replicas بين staging-ha وproduction-ha غير مُدار كمصفوفة.

## القرار

| الموضوع | الاختيار |
|---|---|
| مصفوفة الرحلة | `docs/rel-staging/parity-matrix.json` — TopologyParity / SyntheticReset / ReleaseRehearsal |
| مصدر حقيقة staging | Kustomize `staging-ha` + TF `terraform/envs/staging` |
| Compose | lab/dev — **allowed drift** موثّق (ليس بديلاً صامتاً عن staging-ha) |
| Parity | `scripts/rel001-parity-diff.ps1` يفشل على drift غير `approved`/`match` |
| TLS staging | تفعيل TLS stub على ingress staging (lab secret) — محاذاة شكلية مع prod |
| Synthetic | `testdata/rel001/` + NFR generator بمقياس pilot؛ حظر prod dump/PII |
| Keycloak | `realm-rel001-staging.json` + أدوار QA-004/services |
| Flags | `Gateway:EnabledModules` موثّق في `REL_001_FEATURE_FLAGS.md` |
| Ownership | RACI في `REL_001_ENV_OWNERSHIP.md` |
| UAT | ReadyForSign-off (`REL_001_UAT_PACK.md`) |
| خارج النطاق | REL-002 Go/No-Go؛ parallel payroll؛ national seed؛ Aspire |

## العواقب

- أي فرق staging↔prod غير مدرج في المصفوفة يفشل parity-diff.
- Reset staging قابل للتكرار من صفر بدون بيانات إنتاج.
- Rehearsal يمدّ DEP-001 بنفس digest قبل الترقية.
