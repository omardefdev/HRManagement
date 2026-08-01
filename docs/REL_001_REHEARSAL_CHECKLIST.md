# REL-001 — قائمة تحقق Release Rehearsal

يمدّ [DEP_001_RELEASE_GATE.md](DEP_001_RELEASE_GATE.md) ببوابة staging كاملة قبل ترقية نفس الـdigest.

## قبل النشر

- [ ] اختيار `IMAGE_TAG` / SHA من بناء CI ناجح (لا `:latest`).
- [ ] `pwsh scripts/rel001-parity-diff.ps1` ناجح.
- [ ] مراجعة [REL_001_FEATURE_FLAGS.md](REL_001_FEATURE_FLAGS.md) — لا وحدات غير منشورة.
- [ ] تأكيد مالك promote وفق [REL_001_ENV_OWNERSHIP.md](REL_001_ENV_OWNERSHIP.md).

## نشر staging

- [ ] Deploy إلى staging بنفس الوسم (`environment=staging` أو `kubectl apply -k deploy/k8s/overlays/staging-ha`).
- [ ] Migrations (`scripts/run-migrations.sh` أو startup migrate الموثّق).
- [ ] `pwsh scripts/rel001-reset-staging.ps1 -SkipCompose` **أو** seed فقط إذا العنقود قائم: `rel001-seed-synthetic.ps1`.
- [ ] استيراد Keycloak: services → qa004-e2e → `realm-rel001-staging.json` (lab).

## تحقق

- [ ] `pwsh scripts/verify-staging-stack.ps1` (أو health على Ingress staging).
- [ ] `pwsh scripts/run-staging-e2e.ps1 -SkipComposeUp` (QA-004 subset / journeys).
- [ ] Scanners على نفس digest (Trivy/Gitleaks/CodeQL من CI — لا إعادة بناء).
- [ ] OBS: `scripts/obs-synthetic-journey.sh` و/أو `obs-pii-scan.sh` على حزم staging (لا PII إنتاج).

## ترقية

- [ ] تسجيل نتيجة rehearsal في UAT pack.
- [ ] Production بنفس `IMAGE_TAG` فقط (DEP-001).
- [ ] Rollback plan جاهز من `deployments.log`.

**الحالة المستهدفة:** ReadyForSign-off ضمن `REL_001_UAT_PACK.md`.
