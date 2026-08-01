# DEP-001 — بوابة الإصدار لمسار HA (OPS-001)

هذه الوثيقة تُغلق فجوات قبول **DEP-001** الحرجة قبل/مع أول موجة Kubernetes دون إعادة بناء مسار النشر. المسار التشغيلي: [PRODUCTION_RUNBOOK.md](./PRODUCTION_RUNBOOK.md) و[`.github/workflows/deploy.yml`](../.github/workflows/deploy.yml).

## معايير القبول ↔ الأدلة

| معيار | دليل |
|---|---|
| digest/SHA المنشور هو نفسه الذي بناه CI | `deploy.yml` يضع وسم `${GITHUB_SHA::12}` فقط (لا `:latest`)؛ خطوة `Record digest` ترفع `image@sha256:…` كـartifact لمدة 90 يوماً |
| فشل SSH / migration / readiness يفشل الإصدار | لا `continue-on-error` على SSH؛ job `migrate` يفشل عند خطأ؛ health gate بعد النشر؛ فشل البوابة يطلق auto-rollback |
| rollback يعيد وسم SHA سابق | `action=rollback` + `rollback_tag`؛ أو auto-rollback من `deployments.log` عبر `scripts/record-deployment.sh` |
| الخادم لا يحتاج source/build | `docker-compose.images.yml` يسحب من GHCR؛ k8s يستخدم نفس الصور (`ghcr.io/.../hrmanagement/{service}:{IMAGE_TAG}`) |

## ترقية digest (promotion)

1. **Build:** كل خدمة تُدفع بوسم SHA ثابت + يُسجَّل digest.
2. **Staging:** `workflow_dispatch` بـ`environment=staging` و`IMAGE_TAG=<sha12>`.
3. **Rehearsal:** تشغيل `scripts/verify-staging-stack.ps1` (أو curl على `/health/ready`) بعد النشر.
4. **Production:** نفس `IMAGE_TAG` عبر `environment=production` — لا إعادة بناء؛ ترقية الوسم المختبَر فقط.

## قائمة تحقق rehearsal staging (حد أدنى)

- [ ] اختيار SHA من آخر بناء ناجح (أو tag `v*`).
- [ ] تشغيل migrate مع advisory lock (`scripts/run-migrations.sh`).
- [ ] Pull/up بـ`docker-compose.images.yml` **أو** `kubectl apply -k deploy/k8s/overlays/staging-ha` بعد ضبط الصورة.
- [ ] نجاح health gate على Gateway.
- [ ] محاكاة فشل: وسم صورة خاطئ → يجب أن تفشل البوابة أو الـrollout.
- [ ] Rollback إلى SHA سابق من `deployments.log`.
- [ ] مقارنة digest artifact مع ما يعمل على المضيف/العنقود (`docker image inspect` / `kubectl describe pod`).

## استهلاك DEP-001 من OPS-001

مانيفستات [`deploy/k8s/`](../deploy/k8s/) تستبدل `newTag` في Kustomize بنفس `IMAGE_TAG`؛ لا مسار بناء محلي في الإنتاج HA.
