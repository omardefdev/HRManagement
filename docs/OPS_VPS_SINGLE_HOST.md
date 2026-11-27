# OPS — خادم واحد للعرض والاختبار (PLAN-36)

> **بيانات اصطناعية فقط.** الخادم في «وضع العرض» (D-36-1): الخدمات تتصل بحساب PostgreSQL وRabbitMQ الإداري. لا تُحمَّل
> عليه بيانات شخصية حقيقية. الأدوار الأقل صلاحية ← PLAN-38، وRLS ← الموجة 2.

الخطة: [PLAN-36](acceptance-plans/PLAN-36-ops-production-boot-single-host.md). التقرير: `reports/plan36-final-report.md`.

## 1. الصورة العامة

```
الإنترنت ──443/tcp+udp──> edge (Caddy 2.8.4, 172.28.0.10) ──> hr-gateway:8080 ──> 14 وحدة (شبكة application الداخلية)
                                                      └────> keycloak:8080
SSH 22 ──> المضيف (المستخدم hrm، بالمفتاح فقط)          Seq: 127.0.0.1:5380 (نفق SSH)
```

- المنفذان المفتوحان للإنترنت: **22** (SSH) و**443** (Caddy). لا شيء آخر منشور؛ `scripts/vps/lint-compose.ps1` يفرض ذلك.
- الصور تبنيها GitHub Actions وتدفعها إلى GHCR (§4)، والخادم يسحبها بوسم `<sha12>` فقط. لا شيفرة ولا SDK على الخادم.
- ملفات compose: `docker-compose.yml` + `services` + `images` + `production` + `vps` (+ `vps-demo` حين
  `HRM_DEPLOYMENT_MODE=demo`). مشروع compose ثابت اسمه `hrm`، فتبقى الـ volumes بين الإصدارات.
- البنية على الخادم:

| المسار | المحتوى |
|--------|---------|
| `/opt/hrm/shared/.env` | ملف البيئة الوحيد (600، المالك `hrm`). لا يُنسخ من أي جهاز |
| `/opt/hrm/releases/<tag>` | حزمة كل إصدار (ملفات compose، `deploy/vps/*`، realm العرض) |
| `/opt/hrm/current` | رابط إلى الإصدار العامل |
| `/opt/hrm/previous-tag` | الوسم السابق للتراجع |
| `/opt/hrm/deployments.log` | سجل النشر والتراجع |

## 2. تهيئة المضيف (مرة واحدة، root)

```bash
scp deploy/vps/host-setup.sh hrm-vps:/root/
ssh hrm-vps "bash /root/host-setup.sh"
```

ما يفعله (وكل خطوة تطبع `CHANGED` أو `unchanged`): Docker Engine وCompose من مستودع Docker الرسمي؛ swap بحجم 8 GiB
و`vm.swappiness=10`؛ تدوير سجلات Docker (`10m` × 3)؛ `ufw` يرفض الوارد ويسمح بـ 22/tcp و443/tcp و443/udp (قاعدة SSH
قبل التفعيل)؛ ترقيات أمنية تلقائية دون إعادة تشغيل؛ المنطقة الزمنية UTC؛ المستخدم `hrm` بلا كلمة مرور، في مجموعة
`docker`، مع `sudo` بلا كلمة مرور، ومفتاح المالك منسوخ من root؛ `/opt/hrm/{releases,shared}` بصلاحية 750.

### تشديد SSH (بالترتيب، ولا يُتجاوز)

1. تحقّق من المستخدم الجديد **قبل** لمس sshd:
   `ssh -o BatchMode=yes -o User=hrm hrm-vps "id && sudo -n true && docker ps"`
2. `ssh hrm-vps "bash /root/host-setup.sh --harden-ssh"` — يكتب `/etc/ssh/sshd_config.d/00-hrm-hardening.conf`
   (`PermitRootLogin no`، `PasswordAuthentication no`، `KbdInteractiveAuthentication no`، `PubkeyAuthentication yes`).
   الاسم يبدأ بـ `00-` لأن sshd يأخذ **أول** قيمة يقرؤها، و`Include` في `sshd_config` يأتي أولاً. أي ملف آخر يفعّل كلمات
   المرور يُنقل إلى `/root/sshd-backup-<ts>/`. ثم `sshd -t`، ثم `systemctl reload ssh` (الجلسات القائمة تبقى).
3. تحقّق من Windows، كل سطر اتصال جديد:
   - `ssh -o BatchMode=yes -o User=hrm hrm-vps "echo ok"` ← `ok`
   - `ssh -o BatchMode=yes -o User=root hrm-vps "echo ok"` ← مرفوض
   - `ssh -o BatchMode=yes -o PubkeyAuthentication=no -o PreferredAuthentications=password,keyboard-interactive -o User=hrm hrm-vps "echo ok"` ← `Permission denied (publickey).`
4. إن فشل أي فحص: `ssh -o User=hrm hrm-vps "sudo bash /root/host-setup.sh --undo-harden-ssh"` (وبعد أول نشر:
   `sudo bash /opt/hrm/current/deploy/vps/host-setup.sh --undo-harden-ssh`).
5. حوّل الاسم المستعار إلى `hrm` في `%USERPROFILE%\.ssh\config` (سطر `User hrm` في كتلة `hrm-vps`)، ثم
   `ssh hrm-vps whoami` ← `hrm`. ثم احذف نسخة الإعداد: `ssh hrm-vps "sudo rm -f /root/host-setup.sh"`.

### الاستعادة إن انقطع SSH (لوحة Contabo ← VNC)

```bash
# بعد الدخول كـ root من شاشة VNC:
rm -f /etc/ssh/sshd_config.d/00-hrm-hardening.conf
systemctl reload ssh
ufw allow 22/tcp
```

**بعد انتهاء الحاجة إليه: عطّل VNC من لوحة Contabo.**

## 3. الأسماء وDNS

- بنطاق: سجلّا **A** إلى عنوان الخادم: `app.<domain>` و`auth.<domain>` (على Cloudflare: **DNS only**، السحابة الرمادية).
- بلا نطاق: `hr.161-97-185-71.sslip.io` و`sso.161-97-185-71.sslip.io` (sslip.io يحلّ الاسم إلى العنوان المكتوب فيه).
- تحقّق: `Resolve-DnsName <APP_DOMAIN> -Server 1.1.1.1`.
- الشهادات: Caddy يطلبها من Let's Encrypt بتحدي **TLS-ALPN على 443 وحده** (المنفذ 80 مغلق، وتحدي HTTP معطل). ابدأ
  بـ staging (`HRM_ACME_CA` في `.env`)، وبعد أول اختبار ذاتي أخضر انتقل إلى production وأعد إنشاء الحافة وحدها:
  `docker compose ... up -d --force-recreate edge` (أو أعد النشر).

## 4. السجل

**منذ 2026-11-26 الصور تُبنى في GitHub Actions وتُسحب من GHCR.** لا يُبنى شيء على الخادم ولا على جهاز المالك (القسم
«صور الإصدارات على GHCR» في آخر هذا الملف يروي النقل وجدول تغييراته).

```bash
git push origin <branch>                                        # الإيداع المراد بناؤه رأسُ فرع مدفوع
gh workflow run deploy.yml --ref <branch> -f action=build       # يبني ويدفع ولا ينشر شيئاً
gh run list --workflow deploy.yml --limit 1                     # رقم التشغيل
gh run watch <run-id> --exit-status                             # نحو 12 دقيقة
```

- **ما يبنيه:** مهمة `build-and-push` في [deploy.yml](../.github/workflows/deploy.yml): 17 صورة بالتوازي (الخدمات الخمس
  عشرة من `docker/service-build-matrix.json`، و`hr-migrate`، و`hr-rabbitmq`) إلى
  `ghcr.io/omardefdev/hrmanagement/<image>:<sha12>`. الوسم أول 12 حرفاً من إيداع رأس الفرع، وهو الوسم الوحيد (لا `latest`)،
  وكل صورة تحمل `org.opencontainers.image.revision=<sha12>`. اختبار الوحدة
  `Release_images_workflow_builds_the_seventeen_images…` يطابق المصفوفة بملف المصفوفة.
- **`action=build` لا ينشر:** مهمتا `deploy` و`rollback` (بيئتا staging وproduction، لم تُستعملا بعد) تُتخطيان. `action=deploy`
  ووسم `v*` يبنيان ثم ينشران على بيئة، وهذا ليس طريق خادم العرض.
- **بعد البناء في كل مهمة:** فحص Trivy (CRITICAL وHIGH التي لها إصلاح تُفشل المهمة)، ثم SBOM، ثم توقيع cosign وإثبات
  المصدر. **الدفع يسبق الفحص:** مهمة حمراء عند Trivy صورتها على GHCR لكن بلا توقيع.
- **الحزم عامة** لأن المستودع عام (تُقرأ بلا دخول: `docker manifest inspect ghcr.io/omardefdev/hrmanagement/hr-gateway:<sha12>`)،
  فالخادم يسحب بلا رمز. إن جُعلت خاصة: رمز **classic** للقراءة فقط (`read:packages`) يلصقه المالك في طرفيته:
  `ssh -t hrm-vps "docker login ghcr.io -u <owner>"`.
- **على الخادم:** `HRM_IMAGE_REPO=ghcr.io/omardefdev/hrmanagement` في `/opt/hrm/shared/.env`.
- **إيداع أقدم** (هدف تراجع ليس على GHCR): `gh workflow run deploy.yml --ref main -f action=build -f source_sha=<SHA الكامل>`
  يبني ذلك الإيداع بمصفوفة `main` الحالية ويسمه بأول 12 حرفاً منه. يصلح ما دامت ملفات البناء في ذلك الإيداع تقبل المصفوفة
  نفسها (لم تتغير منذ PLAN-38).

### الطرق السابقة (للتاريخ، وللطوارئ)

- **الدفع من جهاز المالك:** `scripts/vps/build-push-images.ps1 -Registry ghcr.io/<owner>/hrmanagement` (يحتاج `docker buildx`
  ودخولاً بنطاق `write:packages`؛ الناتج `artifacts/vps/images-<tag>.json`، و`-Tag <sha12> -SkipBuild` يكمل دفعاً انقطع). لم
  ينجح في PLAN-36: اتصال المالك يرفع بنحو 0.16 MB/s والصور نحو 810 MB من الطبقات الفريدة. جهاز المالك الحالي بلا `buildx`.
- **البناء على الخادم (PLAN-36 إلى PLAN-57):** `scripts/vps/build-on-host.ps1` يرفع أرشيف المصدر (~30 MB)،
  و`deploy/vps/build-on-host.sh` يبني الصور على الخادم ويدفعها إلى سجل حلقي عليه (`127.0.0.1:5050`، P-10) ثم يحذف المصدر
  وذاكرة البناء وصورة SDK. تُرك لأن البناء يأخذ نحو 28 GB من قرص الخادم في ذروته ونصف ساعة، ولأن السجل الحلقي كان يحتفظ
  بصور كل إصدار (53.7 GB بعد 33 إصداراً). العودة إليه عند الحاجة:
  `ssh hrm-vps "sudo bash /opt/hrm/current/deploy/vps/host-setup.sh --with-registry"` ثم `build-on-host.ps1`، مع
  `HRM_IMAGE_REPO=127.0.0.1:5050/hrmanagement` في `.env`، وبعد قراءة `du -sm /opt/hrm/*` و`docker system df -v`.

## 5. توليد `.env` على الخادم

يُنفَّذ **على الخادم** كمستخدم `hrm`. كل سر يُولَّد بـ `openssl rand` ويُكتب دون طباعة؛ لا تشغّل `cat` على الملف.

```bash
set -euo pipefail
umask 077
f=/opt/hrm/shared/.env
cp /opt/hrm/releases/<tag>/deploy/vps/.env.vps.example "$f"
setkey() { K="$1" V="$2" awk -F= 'BEGIN{OFS="="} $1==ENVIRON["K"]{print ENVIRON["K"] "=" ENVIRON["V"]; next} {print}' "$f" > "$f.tmp" && mv "$f.tmp" "$f"; }
for k in POSTGRES_PASSWORD RABBITMQ_DEFAULT_PASS KEYCLOAK_ADMIN_PASSWORD KEYCLOAK_HR_GATEWAY_CLIENT_SECRET \
         KEYCLOAK_DIRECTORY_SYNC_SECRET HR_CORE_OTP_HMAC_KEY GATEWAY_CALLER_PROOF_SECRET \
         SVC_HR_CORE_CLIENT_SECRET SVC_HR_CONTRACTS_CLIENT_SECRET SVC_HR_HEALTH_CLIENT_SECRET SVC_HR_MOVEMENTS_CLIENT_SECRET \
         SVC_HR_TRAINING_CLIENT_SECRET SVC_PAYROLL_CLIENT_SECRET SVC_IDCARD_CLIENT_SECRET SVC_RECORDS_ARCHIVE_CLIENT_SECRET \
         SVC_ADMIN_LOGISTICS_CLIENT_SECRET SVC_IT_INFRA_CLIENT_SECRET SVC_DEV_ANALYTICS_CLIENT_SECRET SVC_COMPLIANCE_CLIENT_SECRET \
         SVC_AUTH_DIRECTORY_CLIENT_SECRET; do
  setkey "$k" "$(openssl rand -hex 24)"
done
setkey DEMO_USERS_PASSWORD "$(openssl rand -hex 12)"
setkey APP_DOMAIN hr.161-97-185-71.sslip.io
setkey SSO_DOMAIN sso.161-97-185-71.sslip.io
setkey HRM_IMAGE_REPO ghcr.io/<owner>/hrmanagement
chmod 600 "$f"
cut -d= -f1 "$f" | grep -E '^[A-Z]'     # تحقّق بأسماء المفاتيح فقط
```

`deploy.ps1` و`remote-deploy.sh check-env` يرفضان النشر إن غاب مفتاح من `.env.vps.example`، أو فرغت قيمة سر، أو قصر
`DEMO_USERS_PASSWORD` عن 16 حرفاً أو `HR_CORE_OTP_HMAC_KEY` عن 32 — دون طباعة أي قيمة.
منذ PLAN-38 يحلّ المولّد الملتزم `deploy/vps/init-secrets.sh` محل هذه الخطوة اليدوية (§12.2)؛ تبقى هنا للتاريخ.
ومنذ PLAN-42 حلّ زوج مفاتيح إثبات البوابة محل `GATEWAY_CALLER_PROOF_SECRET` (§15).

**إضافة مفتاح لخادم قائم (PLAN-37):** كل مفتاح `SVC_*` في `deploy/wiring/service-wiring.json` (حقل `secretEnv`) مطلوب. لإضافة
الناقص فقط، على الخادم ودون طباعة:

```bash
f=/opt/hrm/shared/.env
for k in SVC_AUTH_DIRECTORY_CLIENT_SECRET; do
  grep -q "^$k=" "$f" || printf '%s=%s\n' "$k" "$(openssl rand -hex 24)" >> "$f"
done
chmod 600 "$f"; cut -d= -f1 "$f" | grep -E '^SVC_'
```

## 5أ. عملاء الخدمات في Keycloak (PLAN-37، ثم PLAN-39)

realm الخادم لا يُحذف ولا يُعاد استيراده (المستخدمون التجريبيون والبيانات يعتمدون عليه). منذ PLAN-39 تجعله الخدمة لمرة واحدة
`keycloak-reconcile` ([deploy/keycloak/reconcile.sh](../deploy/keycloak/reconcile.sh)، من صورة Keycloak نفسها) مطابقاً للـ realm
المولَّد من القائمة والمصفوفة في **كل نشر**، بعد أن يصبح `keycloak` سليماً وقبل أي تطبيق (كل التطبيقات تنتظرها:
`service_completed_successfully`). تقرأ `reconcile-plan.tsv` الذي يكتبه `render-realm.ps1` بجانب الـ realm (أسماء وأعلام
وعناوين وأسماء متغيرات الأسرار، بلا قيمة)، وتطابق: حقول الأمان المسماة، والنطاقات الثمانية بمُعيّنات الجمهور بالضبط، وكل
عميل (عملاء `svc-*` والبوابة و`directory-sync`): وجوده وأعلامه ونطاقاته الاختيارية وعناوين البوابة وسرّه من متغيره. **لا تمس
المستخدمين ولا ربط الأدوار ولا تحذف عميلاً.** كلمة مرور المدير والأسرار من بيئة الحاوية، وتصل إلى kcadm عبر stdin أو ملف 600،
ولا تُطبع؛ المخرجات أسماء والإجراء و`reconcile: N changes`، والتشغيل الثاني `reconcile: 0 changes`.

- فشلها يمنع بدء التطبيقات فيفشل `up` والنشر (`failed-tag` ثم `-Rollback`). `remote-deploy.sh` يطبع سجلها و`timing keycloak_reconcile_seconds`.
- يدوياً (تشغيل ثانٍ): `ssh hrm-vps "docker start -a $(docker ps -aq -f label=com.docker.compose.project=hrm -f label=com.docker.compose.service=keycloak-reconcile)"`.
- التدوير: تعديل `SVC_X_CLIENT_SECRET` في `.env` ثم `deploy.ps1 -Tag <الوسم الحالي>` (القديم يُرفض `unauthorized_client`).
- `keycloak-sync-clients.sh` (PLAN-37) حُذف؛ لا يشغّله `remote-deploy.sh` إلا لإصدار أقدم من PLAN-39 عند `-Rollback` إليه.

## 6. النشر والتراجع

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/vps/deploy.ps1 -Tag <sha12> [-AllowStagingCa]
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/vps/deploy.ps1 -Rollback
```

`deploy.ps1`: يقرأ `APP_DOMAIN` من الخادم ويولّد realm العرض له (عناصر نائبة فقط)، ويبني الحزمة
(`make-deploy-bundle.ps1`: بلا `.env`، ويفحصها gitleaks)، وينسخها بـ `scp`، ويشغّل `remote-deploy.sh check-env` ثم
`deploy <tag>` (`pull` ثم `up -d --remove-orphans` ثم انتظار `healthy` حتى 10 دقائق)، ثم `smoke.ps1`. الأزمنة في
`artifacts/vps/deploy-<host>-<action>-<tag>.json`.

**الصور من GHCR (§4)، والتراجع كذلك.** `-Tag <sha12>` يلزمه أن تكون صور الوسم السبع عشرة على GHCR (ابنِها أولاً:
`gh workflow run deploy.yml --ref <branch> -f action=build`). `-Rollback` يعيد نشر الوسم المسجَّل في `/opt/hrm/previous-tag`
من مجلده الباقي في `/opt/hrm/releases/` بالطريق نفسه: `pull` من `HRM_IMAGE_REPO` ثم `up`. GHCR يحتفظ بكل وسم دُفع إليه (لا
سياسة حذف)، فكل إصدار بُني هناك يبقى هدفاً للتراجع. إن غاب وسم عن GHCR فشل `pull` وتوقف `remote-deploy.sh` قبل أن يمس
الحاويات. وإصدار أقدم لم يُبنَ هناك يُبنى بـ`-f source_sha=<SHA الكامل>` (§4) ثم يُنشر بـ`-Tag`.

الاختبار الذاتي وحده: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/vps/smoke.ps1 [-AllowStagingCa]`.
منذ PLAN-37 فحوص التوصيل **إلزامية** (`deploy/vps/smoke.sh` §8، من `deploy/wiring/service-wiring.json` في حزمة الإصدار):
عناوين كل خدمة وعميلها ونطاقاتها داخل الحاوية، وطول السر في الحاوية و`.env`، وتوكن لكل عميل بكل نطاقاته (يبقى داخل
الحاوية)، ونداء S2S حقيقي (HR.Core ← File.Storage service-upload بإثبات موقّع: 404 لملف غير موجود = مصرَّح)، وبصمة SHA-256
لحزمة `syr003-service-regimes-v1` في الصور الثلاث مقارنةً بـ `docs/syr/service-regimes.json` في الحزمة. أي نقص يُفشل الاختبار،
و`Deferred(PLAN-*)` يُعدّ فشلاً.
`-AllowStagingCa` يقبل شهادة staging من Let's Encrypt **فقط**، لأول تشغيل.

فحص المنافذ من جهاز المالك قد يكون غير حاسم: إن كان في طريق الاتصال VPN أو وكيل ينهي TCP بنفسه فكل منفذ يبدو مفتوحاً.
لذلك يجرّب `smoke.ps1` أولاً منفذاً لا يستمع عليه شيء أبداً (23456)؛ فإن «اتصل» صار الفحص الخارجي تحذيراً (Warn)، والحكم
لفحص الخادم نفسه: `ss` لا يُظهر غير 22 و443 على العناوين العامة، و`ufw` يرفض الباقي (التقاط tcpdump على الخادم أكد أن
الطلب يصل ولا يُرد عليه).

الاسم المؤسسي: `scripts/deploy-organization.ps1 -OrganizationName "<الاسم>"` يضبط `HRM_ORGANIZATION_NAME` على الخادم ثم
ينشر. الإطار القانوني يُفعَّل من الرواتب ← الأطر القانونية.

## 7. الأنفاق

```bash
ssh -N -L 5380:127.0.0.1:5380 hrm-vps      # Seq: http://localhost:5380
```

لوحة Keycloak الإدارية محجوبة من الإنترنت (`/admin`، `/realms/master` ← 403). للوصول المؤقت:

```bash
ssh hrm-vps "docker inspect -f '{{range .NetworkSettings.Networks}}{{.IPAddress}} {{end}}' \$(docker ps -qf label=com.docker.compose.service=keycloak)"
ssh -N -L 8088:<keycloak-ip>:8080 hrm-vps   # ثم http://localhost:8088/admin (حساب KEYCLOAK_ADMIN)
```

أو اسمح لعنوانك مؤقتاً: `ADMIN_ALLOWED_CIDRS=<ip>/32` في `.env` ثم أعد إنشاء الحافة.

## 8. تغيير الاسم من sslip.io إلى نطاق حقيقي (P-9)

Keycloak يستورد realm العرض في أول إقلاع فقط، فلا يكفي تعديل `.env`:

1. أضف سجلّي A للنطاق الجديد، وحدّث `APP_DOMAIN` و`SSO_DOMAIN` في `/opt/hrm/shared/.env`.
2. حدّث عميل البوابة في Keycloak:
   ```bash
   kc=$(docker ps -qf label=com.docker.compose.service=keycloak)
   docker exec -it $kc /opt/keycloak/bin/kcadm.sh config credentials --server http://localhost:8080 --realm master --user <KEYCLOAK_ADMIN>
   id=$(docker exec $kc /opt/keycloak/bin/kcadm.sh get clients -r hrmanagement -q clientId=hr-gateway-client --fields id --format csv --noquotes)
   docker exec $kc /opt/keycloak/bin/kcadm.sh update clients/$id -r hrmanagement \
     -s 'redirectUris=["https://<APP_DOMAIN>/signin-oidc"]' -s 'webOrigins=["https://<APP_DOMAIN>"]' \
     -s 'attributes."post.logout.redirect.uris"=https://<APP_DOMAIN>/signout-callback-oidc'
   ```
   أو، لأن بياناته اصطناعية: احذف realm `hrmanagement` من اللوحة ثم أعد إنشاء Keycloak ليُعاد الاستيراد.
3. أعد النشر: `scripts/vps/deploy.ps1 -Tag <sha12>` (يولّد realm بالاسم الجديد للمرات القادمة).

## 9. ختم الرقابة التشريعية

`LegalControlGuard` يوقف الإقلاع في Production بعد `nextDue` في `LegalControl/legislative-watch-stamp.json`
(اليوم: 2027-01-31). الاختبار الذاتي يحذّر قبل 30 يوماً. التجديد إجراء حوكمة (مراجعة التشريعات ثم تحديث الختم في
المستودع وإصدار جديد)، ولا يُعطَّل الحارس.

## 10. تغييرات الخادم وطرق التراجع عنها

| التغيير | التراجع |
|---------|---------|
| Docker Engine + Compose (`/etc/apt/sources.list.d/docker.list`) | `apt-get purge docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin` ثم حذف ملف المستودع والمفتاح |
| `/etc/docker/daemon.json` | استعادة `daemon.json.bak-<ts>` أو حذفه، ثم `systemctl restart docker` |
| `/swapfile` و`/etc/fstab` و`/etc/sysctl.d/90-hrm-swappiness.conf` | `swapoff /swapfile && rm /swapfile`، وحذف السطر والملف |
| `ufw` مفعّل (22، 443) | `ufw disable` |
| `/etc/apt/apt.conf.d/52-hrm-unattended` | حذف الملف |
| المنطقة الزمنية UTC | `timedatectl set-timezone Europe/Berlin` (القيمة الأصلية) |
| المستخدم `hrm` و`/etc/sudoers.d/90-hrm` | `userdel -r hrm && rm /etc/sudoers.d/90-hrm` |
| `/opt/hrm` | `rm -rf /opt/hrm` (يحذف `.env` والإصدارات) |
| تشديد SSH (`00-hrm-hardening.conf`، ونقل `50-cloud-init.conf` إلى `/root/sshd-backup-<ts>/`) | `sudo bash /opt/hrm/current/deploy/vps/host-setup.sh --undo-harden-ssh` (أو خطوات VNC أعلاه) |
| السجل الحلقي `hrm-registry` (127.0.0.1:5050، `/opt/hrm/registry`) — **أُزيل في 2026-11-26** (القسم «صور الإصدارات على GHCR») | لا يُستعاد محتواه. سجل فارغ من جديد: `sudo bash /opt/hrm/current/deploy/vps/host-setup.sh --with-registry` |
| `/opt/hrm/build` (تقارير البناء على الخادم) | `rm -rf /opt/hrm/build` |
| `tcpdump` (لتشخيص فحص المنافذ) | `sudo apt-get purge tcpdump` |
| مشروع compose `hrm` | `docker compose -p hrm ... down` (والـ volumes بـ `-v`) |
| `docker login ghcr.io` على الخادم (من PLAN-36؛ لا يلزم للسحب ما دامت الحزم عامة) | `docker logout ghcr.io` (يحذف `~hrm/.docker/config.json`) |
| PLAN-37: `SVC_AUTH_DIRECTORY_CLIENT_SECRET` في `/opt/hrm/shared/.env` | حذف السطر (`sed -i '/^SVC_AUTH_DIRECTORY_CLIENT_SECRET=/d'`) ثم إعادة النشر |
| PLAN-37: عملاء ونطاقات Keycloak من المصفوفة (`keycloak-sync-clients.sh`) | لوحة Keycloak (§7): حذف العملاء/النطاقات المضافة؛ أو إرجاع الإصدار السابق (لا يحذف ما أُضيف) |
| PLAN-38: 45 سراً ومفتاحا الوضعين أُضيفت إلى `/opt/hrm/shared/.env` (`init-secrets.sh`، 84 مفتاحاً) | استعادة `/opt/hrm/shared/.env.bak-plan38-20261101T163137Z` (600) ثم إعادة النشر |
| PLAN-38: إعادة البناء من volumes فارغة (حُذفت `hrm_postgres_data` و`hrm_file_storage_data` و`hrm_gateway_dataprotection_keys` و14 `hrm_dp_*` وvolume RabbitMQ المجهول) | §12.5: الاستعادة من `/opt/hrm/backups/pre-plan38-20261101T175541Z` |
| PLAN-38: volume جديد `hrm_rabbitmq_data` وvolume النتائج `hrm_bootstrap_results` | `docker volume rm` بعد `down` |
| PLAN-38: `/opt/hrm/backups/` (700) | حذف المجلد بعد التأكد من عدم الحاجة إلى النسخة |
| PLAN-39: 3 مفاتيح في `/opt/hrm/shared/.env` (`POSTGRES_KEYCLOAK_PASSWORD`، `HRM_BOOTSTRAP_ADMIN_PASSWORD`، `HRM_BOOTSTRAP_ADMIN_USER`؛ 87 مفتاحاً) | استعادة `/opt/hrm/shared/.env.bak-plan39-20261102T014535Z` (600) ثم إعادة النشر |
| PLAN-39: الدور `keycloak` في PostgreSQL، ونقل ملكية كل كائنات `keycloak_db` (285 علاقة) والقاعدة نفسها إليه | الإصدار السابق يتصل بالحساب الإداري فيعمل دون نقل عكسي؛ للرجوع الكامل: §12.5 بنسخة `/opt/hrm/backups/pre-plan39-20261102T014546Z` ثم `-Rollback` |
| PLAN-39: إعدادات الأمان في realm `hrmanagement` (سياسة كلمات المرور، التخمين 5/900 ث) و`fullScopeAllowed=false` لثمانية عملاء `svc-*` (`keycloak-reconcile`) | لوحة Keycloak (§7)، أو `-Rollback` (لا يعيد الحقول) |
| PLAN-39: volume `hrm_keycloak_import` (realm مولّد بنواب `${NAME}` وخطة المطابقة، بلا أسرار) | `docker volume rm hrm_keycloak_import` بعد `down` |
| PLAN-39: الإصدار `817304f5bd88` (current-tag؛ previous-tag `7bd85bc3ac9d`) وصوره في السجل الحلقي | `deploy.ps1 -Rollback` |
| PLAN-39: `/opt/hrm/backups/pre-plan39-20261102T014546Z` (700، الملفات 600) | حذف المجلد بعد التأكد من عدم الحاجة إلى النسخة |
| PLAN-40: الإصدار `3d0b2df84f82` (current-tag؛ previous-tag `f44e52cf58be`)، وترحيلات `MSG040_*`: outbox في admin وauth_directory وit_infra وarchive وanalytics، وإيصالات Training، وفهرس `InboxState` | `deploy.ps1 -Tag 817304f5bd88` آمن: الإصدار القديم يتجاهل الجداول الجديدة. `-Rollback` يعود إلى `f44e52cf58be` (طلبات الإعادة فيه بلا رمز antiforgery) |
| PLAN-40: `HRM_OTEL_ENABLED=true` في `.env` (يلزم لـ `/metrics` داخل الشبكة؛ مغلق على الحافة والبوابة) | إعادة القيمة `false` ثم `deploy.ps1 -Tag <الوسم الحالي>` |
| PLAN-40: `/opt/hrm/backups/pre-plan40-20261104T014945Z` (700، الملفات 600) و`/opt/hrm/shared/.env.bak-plan40-20261104T014945Z` | حذفهما بعد التأكد من عدم الحاجة إليهما |
| PLAN-42: `GATEWAY_PROOF_PRIVATE_KEY_B64` و`GATEWAY_PROOF_PUBLIC_KEY_B64` في `/opt/hrm/shared/.env` (89 مفتاحاً) | `sed -i '/^GATEWAY_PROOF_/d' /opt/hrm/shared/.env`، وهو آمن مع الوسم القديم فقط |
| PLAN-42: الإصدار `a7ad95e0a4fa` (current-tag؛ previous-tag `3d0b2df84f82`)، وترحيلا `S2S042_*` في `hr_core_db` (`PolicyEnforcementLedgers.CallerClientId` و`CodeIssuances.IssuedByClientId`) | `deploy.ps1 -Rollback`؛ الأعمدة تبقى ولا تضر |
| PLAN-42: نطاق `dsar.subject.erase` لـ `svc-compliance` في realm (`keycloak-reconcile`، 3 تغييرات) | لوحة Keycloak (§7)، ويعيده `keycloak-reconcile` في أي نشر بوسم PLAN-42 |
| PLAN-42: حُذف الطابور `idcard-web-policy-published` (0 رسالة، 0 مستهلك) | لا تراجع لازم: الوسم القديم يعيد إنشاءه عند الإقلاع |
| PLAN-42: `/opt/hrm/backups/pre-plan42-20261105T090239Z` (700، الملفات 600) و`/opt/hrm/shared/.env.bak-plan42-20261105T090239Z` | حذفهما بعد التأكد من عدم الحاجة إليهما |
| PLAN-43: الإصدار `3bd9ee92bcf5` (current-tag؛ previous-tag `c6faf0a92e6e`)، وترحيلا `WF043_*` في `hr_core_db` (أعمدة تقبل الفراغ أو لها قيمة افتراضية) | `deploy.ps1 -Rollback`؛ الأعمدة تبقى ولا تضر الإصدار السابق |
| PLAN-44: الإصدار `973d68488f09` (current-tag؛ previous-tag `3bd9ee92bcf5`)، وترحيل `WF044_WorkflowInstanceIntegrity` في `hr_core_db` (فهرسان فريدان جزئيان، ولا عمود جديد)، ومهمة `WorkflowOutcomeReconciliationJob` كل 15 دقيقة (كشف فقط) | `deploy.ps1 -Rollback`؛ الفهرسان يبقيان، والإصدار السابق يعمل معهما، إلا أن بدءاً ثانياً لكيان جارٍ فيه يتلقى خطأ قيد فريد بدل نسخة ثانية. النسخة الاحتياطية: `/opt/hrm/backups/pre-plan44-20261107T083208Z` |
| PLAN-43: `keycloak-reconcile` أنشأ `workflow-admin` وستة أدوار قديمة كانت غائبة (`hr-core-supervisory`، `hr-core-executive`، `hr-health-clinical`، `payroll-poster`، `payroll-reverser`، `hr-exam-marker`) | لوحة Keycloak (§7)، ولا مستخدم يحملها |
| PLAN-43: حُذف `GATEWAY_CALLER_PROOF_SECRET` من `/opt/hrm/shared/.env` (لا شيفرة تقرؤه منذ `a7ad95e0a4fa`) | إعادة السطر من `env.backup` في نسخة `pre-plan43` |
| PLAN-43: طلب شاغر معتمد واحد باسم «PLAN-43 server check» من فحص الخادم، ورفع عدّاد `RecruitmentRequisition` فوق أرقام البذرة | يبقى، أو يُرفض من الواجهة |
| PLAN-43: `/opt/hrm/backups/pre-plan43-20261106T140127Z` (700، الملفات 600) | حذفه بعد انتهاء الحاجة |
| PLAN-45: الإصدار `9560b03e51a3` (current-tag؛ previous-tag `973d68488f09`)، وترحيل `WF045_AssetApprovalLoop` في `admin_db` (أعمدة مضافة، وجدول `IdempotencyRecords`، وفهرس على `AuditLogs`، وفهرس فقدان موسَّع) | `deploy.ps1 -Rollback`. الأعمدة تبقى ويتجاهلها الوسم السابق |
| PLAN-45: الإصداران 2 من `ASSET-ASSIGN-APPROVAL` و`ASSET-LOSS-APPROVAL`، والإصداران 1 مسحوبان، ويأتيان من `EnsureCustodyChainAsync` عند الإقلاع | لا تراجع بالوسم: الإصدار 2 يبقى منشوراً بعد `-Rollback`، فالطلب الجديد يبقى عند الخطوة 1 حتى العودة إلى هذا الوسم. لا يتعطل شيء ولا تُفقد بيانات |
| PLAN-45: المستخدمان `e2e-logistics-maker` و`e2e-logistics-checker` في الـ realm (`admin-logistics`، و`tenant_id` للجهة الوطنية، وكلمة مرور العرض) عبر `scripts/vps/add-demo-user.sh` | لوحة Keycloak: حذف المستخدمين |
| PLAN-45: ثلاثة أصول باسم «PLAN-45 server check» لكل تشغيل لفحص الخادم (محجوز، ومفقود، ومتاح) | تُترك، أو تُلغى من شاشة الأصل |
| PLAN-45: `/opt/hrm/backups/pre-plan45-20261107T155048Z` (700، الملفات 600) | حذفه بعد انتهاء الحاجة |
| PLAN-46: الإصدار `dbf53280121d` (current-tag؛ previous-tag `9560b03e51a3`)، وترحيلا `WF046_CardRequestApproval` في `idcard_db` و`WF046_DisciplinaryApproval` في `compliance_db` (أعمدة الاعتماد؛ الإجراءات القائمة معتمدة `ApprovedBeforeEngine` بقيمة العمود الافتراضية) | `scripts/vps/deploy.ps1 -Rollback`؛ الأعمدة تبقى ولا يقرؤها الإصدار السابق |
| PLAN-46: الإصداران 2 من `ID-CARD-ISSUANCE` و`DISCIPLINARY-APPROVAL`، والإصداران 1 مسحوبان، وسُحبت `HIRE-WIZARD` و`TRANSFER-APPROVAL` و`IT-TICKET` و`SEPARATION-WIZARD`، كلها من البذرة عند الإقلاع | لا تراجع: الإصدار السابق لا يعيد نشرها |
| PLAN-46: المستخدمون `e2e-idcard-maker` و`e2e-idcard-checker` (`idcard`) و`e2e-compliance-checker` (`compliance`) في الـ realm، للجهة الوطنية وبكلمة مرور العرض، عبر `scripts/vps/add-demo-user.sh` | حذفهم من Keycloak |
| PLAN-46: من فحص الخادم لكل تشغيل: طلبا بطاقة (معالج، ومسحوب إلى مسودة) وبطاقة نشطة، وإنذار معتمد ملاحظته «PLAN-46 server check» وسجله في الرواتب | تُترك، أو تُلغى من الشاشات |
| PLAN-46: حُذفت صور قديمة غير مستعملة (`docker image prune -af --filter until=12h`) وذاكرة البناء، لأن القرص امتلأ أثناء البناء (95%) | لا يلزم: كل إصدار باقٍ في السجل المحلي `127.0.0.1:5050` |
| PLAN-46: `/opt/hrm/backups/pre-plan46-20261107T224439Z` (700، الملفات 600) | حذفه بعد انتهاء الحاجة |
| PLAN-47: الإصدار `ae95ec2fec82` (current-tag؛ previous-tag `dbf53280121d`)، وترحيل `POL047_PolicyRuleLifecycle` في `hr_core_db` (جدولان، وأعمدة تقبل NULL أو بقيم افتراضية، وفهرس المسودة الواحدة، و`xmin`، والـ triggers) | `scripts/vps/deploy.ps1 -Rollback`، ثم إسقاط الـ triggers بالـ SQL في قسم PLAN-47 إن لم يُقلع الإصدار السابق |
| PLAN-47: الدوران `policy-author` و`policy-approver` في الـ realm (`keycloak-reconcile`)، والمستخدم `e2e-policy-author` (`policy-author`، الجهة الوطنية، كلمة مرور العرض) عبر `scripts/vps/add-demo-user.sh` | حذف المستخدم والدورين من Keycloak |
| PLAN-47: ربطان غير مثبتين لـ `Core.Recruitment` (`RECRUITMENT-SELECTION-COMMITTEE`، `RECRUITMENT-APPEAL-REVIEW`) وسجلات `PolicySeedApplications` (كلها NoOp على الخادم) من البذرة عند الإقلاع | تُترك |
| PLAN-47: جولة فحص الخادم: الإصداران 2 و3 من `Analytics.ETL` (نسخة من v1، ثم تراجع إليها)، والقواعد كما كانت | تُترك (التراجع إصدار جديد بالتصميم) |
| PLAN-47: `/opt/hrm/backups/pre-plan47-20261108T144522Z` (700، الملفات 600) | حذفه بعد انتهاء الحاجة |
| PLAN-47 بعد الدمج: الإصدار `61816ed7dda2` (current-tag؛ previous-tag `ae95ec2fec82`)، فيه التحقق من رمز antiforgery على عمليات الكتابة الثماني في `api/policies`؛ بلا ترحيل ولا تغيير إعدادات | `scripts/vps/deploy.ps1 -Rollback` (بلا SQL) |
| PLAN-48: الإصدار `8abd5aa7fe93` (current-tag؛ previous-tag `61816ed7dda2`)، وسياسة `national_shared_read` على الجداول الوطنية العشرة (يطبّقها bootstrap)، بلا ترحيل EF | انشر إلى الأمام. وللتراجع: ارفع عدّاد `AssetTag` المشترك بنص SQL في قسم PLAN-48 ثم `scripts/vps/deploy.ps1 -Rollback`؛ والسياسة تبقى ولا تضر الإصدار السابق |
| PLAN-48: `ten048-code-policy-upgrade-v1` في `SeedPackApplications`: v1 من `FundedPosition` و`RecruitmentRequisition` مؤرشفان وv2 منشوران، وعدّاد طلب الشاغر الجديد `{PLATFORM}\|RecruitmentRequisition\|2026` (السنة 0) من 15 | لا تراجع عن الترقية: الإصدار السابق يقرأ v2 بالمفاتيح نفسها (القسم «الإعدادات الوطنية لكل جهة والترقيم») |
| PLAN-48: من فحص الخادم: طلبا الشاغر `REQ-2026-00016` و`REQ-2026-00017` (جولتا PLAN-43 وPLAN-44) | لا شيء |
| PLAN-48: `/opt/hrm/backups/pre-plan48-20261109T194711Z` (700، الملفات 600) | حذفه بعد انتهاء الحاجة |
| PLAN-49: الإصدار `046316bde9e7` (current-tag؛ previous-tag `8abd5aa7fe93`)، والترحيل `POL049_PolicyDomainRetirement` في `hr_core_db` (عمودان يقبلان NULL في `PolicyTemplates`، بلا تغيير بيانات)، والمجالات الخمسة المحكومة بوضع Shadow في ملفات Production للخدمات الأربع، وزوال `Integration__HrCoreBaseUrl` من `hr-health-web` و`hr-training-web` و`dev-analytics-web` | `scripts/vps/deploy.ps1 -Rollback` (بلا SQL؛ القسم «نطاق محرك السياسات») |
| PLAN-49: مجموعات التغيير الأربع `POL-049-*` في `PolicySeedApplications`: إصدار أخير بلا قواعد لكل قالب من الخمسة عشر خارج النطاق (والقالب معلَّم مسحوباً)، وإصدار جديد لكل من `Assets.Custody` و`IDCard.Lifecycle` و`Contracts.Leave`، ورابط `ESTABLISHMENT-CHANGE-APPROVAL` | لا تراجع عن إصدار منشور: التراجع إصدار جديد. الإصدار السابق يعمل بجانبها (القسم نفسه). ولا تُفتح مسودة في قالب مسحوب |
| PLAN-49: من فحوص الخادم: طلب إجازة ملغى وطلب تجميد وظيفة ملغى مع نسخة اعتماده (موسومة «PLAN-49 server check»)، وصفوف Shadow في `PolicyEnforcementLedgers`، وإصداران إضافيان لـ `Contracts.Leave` من جولة PLAN-47 (نسخة ثم تراجع)، وما تتركه فحوص PLAN-43..46 كعادتها | تُترك |
| PLAN-49: `/opt/hrm/backups/pre-plan49-20261111T004110Z` (700، الملفات 600) | حذفه بعد انتهاء الحاجة |
| PLAN-50: الإصدار `a504f6e54921` (current-tag؛ previous-tag `046316bde9e7`)، والترحيلان `HCM050_ApprovalDelegationScope` في `hr_core_db` (حُذف `WorkflowDelegations` الفارغ، وأعمدة جديدة وقيدان في `ApprovalDelegations`، ولم يُلغَ صف إذ لا صف) و`MOV050_ApprovalOnBehalfOf` في `hr_movements_db` (عمودان يقبلان NULL) | `deploy.ps1 -Rollback`، بلا SQL وبلا استعادة (ملاحظة التراجع في قسم PLAN-50) |
| PLAN-50: المستخدم `e2e-delegate-hr` في الـ realm (`hr-core`، `offline_access`، `tenant_id` للجهة الوطنية، `employee_id` = `98655dae-f87e-4d46-9703-a17c0b5cc09a`، وكلمة مرور العرض) عبر `scripts/vps/add-demo-user.sh --employee-id` | حذف المستخدم من لوحة Keycloak (§7) |
| PLAN-50: من فحوص الخادم لكل تشغيل: نقلان ملغيان (أحدهما معتمد بالنيابة)، وطلب شاغر معتمد على وظيفة شاغرة (الخطوة 2 بالنيابة)، وتفويضان ملغيان (فحص الحركات وفحص المحرك)، كلها موسومة «PLAN-50 server check» | تُترك |
| PLAN-50: `/opt/hrm/backups/pre-plan50-20261111T161806Z` (700، الملفات 600) | حذفه بعد انتهاء الحاجة |
| PLAN-52: الإصدار `92a69960c826` (current-tag؛ previous-tag `d0e446fe3280`)، والترحيل `HCM052_OrgScopeKeys` في `hr_core_db` (عمود `OrgUnitId` يقبل NULL بفهرس ومفتاح أجنبي على `DiwanCorrespondences` و`FieldInspectionVisits`، وختم `ScopeOrgUnitId` لنسخ طلبات الشاغر الاثنتين والعشرين) طبّقه `hr_core_migrator` | `scripts/vps/deploy.ps1 -Rollback` بلا SQL (انظر «نطاق الفرع» أدناه) |
| PLAN-52: المستخدم `e2e-branch-manager` في الـ realm (`branch-manager` و`hr-core` و`admin-logistics` و`offline_access`، `tenant_id` للجهة الوطنية، `scope_org_unit_id` = قسم الموارد البشرية `11111111-1111-1111-1111-111111111102`، وكلمة مرور العرض) عبر `scripts/vps/add-demo-user.sh --scope-org-unit-id` | حذف المستخدم من لوحة Keycloak (§7)، أو `kcadm.sh delete users/<id> -r hrmanagement` |
| PLAN-52: `/opt/hrm/backups/pre-plan52-20261113T064822Z` (700، الملفات 600) | حذفه بعد انتهاء الحاجة |
| PLAN-67: الإصدار `1ee455bf1a6f` (current-tag؛ previous-tag `92a69960c826`)، والترحيل `UX067_UserNameRegistry` في `gateway_audit_db` (جدول `gateway_user_names` جديد، طبّقه `gateway_audit_migrator`) | `scripts/vps/deploy.ps1 -Rollback`، بلا SQL (ملاحظة التراجع في قسم PLAN-67) |
| PLAN-67: أُغلق طلب الشاغر `REQ-2026-00004` (`23b42bbe-…`) بإجراء «إغلاق» كـ `hradmin`، فأُخفي إعلانه العام `b4e13f41-…` («PLAN-43 server check») | لا إجراء إعادة فتح في التطبيق: الاستعادة من `pre-plan67-20261115T063540Z`، أو إعادة `Status` إلى `Published` و`ClosedDate` إلى NULL و`IsPublished` إلى true |
| PLAN-67: حُذفت ذاكرة البناء (13.3 GB) والصور المحلية لسبعة إصدارات قديمة (`dbf53280121d` و`ae95ec2fec82` و`a504f6e54921` و`9560b03e51a3` و`8abd5aa7fe93` و`61816ed7dda2` و`046316bde9e7`)، وهي باقية في السجل الحلقي | `docker pull 127.0.0.1:5050/hrmanagement/<image>:<tag>` |
| PLAN-67: `/opt/hrm/backups/pre-plan67-20261115T063540Z` (700، الملفات 600) | حذفه بعد انتهاء الحاجة |
| PLAN-53: الإصدار `5aa61b78e3a6` (current-tag؛ previous-tag `27cbf35db9cc`، وقبلهما `0640c2ac6db0` ثم `1ee455bf1a6f`)، والترحيل `PAY053_SalaryChangeRequests` في `payroll_db` (جدولان جديدان بأمان الصفوف، وعلاقتا السلّم←الدرجات والدرجة←ملفات الأجر `RESTRICT`؛ لا تغيير في البيانات) | `scripts/vps/deploy.ps1 -Tag 1ee455bf1a6f` للعودة إلى ما قبل الخطة (ملاحظة التراجع في قسم PLAN-53)؛ الجدولان والقيدان يبقيان ولا يضران الإصدار السابق |
| PLAN-53: من فحوص الخادم: أربعة طلبات تغيير راتب `Cancelled` (مرجع المصدر `PLAN-53 server check`، لشهر 2026-12) وثلاثة طلبات بيانات مرجعية `Rejected`، مع صفوف تدقيقها؛ لا ملف أجر ولا درجة تغيّرت (425 ملفاً فعّالاً وبصمتها كما قبل) | لا تراجع: الطلبات سجل لا يُحذف، ولا شيء منها مفتوح |
| PLAN-53: حُذفت الصور المحلية لإصدارات `d0e446fe3280` و`92a69960c826` و`0640c2ac6db0` (51 وسماً؛ تبقى في السجل `127.0.0.1:5050`) | `docker pull 127.0.0.1:5050/hrmanagement/<image>:<tag>` |
| PLAN-53: `/opt/hrm/backups/pre-plan53-20261122T224310Z` (700، الملفات 600) | حذفه بعد انتهاء الحاجة |

## 11. التسليم للمالك

- الرابط: `https://hr.161-97-185-71.sslip.io/` (Keycloak: `https://sso.161-97-185-71.sslip.io/`)، بشهادة Let's
  Encrypt حقيقية (production، تُجدَّد تلقائياً داخل Caddy).
- كلمة مرور المستخدمين التجريبيين يقرؤها **المالك وحده**:
  `ssh hrm-vps "grep ^DEMO_USERS_PASSWORD= /opt/hrm/shared/.env"`
  المستخدمون: `hradmin` (مدير الموارد البشرية) ومستخدمو `e2e-*` في realm العرض، كلهم بهذه الكلمة.
- Seq ولوحة Keycloak: §7. النشر والتراجع: §6.
- **عطّل VNC في لوحة Contabo** حين لا تحتاجه.


## 12. الإقلاع الأول والأسرار والوضعان (PLAN-38)

### 12.1 ما يفعله `hrm-bootstrap` عند كل نشر

حاوية لمرة واحدة (صورة `hr-migrate`، [deploy/bootstrap/bootstrap.sh](../deploy/bootstrap/bootstrap.sh)) تعمل عند كل
`docker compose up` قبل أي تطبيق (`depends_on: condition: service_completed_successfully`)، وكل خطوة قابلة للإعادة:
القواعد الـ16، وأدوار `<svc>_app`/`<svc>_migrator` بكلمة مستقلة لكل منها (تُضبط في كل تشغيل)، والترحيلات بدور
`<svc>_migrator` ثم TEN-002C، ودور النسخ `hrm_backup` (قراءة فقط)، ومستخدمو RabbitMQ `mq_*`. التشغيل الثاني يطبع
`0 migrations applied` لكل قاعدة. أي فشل يوقف النشر ولا يبدأ أي تطبيق. `remote-deploy.sh` يطبع سجلها وملف النتيجة
(أسماء وأعداد، بلا أسرار) و`timing bootstrap_seconds`.

### 12.2 المفاتيح الجديدة في `.env` (init-secrets)

لا يُولَّد سر يدوياً. من مجلد إصدار على الخادم:

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/vps/deploy.ps1 -Tag <sha12> -PrepareOnly   # من جهاز المالك
ssh hrm-vps "cd /opt/hrm/releases/<sha12> && bash deploy/vps/init-secrets.sh"                         # المفاتيح الناقصة فقط
```

يضيف كل مفتاح ناقص من `${VAR:?}` في السلسلة و`.env.vps.example` ومصفوفتي الأدوار والوسيط: الأسرار بـ `openssl rand`
(32 بايت base64url)، وغير الأسرار من القيمة الافتراضية في المثال، و`APP_DOMAIN` وأمثاله تُعطى بـ `--set KEY=VALUE` (مرفوض
لأسماء الأسرار). لا يكتب فوق قيمة، ويطبع الأسماء فقط، ويبقي الملف 600.

### 12.3 التدوير

تعديل السطر في `/opt/hrm/shared/.env` (أو حذفه ثم `init-secrets.sh` لتوليد قيمة جديدة) ثم `deploy.ps1 -Tag <الوسم الحالي>`.
`hrm-bootstrap` يضع كلمات مرور أدوار PostgreSQL ومستخدمي RabbitMQ، و`keycloak-sync-clients.sh` أسرار عملاء `SVC_*`، ثم
تُعاد التطبيقات بالقيمة الجديدة؛ القيمة القديمة تُرفض فوراً (AC-OPS-38b).

### 12.4 الوضعان

| المفتاح | القيم | الأثر |
|--------|-------|-------|
| `HRM_MQ_POSTURE` (D-38-2) | `least-privilege` (الافتراضي) / `admin` | `admin` يضيف `docker-compose.posture-mq-admin.yml` (الحساب الإداري للتطبيقات، للرجوع فقط) |
| `HRM_DB_POSTURE` (D-38-1) | `demo` (الافتراضي مع `HRM_DEPLOYMENT_MODE=demo`) / `least-privilege` | `least-privilege` مع وضع العرض مرفوض حتى الموجة 2 (OPS-D)؛ مثبت على مكدّس `hrm-leastpriv` |

حارس الإنتاج يرفض مستخدم وسيط غير `mq_*` مع `Deployment:MqPosture=least-privilege`، ومستخدم قاعدة غير `*_app` مع
`Deployment:DbPosture=least-privilege`.

### 12.5 إعادة بناء خادم العرض من volumes فارغة (S-1، 2026-11-01)

ما جرى بالترتيب، وكل خطوة متحقَّق منها قبل التالية:

1. `init-secrets.sh` على `.env` القائم: أُضيف 45 سراً ومفتاحا الوضعين (لم تُمسّ قيمة قائمة؛ نسخة الملف قبلها:
   `/opt/hrm/shared/.env.bak-plan38-20261101T163137Z`).
2. بناء الوسم `7bd85bc3ac9d` على الخادم (17 صورة، منها `hr-migrate` و`hr-rabbitmq`) ودفعه إلى السجل الحلقي.
3. الوسيط فارغ (0 رسالة في 120 طابوراً) ثم نسخة كاملة متحقَّق منها في
   `/opt/hrm/backups/pre-plan38-20261101T175541Z` (700، الملفات 600): `pg_dumpall.sql` (16 قاعدة كلها حاضرة) وأرشيف
   tar غير فارغ لكل volume حُذف ولـ Seq، و`SHA256SUMS`.
4. `down` ثم حذف volumes البيانات (الجدول في §10)، مع إبقاء Caddy (الشهادات) وSeq وClamAV وصندوقي الإرسال.
5. `deploy.ps1 -Tag 7bd85bc3ac9d`: `hrm-bootstrap` أنهى في 48 ث (264 ترحيلاً بأدوار `*_migrator`، و14 مستخدم `mq_*`)،
   وكل الحاويات سليمة بعد 12 ث من `up`، ومزامنة Keycloak 98 ث. التوقف 5 د 53 ث (17:56:40Z–18:02:33Z، من `down` إلى سطر `healthy` في `deployments.log`).
6. الفحص: smoke 48/0، والوسيط لا يرى إلا اتصالات `mq_*` (14)، وملحق الرسائل المؤجلة `3.13.0` مفعّل، والعقدة
   `rabbit@rabbitmq` على `hrm_rabbitmq_data`. الدليل: `acceptance-evidence/AC-OPS-38/vps/`.

**الاستعادة من النسخة** (إن لزم الرجوع إلى بيانات ما قبل إعادة البناء):

```bash
cd /opt/hrm/current && B=/opt/hrm/backups/pre-plan38-20261101T175541Z
(cd "$B" && sha256sum -c SHA256SUMS)
docker compose -p hrm <ملفات السلسلة كما في current> down
docker volume rm hrm_postgres_data hrm_file_storage_data hrm_gateway_dataprotection_keys $(docker volume ls -q | grep '^hrm_dp_')
docker compose -p hrm <ملفات السلسلة> up -d postgresql      # قاعدة فارغة يهيئها 01-init
docker exec -i "$(docker ps -qf label=com.docker.compose.service=postgresql)" sh -c 'psql -U "$POSTGRES_USER" -d postgres' < "$B/pg_dumpall.sql"
for v in file_storage_data gateway_dataprotection_keys $(ls "$B" | sed -n 's/^\(dp_.*\)\.tar\.gz$/\1/p'); do
  docker run --rm -i -v "hrm_$v:/v" alpine:3.20.3 tar -C /v -xzf - < "$B/$v.tar.gz"
done
```

ثم `deploy.ps1 -Tag <الوسم>`: `hrm-bootstrap` يجد القواعد والترحيلات قائمة فيضبط كلمات مرور الأدوار فقط. أخطاء
`already exists` من `pg_dumpall` على الأدوار والقواعد التي أنشأها 01-init متوقعة.

## 13. Keycloak بدور قاعدة خاص، والـ realm كشيفرة، وحلقات المفاتيح (PLAN-39)

### 13.1 دور `keycloak` على خادم جديد

`hrm-bootstrap` (الخطوة 3، [ensure-keycloak-role.sql](../deploy/bootstrap/ensure-keycloak-role.sql)) ينشئ الدور `keycloak`
(`LOGIN`، بلا أي صلاحية إدارية ولا `BYPASSRLS`)، ويضع كلمة مروره من `POSTGRES_KEYCLOAK_PASSWORD` في كل تشغيل، ويمنحه
`CONNECT` على `keycloak_db` وحدها، ويجعله مالكها إن كانت فارغة. و`keycloak` ينتظر `hrm-bootstrap` ثم يتصل بـ
`KC_DB_USERNAME: keycloak`، فينشئ جداوله بنفسه. قاعدة الصيانة `postgres` تبقى بمنحة PostgreSQL الافتراضية لـ PUBLIC كما
لكل أدوار PLAN-38.

### 13.2 خادم قائم: نقل ملكية `keycloak_db` (مرة واحدة، بعد نسخة متحقَّق منها)

قبل PLAN-39 كان Keycloak يتصل بالحساب الإداري، فكل جداول `keycloak_db` ملكه. بدون النقل يتوقف `hrm-bootstrap` برسالة
«run the ownership transfer» ولا يبدأ Keycloak على جداول لا يملكها. الترتيب إلزامي:

```bash
cd /opt/hrm/releases/<sha12>                                   # الإصدار الجديد مستخرَج (deploy.ps1 -PrepareOnly)
bash deploy/vps/init-secrets.sh                                # يضيف POSTGRES_KEYCLOAK_PASSWORD وغيره (الأسماء فقط)
# 1. نسخة متحقَّق منها أولاً (pg_dumpall + حلقات المفاتيح، 700/600، SHA256SUMS؛ §12.5)
# 2. إيقاف Keycloak وكل ما يعتمد عليه (التطبيقات والحافة)، فلا جلسة على keycloak_db
docker compose -p hrm <ملفات السلسلة> stop hr-gateway <الوحدات الـ14> keycloak
# 3. النقل: hrm-bootstrap من الإصدار الجديد مع المفتاح، مرة واحدة
docker compose -p hrm <ملفات السلسلة> run --rm -e HRM_KEYCLOAK_TRANSFER_OWNERSHIP=true hrm-bootstrap
# 4. النشر المعتاد
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/vps/deploy.ps1 -Tag <sha12>     # من جهاز المالك
```

[transfer-keycloak-ownership.sql](../deploy/bootstrap/transfer-keycloak-ownership.sql) يعمل داخل `keycloak_db` وحدها،
كائناً كائناً (`ALTER … OWNER TO keycloak` للجداول والتسلسلات والعروض والدوال والأنواع والمخططات، ثم
`ALTER DATABASE keycloak_db OWNER TO keycloak`)، ولا يستعمل `REASSIGN OWNED` (يرفضه PostgreSQL للمستخدم الإداري الأول، ويتجاوز
القاعدة). يرفض العمل ما دامت جلسة أخرى متصلة بـ `keycloak_db`، ولا يمس الحساب الإداري ولا أي قاعدة أخرى، والتشغيل الثاني
لا يغيّر شيئاً. يطبع عدد الكائنات لكل مالك بعد النقل.

**التراجع:** الإصدار السابق يتصل بالحساب الإداري (superuser) فيقرأ جداول `keycloak` دون نقل عكسي؛ وإن لزم الرجوع الكامل
فالنسخة (§12.5) ثم `deploy.ps1 -Rollback`.

### 13.3 الـ realm: استيراد أول ومطابقة في كل نشر

- `deploy.ps1` يقرأ `APP_DOMAIN` و`HRM_DEPLOYMENT_MODE` من `.env` الخادم (اسم ووضع، لا سر) ويولّد الـ realm بملف التعريف
  `Demo` (وضع العرض) أو `Production`: `scripts/prepare-keycloak-import.ps1` ← `scripts/keycloak/render-realm.ps1`. الحزمة تحمل
  `realm-hrmanagement.json` و`reconcile-plan.tsv`، بنواب `${NAME}` فقط.
- `remote-deploy.sh` يملأ بهما قبل كل `up` (والتراجع منه) volume `hrm_keycloak_import` من الإصدار الذي ينشره، ويبقي نسخة الإصدار (نواب وأسماء فقط)، فلا يطابق إصدار بخطة إصدار آخر؛ ويرفض ملف تعريف لا يوافق `HRM_DEPLOYMENT_MODE` (Demo في وضع العرض، Production خارجه). Keycloak
  يستوردهما بـ `--import-realm` على خادم جديد فقط، و`keycloak-reconcile` يطابق realm قائماً (§5أ).
- ملف Production: مدير bootstrap واحد `HRM_BOOTSTRAP_ADMIN_USER` بكلمة مؤقتة `HRM_BOOTSTRAP_ADMIN_PASSWORD` (يولّدها
  `init-secrets.sh`) ويُطلب منه تغييرها عند أول دخول؛ لا مستخدمين آخرين.

### 13.4 حلقات مفاتيح Data Protection

- كل خدمة في Production تحفظ حلقتها في volume مسمّى (`hrm_dp_<svc>`، والبوابة `hrm_gateway_dataprotection_keys`).
  `DataProtectionStartupGuard` يوقف أي خدمة في Production (أو مع `DataProtection__RequirePersistentKeyRing=true`) قبل أن تلمس
  قاعدتها إن كان `DataProtection__KeyRingPath` فارغاً أو غير قابل للكتابة أو اسم التطبيق غير مضبوط، أو كان المسار على نظام ملفات
  الحاوية نفسه لا على volume (يضيع عند إعادة الإنشاء؛ `DataProtection__AllowUnmountedKeyRing=true` لتثبيت على مضيف بلا حاويات). **k8s:** لا تخزين مشترك
  للحلقات في المسار الأحادي؛ الحارس يوقف أي pod بلا مسار مستمر، وهذا مقصود.
- `EmployeePiiKeyRingCanary` في HR.Core: قبل الترحيلات والبذور يقرأ رقماً وطنياً محمياً واحداً (`LIMIT 1`، لكل جهة إن لزم بسبب
  RLS) ويفكه بالحلقة. إن لم يُفك، أو كانت الحلقة فارغة فوق بيانات محمية، يخرج HR.Core برسالة «حلقة مفاتيح حماية البيانات لا
  تطابق البيانات المخزنة…» تسمّي المسار، ولا يولّد حلقة جديدة. **الطوارئ فقط:** `DataProtection__AllowKeyRingReset=true` في
  ملف compose مؤقت: يُقلع مع سطر `Critical` وعدّاد `dataprotection_key_ring_reset_total`، والأرقام المحمية تبقى غير مقروءة. الحل
  الصحيح استعادة الحلقة من النسخة (`dp_hr_core.tar.gz`).
- **الشهادة (اختيارية، معطلة على خادم العرض، D-39-3؛ موصى بها للتجربة):** `bash deploy/vps/make-dp-cert.sh` يكتب
  `/opt/hrm/secrets/dp-cert.pfx` (RSA 3072، 10 سنوات، 600) ويضيف `DATAPROTECTION_CERTIFICATE_PASSWORD` إلى `.env`. التفعيل بملف
  compose إضافي: ربط الملف للقراءة في كل تطبيق و`DataProtection__CertificatePath` و`DataProtection__CertificatePassword`. المفاتيح
  القديمة غير المشفرة تبقى مقروءة؛ التدوير بشهادة جديدة ونقل القديمة إلى `DataProtection__PreviousCertificatePaths`. ضياع الشهادة
  يعني ضياع كل الحلقات: احفظ نسخة منها ومن كلمتها **خارج الخادم**.
- **تصدير المفاتيح للنسخ:** `read -rs DP_EXPORT_PASSPHRASE; export DP_EXPORT_PASSPHRASE; bash deploy/vps/export-dp-keys.sh --out <مسار يختاره المالك>`
  ينتج أرشيفاً مشفراً (AES-256، PBKDF2) لكل volumes الحلقات (والشهادة إن وُجدت) مع `SHA-256`. ضمه إلى النسخ المجدولة ← الموجة 2 (OPS-E).

### 13.5 نقل خادم العرض في مكانه (S-1، 2026-11-02)

1. بناء `817304f5bd88` على الخادم (1187 ث)، `deploy.ps1 -PrepareOnly`، `init-secrets.sh` (3 مفاتيح؛ نسخة `.env` قبلها
   `/opt/hrm/shared/.env.bak-plan39-20261102T014535Z`).
2. نسخة متحقَّق منها `/opt/hrm/backups/pre-plan39-20261102T014546Z` (700/600): `pg_dumpall.sql` (16 قاعدة حاضرة)، و15 أرشيف
   حلقة كل منها بمفتاح، و`env.backup`، و`SHA256SUMS` (7 ث).
3. إيقاف البوابة والوحدات الـ14 وKeycloak (01:46:13Z)، ثم `run --rm --no-deps -e HRM_KEYCLOAK_TRANSFER_OWNERSHIP=true hrm-bootstrap`
   من الإصدار الجديد (34 ث): قبل النقل `hrm_admin` يملك 92 جدولاً و193 فهرساً والقاعدة؛ بعده `keycloak=285` والقاعدة لـ `keycloak`.
4. `deploy.ps1 -Tag 817304f5bd88` (160 ث: pull 5، `up` 107، bootstrap 33، reconcile 16 بـ 9 تغييرات، healthy 12). الكاشف مرّ
   على البيانات القائمة (500 رقم وطني محمي). التوقف 3 د 10 ث (01:46:13Z → 01:49:23Z). smoke 49/0.

**الاستعادة إن لزم:** §12.5 بالنسخة أعلاه ثم `deploy.ps1 -Rollback` (إلى `7bd85bc3ac9d`).

## 14. نشر الأحداث الذري وإعادة ما فُقد (PLAN-40)

**ما تغيّر.** 13 خدمة على outbox المستهلك، وخمس خدمات اكتسبت جداول outbox بترحيلات يطبقها `hrm-bootstrap` بأدوار `*_migrator`. لا أسماء جديدة في الوسيط. المقياس `outbox_unsaved_messages_total` يجب أن يبقى 0، والقراءة من داخل الحاوية:

```bash
docker exec hrm-payroll-web curl -s http://localhost:8080/metrics | grep '^outbox_unsaved_messages_total'
```

`/metrics` بلا مصادقة لكنه مقصور على الشبكة الداخلية: يُرفض كل طلب وصل عبر البوابة، وCaddy يرد 404 على `/metrics*` و`/careers/metrics*`، و`smoke.sh` يفحصهما.

**إعادة ما فُقد قبل PLAN-40 (D-40-4).** تشغيل يدوي مرة واحدة بعد النشر، بحساب `hr-admin` للجهة، ولكل جهة على حدة. على خادم العرض لا يوجد ما يُعاد: التشغيل التجريبي في 2026-11-04 أعطى 0 ترفيع و0 ندب و0 نتيجة عهدة، و30 وحدة لإعادة المزامنة ([msg040-replay-server-20261104T031223Z.json](../reports/msg040-replay-server-20261104T031223Z.json)).

```bash
# تجريبي (الافتراضي): يسرد ولا يغيّر شيئاً، ويكتب reports/msg040-replay-<utc>.json بالمعرّفات والأعداد فقط
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/msg040-replay-lost-events.ps1 -GatewayUrl https://<APP_DOMAIN> -CookieHeader "<جلسة hr-admin>" -DryRun
# التطبيق: قرار المالك، بعد مراجعة التقرير التجريبي؛ كل تشغيل يُسجَّل في EntityAuditLog بالفاعل والعدد
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/msg040-replay-lost-events.ps1 -GatewayUrl https://<APP_DOMAIN> -CookieHeader "<جلسة hr-admin>" -Apply
```

`password grant` مغلق في Production وDemo، لذلك يأخذ السكربت ترويسة Cookie لجلسة مسجلة، أو `-AccessToken`. التشغيل الثاني بعد `-Apply` يسرد صفراً، والمعرّف الحتمي يمنع التكرار عند المستهلك.

كل طلب POST للإعادة يتطلب رمز antiforgery، ويُرفض بدونه بـ 400 حتى مع جلسة أو token صالحين؛ فالخدمات تقبل الجلسة، وبدون الرمز قد يصل طلب مزوَّر من موقع آخر فيه `dryRun=false`. يجلب السكربت الرمز من `GET /api/admin/messaging/antiforgery` (خلف سياسة الإعادة نفسها) ولا يكتبه في التقرير. من يستدعي الواجهة بغير السكربت يرسل الترويسة `RequestVerificationToken` مع الكوكي، وينتبه إلى أن الخدمة تقيّد مسار الكوكي بـ `/ui/<service>` بينما الإعادة تمر عبر `/api/<service>`.

**ملاحظة نشر.** `keycloak-reconcile` يقرأ من Keycloak بعد إعادة إنشاء PostgreSQL، فقد تفشل قراءته الأولى باتصال مغلق، فتتوقف التطبيقات عند `Created` (حدث مرتين في 2026-11-04، في كل نشر أعاد إنشاء PostgreSQL، وتوقفت التطبيقات 3 إلى 4 دقائق). العلاج إعادة أمر النشر نفسه؛ فهو قابل للتكرار.

## 15. حدود الثقة بين الخدمات (PLAN-42)

**المفاتيح الجديدة.** زوج ECDSA P-256 لإثبات البوابة في `/opt/hrm/shared/.env`:

| المفتاح | من يراه |
|---------|---------|
| `GATEWAY_PROOF_PRIVATE_KEY_B64` | `hr-gateway` وحدها |
| `GATEWAY_PROOF_PUBLIC_KEY_B64` | كل الوحدات، وليس سراً |

القيمتان سطر واحد base64 من DER (PKCS#8 للخاص وSPKI للعام)، لأن `.env` وCompose لا يحملان قيماً متعددة الأسطر بأمان.

**كيف يضيفهما `init-secrets`.** بالخطوات نفسها في §12.2: `deploy.ps1 -PrepareOnly`، ثم `init-secrets.sh` من مجلد الإصدار. يولّد الزوج بـ `openssl` معاً إن غاب المفتاحان، ويطبع بصمة SHA-256 للمفتاح العام ولا يطبع مفتاحاً. وجود أحدهما دون الآخر خطأ، فالزوج يُستبدل كاملاً وباليد.

بصمة المفتاح العام على خادم العرض: `8d190da8c751e76be7d1c8c9b302d65387d1b444ce6047f3c9afa82d27bf6419`.

**التدوير.** احذف المفتاحين من `.env`، وشغّل `init-secrets.sh`، ثم `deploy.ps1 -Tag <الوسم الحالي>`. تُرفض الإثباتات القديمة فوراً، وصلاحية الإثبات أصلاً 60 ثانية.

**القيم المؤقتة.** أي مفتاح قيمته `USE_ENV_OR_SECRET_MANAGER` أو `CHANGE_ME…` أو `__X__` يوقف الإقلاع خارج `Development` و`Testing` و`Acceptance`، ويسمّي الرسالة المفتاح دون قيمته. لذلك يُفحص الإعداد المولَّد بالأسماء قبل كل نشر يضيف مفتاحاً.

**نقطة الدخول في الصور.** صارت `bash` لأن `sh` يُسقط المتغيرات التي في اسمها نقطة، مثل `PolicyEngine__Assets.Custody`. من يضيف وضع سياسة في `.env` يتأكد أن قيمته وصلت إلى الخدمة.

**النسخة الاحتياطية والتراجع.**

- النسخة: `/opt/hrm/backups/pre-plan42-20261105T090239Z` (700، الملفات 600): `pg_dumpall.sql` و`env.backup` و`SHA256SUMS`. فيها 17 سطر `CREATE DATABASE`: القواعد الست عشرة و`hrm_admin`. ونسخة `.env`: `/opt/hrm/shared/.env.bak-plan42-20261105T090239Z`.
- `deploy.ps1 -Rollback` يعيد `3d0b2df84f82`، وهو آمن: ترحيلات `S2S042_*` أعمدة تقبل الفراغ، والإصدار القديم يتجاهلها، و`GATEWAY_CALLER_PROOF_SECRET` ما زال في `.env`.

**أُنجز في PLAN-43 (2026-11-06).** حُذف `GATEWAY_CALLER_PROOF_SECRET` من `.env` الخادم. كان النص قبله: الشيفرة الجديدة تتجاهله خارج `Development`، ويبقى ليعمل التراجع. يُحذف في أول خطة تنشر بعد PLAN-42، حين لا يعود الوسم السابق بحاجة إليه.

**نشر 2026-11-05.** الوسم `a7ad95e0a4fa`: البناء 1236 ث، والنشر 135 ث بمحاولة واحدة، و`keycloak-reconcile` طبّق 3 تغييرات، وحُذف الطابور اليتيم `idcard-web-policy-published` بعد تسجيل 0 رسالة و0 مستهلك. ودامت إعادة إنشاء الحاويات نحو دقيقة و45 ثانية (11:48:06Z إلى 11:49:49Z).

## 16. حوكمة تعريفات المسارات (PLAN-43)

- **الدور:** `workflow-admin` في الـ realm. `keycloak-reconcile` ينشئ الناقص من أدوار `docker/keycloak/realm-manifest.json` ولا يحذف دوراً ولا يغيّر من يحمله. على خادم العرض يكفي `hradmin` و`e2e-hr-admin` (كلاهما `hr-admin`) للنشر بشخصين.
- **تقرير الإقلاع:** `docker logs hrm-hr-core-web | grep 'fails validation'`. ستة تعريفات متوقعة حتى PLAN-46: `ID-CARD-ISSUANCE` و`IT-TICKET` (دور خارج الكتالوج)، و`HIRE-WIZARD` و`SEPARATION-WIZARD` و`TRANSFER-APPROVAL` و`DISCIPLINARY-APPROVAL` (نوع بلا معالج ولا مالك). لا يوقف التقرير الإقلاع.
- **التعبئة عند الإقلاع:** `Status` و`RequireDistinctApprovers` للصفوف القديمة يملؤهما `WorkflowPackSeeder.BackfillGovernanceAsync`، لا الترحيل: الجدول تحت أمن صفوف مفروض، ودور `hr_core_migrator` بلا جهة، فلا يرى صفاً.
- **النسخة:** `/opt/hrm/backups/pre-plan43-20261106T140127Z` (700، الملفات 600): `pg_dumpall.sql` و`env.backup` و`SHA256SUMS`، و17 سطر `CREATE DATABASE`.
- **التراجع:** `deploy.ps1 -Rollback` يعيد `c6faf0a92e6e`، وبعده `a7ad95e0a4fa` آمن أيضاً: أعمدة `WF043_*` تقبل الفراغ أو لها قيمة افتراضية. لكن مستهلكي الإصدار الأقدم (`supportedMax: 1`) يتجاهلون أحداث العهدة v2 الجارية، وسجل النشر يمنع إعادة تشغيلها، فراجع نسخ العهدة المعلقة بعد تراجع كهذا. وما ينشره الوسم القديم يصحح الترقيع `Status` له عند العودة.

**نشر 2026-11-06.** بناءان: `c6faf0a92e6e` (1349 ث) ثم `3bd9ee92bcf5` (1299 ث) بعد أن كشف فحص الخادم أن تعبئة الترحيل لم تمس صفاً. النشر الأول 156 ث. النشر الثاني توقف عند `keycloak-reconcile` بعد إعادة PostgreSQL، خطأ `could not read the realm roles`، فبقي الوسم وتوقفت التطبيقات نحو 4 دقائق، ونجحت إعادة الأمر نفسه في 120 ث. الدخان 50 نجاحاً و0 فشل وتحذير واحد.

## تقرير النسخ المكررة والإصلاح (PLAN-44)

**التقرير** (قراءة فقط، بدور المسؤول لأن الجداول تحت أمن صفوف مفروض):

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/wf044-duplicate-instances-report.ps1 -Server -FailOnDuplicates
```

يطبع المالك وأعلام أمن الصفوف، والنسخ حسب النوع والحالة، والمكررات، والمفاتيح المكررة، وما حلّه الترحيل، و«نسخة منتهية وكيان معلّق».

**الإصلاح:** المهمة الدورية تكشف فقط (`Workflow:Reconciliation:AutoApply=false`) وتسجّل تحذيراً لكل بند وتحدّث `workflow_outcome_unapplied_total`. والتطبيق عبر نقطة المشغّل، بتشغيل جاف أولاً:

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/hcm025-repair-stuck-approvals.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/hcm025-repair-stuck-approvals.ps1 -Apply
```

البند `ApplyFinishedOutcome` يعيد تشغيل المعالج ويدوّن المشغّل فاعلاً، ولا ينشئ نسخة ولا يغيّرها. و`NotLinked` يعني نسخة مكتملة لا يربطها الكيان، فلا تُطبَّق. والاعتراض المقبول يبقى للمشغّل، لأن إعادة احتساب النتائج ليست في المعالج.


## اعتماد العهدة والفقدان (PLAN-45)

- **المهمة:** `AssetApprovalStartRetryJob` في `admin-logistics-web` تبدأ كل `AssetApproval:Retry:IntervalSeconds` (120 ثانية) ما بقي `ApprovalStarting`، حتى `MaxAttempts` (30) ثم «يتطلب تدخلاً». تعرف الجهات من `AuditLogs`، لأن قاعدة الخدمة بلا جدول جهات. سطرها في السجل: `docker logs hrm-admin-logistics-web 2>&1 | grep 'Asset approval start'`.
- **المقياسان** داخل الحاوية: `curl -s http://localhost:8080/metrics | grep '^asset_approval_'`: `asset_approval_starting_total` و`asset_approval_needs_intervention_total`. يتجاوز الثاني الصفر حين يحتاج طلب إلى «إعادة المحاولة» من صفحة الأصل أو إلى السحب.
- **سكربت الإصلاح** للطلبات `PendingApproval` بلا نسخة، جاف افتراضياً:

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/wf045-repair-asset-approvals.ps1 -Server
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/wf045-repair-asset-approvals.ps1 -Server -Apply
```

  التقرير في `artifacts/plan45/`. أما `-RestartOnLatest` فلا يُستعمل على هذا الخادم: النسخ الجارية تكمل على إصدارها المثبّت.
- **مستخدم عرض جديد:** أضفه إلى `demoUsers` في `docker/keycloak/realm-manifest.json`، ثم إلى الـ realm القائم، لأن الاستيراد للـ realm الجديد فقط و`keycloak-reconcile` لا يمس المستخدمين:

```bash
ssh hrm-vps "docker exec -i hrm-keycloak bash -s -- <username> <email> <lastName> <tenantId> <role>" < scripts/vps/add-demo-user.sh
```

  ملف المستخدم في الـ realm القائم لا يعرّف `tenant_id`، فالسكربت يسمح بالسمة أثناء الكتابة فقط ثم يعيد الملف كما كان.
- **النسخة:** `/opt/hrm/backups/pre-plan45-20261107T155048Z` (700، الملفات 600): `pg_dumpall.sql` و`env.backup` و`SHA256SUMS`، و17 سطر `CREATE DATABASE`.
- **التراجع:** `deploy.ps1 -Rollback` يعيد `973d68488f09`. صف `ApprovalStarting` يبقى حاجزاً للأصل بلا أزرار، ويظهر رقم الحالة، حتى العودة إلى هذا الوسم.

**نشر 2026-11-07.** بناء واحد لـ `9560b03e51a3` (1280 ث، منفصل عن جلسة ssh)، ونشر في 150 ث، وفحص الدخان 50 نجاحاً و0 فشل وتحذير واحد.

## الاعتماد العابر للخدمات (PLAN-46)

- **ما تغيّر:** طلب البطاقة مسودة حتى يقدّمه موظف `idcard` ويعتمده موظف `idcard` آخر من صندوق العمل، ثم يعالجه. والإجراء التأديبي مسودة حتى يعتمده `compliance` ثم `hr-management-approver`، شخصين مختلفين غير المنشئ، وعندها فقط يصل إلى الرواتب. والإجراءات القائمة قبل PLAN-46 معتمدة (`ApprovedBeforeEngine`) ولا يُعاد نشرها.
- **مستخدمو العرض:** `e2e-idcard-maker` (يطلب ويعالج)، `e2e-idcard-checker` (يعتمد)، `e2e-compliance` (ينشئ الإجراء)، `e2e-compliance-checker` (الخطوة 1)، `e2e-checker-mgmt` (الخطوة 2). كلهم في الجهة الوطنية.
- **المصالحة:** `WorkflowApprovalSweepJob` في `idcard-web` و`compliance-web` كل `WorkflowApproval:Sweep:IntervalMinutes` (10) للسجلات المعلّقة أقدم من `StaleAfterMinutes` (15). سطرها: `docker logs hrm-idcard-web 2>&1 | grep 'Workflow approval sweep of'`. والمقاييس داخل الحاوية: `curl -s http://localhost:8080/metrics | grep -E '^workflow_(outcome_ignored|approval_sweep_applied)_total'`.
- **فحص الخادم** يحتاج المتغيرات `VPS_SSH_HOST=hrm-vps` و`VPS_APP_DOMAIN` و`VPS_SSO_DOMAIN` و`VPS_TARGET_IP` (عنوان الخادم)، وإلا اتصل العميل بـ 127.0.0.1.
- **القرص:** امتلأ أثناء بناء PLAN-46 (95%). قبل كل بناء: `df -h /` و`docker system df`؛ وإن تجاوز ~85%: `docker builder prune -af` و`docker image prune -af --filter until=12h` (الإصدارات باقية في السجل المحلي).
- **النسخة:** `/opt/hrm/backups/pre-plan46-20261107T224439Z` (700، الملفات 600): `pg_dumpall.sql` و`env.backup` و`SHA256SUMS`، و17 سطر `CREATE DATABASE`.
- **التراجع:** `deploy.ps1 -Rollback` يعيد `9560b03e51a3`. لا يعرف الإصدار السابق الاعتماد: يعرض الحالتين الجديدتين رقماً ولا يعالج إلا `Pending`، ويصدر `Renew` بطاقة فوراً، وما ينشئه من إجراءات تأديبية يُنشر فوراً ويُخزَّن معتمداً قبل المحرك بالقيمة الافتراضية للعمود، فلا يُعاد نشره بعد العودة.

**نشر 2026-11-07.** البناء الأول فشل لامتلاء القرص؛ وبعد التنظيف بناء واحد لـ `dbf53280121d` (1301 ث، منفصل عن جلسة ssh)، والنشر توقف عند `keycloak-reconcile` بعد إعادة تشغيل PostgreSQL (حادثة PLAN-40)، وأعيد الأمر نفسه فنجح في 116 ث. فحص الدخان 50 نجاحاً و0 فشل وتحذير واحد. التوقف نحو 4 دقائق.

## دورة حياة قاعدة السياسة (PLAN-47)

- **ما تغيّر:** الإصدار المنشور لا يتغير: معترِض في HR.Core، وtrigger في `hr_core_db` (`policy_rules_guard` على `PolicyRules`، و`policy_versions_guard` على `PolicyTemplateVersions`، SQLSTATE `P0047`) يرفض أي تعديل لقاعدة إصدار غير مسودة، وأي تعديل لإصدار منشور إلا `EffectiveTo`/`UpdatedAt`/`UpdatedBy`، ولو من مستخدم superuser. المسودة نسخة من الإصدار النافذ، والنشر لمعتمد لم يحرّرها، والتراجع إصدار جديد. تغييرات البذور مجموعات تغيير تُسجَّل في `PolicySeedApplications`.
- **الأدوار:** `policy-author` و`policy-approver` في الـ realm بلا حامل افتراضي (`keycloak-reconcile` ينشئهما). يؤلف أيضاً `hr-core` و`admin`، وينشر `hr-management-approver` و`admin`. تغيير القوائم: `PolicyEngine:Roles:Author` و`PolicyEngine:Roles:Approver` في `hr-core-web` **وفي البوابة** معاً (البوابة تقرأ الإعداد نفسه من إعدادها).
- **تقرير القواعد التي يرفضها المدقق:** `docker logs hrm-hr-core-web 2>&1 | grep -E 'PLAN-47 validator inventory|PLAN-49 agency override inventory'` (الجزآن معاً منذ PLAN-49: القوالب الوطنية مع عدد ما قُرئ منها، ثم تجاوزات الجهات). كان المتوقع حتى PLAN-49: `R-HIRE-START` و`R-SEP-START` و`R-TKT` و`R-TRF` (تطلق مسارات سحبها PLAN-46) و`R-EST-SUBMIT` (مسار غير مربوط بالمجال)؛ ومنذ PLAN-49 لا قاعدة نافذة يرفضها المدقق (القسم «نطاق محرك السياسات»). لا يوقف التقرير الإقلاع، ولا تُصلَح القواعد في مكانها: الإصلاح مجموعة تغيير جديدة.
- **البصمة:** `docker exec -i hrm-postgresql sh -c 'psql -X -U "$POSTGRES_USER" -d hr_core_db -At' < scripts/pol047-policy-fingerprint.sql` (قراءة فقط). وصفوف «[معطّلة]» القديمة: `scripts/pol047-history-report.sql` بالطريقة نفسها.
- **التراجع إلى الإصدار السابق:** يعمل بجانب المخطط الجديد. أعمدة PLAN-47 تقبل NULL أو لها قيمة افتراضية، و`xmin` عمود نظام. بذرة الإصدار السابق تكتب في الإصدار المنشور، فيرفضها الـ trigger. البذرة إلزامية، فرفضها يوقف الإقلاع (D-38-6) إلا مع `Seed:AllowMandatoryFailure=true`. لكنها على خادم العرض لا تكتب شيئاً: كل مفتاح موجود، ولا صف `R-REC-HIRE`. ما يتعطل مع الإصدار السابق:
  - إضافة جهة بحزمة سياسة: `AgencyPolicySeeder` القديم يرفضه الـ trigger.
  - إنشاء مسودة لقالب فيه مسودة: الفهرس الفريد يردّ 500.
  - قاعدة `R-REC-HIRE` نشطة في قاعدة لم تشغّل PLAN-25: ترفض البذرة، فيتوقف الإقلاع.

  إن لم يُقلع الإصدار السابق: `scripts/vps/deploy.ps1 -Rollback`، ثم إسقاط الـ triggers بدور `hr_core_migrator`:

```sql
DROP TRIGGER IF EXISTS policy_rules_guard ON public."PolicyRules";
DROP TRIGGER IF EXISTS policy_versions_guard ON public."PolicyTemplateVersions";
DROP FUNCTION IF EXISTS public.policy_rules_guard();
DROP FUNCTION IF EXISTS public.policy_versions_guard();
```

  والعودة إلى PLAN-47 تعيدها (الترحيل يطبَّق من جديد فقط إن أُزيل من `__EFMigrationsHistory`؛ وإلا يعاد إنشاؤها بنص `PolicyImmutabilitySql` في الترحيل `20261108125046_POL047_PolicyRuleLifecycle`).
- **مستخدم العرض:** `e2e-policy-author` يؤلف (دور `policy-author` وحده)، و`e2e-checker-mgmt` يعتمد. صفحة القالب: «إنشاء مسودة (نسخة من النافذ)»، ثم المعتمد يفتح المسودة «مراجعة ونشر».
- **النسخة:** `/opt/hrm/backups/pre-plan47-20261108T144522Z` (700، الملفات 600): `pg_dumpall.sql` و`env.backup` و`SHA256SUMS`، و17 سطر `CREATE DATABASE`.

**نشر 2026-11-08.** بناء واحد لـ `ae95ec2fec82` على الخادم (نحو 22 دقيقة؛ انقطعت جلسة ssh المحلية عند 1245 ث، وأكمل الخادم البناء والدفع: 17 من 17). النشر 156 ث (السحب 2، التشغيل 91، الإقلاع 42، المطابقة 10، الصحة 12)، وأنشأ `keycloak-reconcile` الدورين. فحص الدخان 50 نجاحاً و0 فشل وتحذير واحد. البصمة: الإصدارات العشرون السابقة لم تتغير.

**نشر 2026-11-09 (بعد دمج PR 45).** بناء `61816ed7dda2` على الخادم (17 من 17). فشلت المحاولة الأولى عند `keycloak-reconcile`: أعاد النشر إنشاء حاوية PostgreSQL فأُغلقت اتصالات Keycloak المجمّعة (SQLSTATE 57P01)، وفشل أول استعلام له («could not read the clients»)، وبقيت حاويات الويب في حالة Created. أعيد تشغيل النشر نفسه مباشرة فنجح: السحب 2، التشغيل 70، الإقلاع 37، المطابقة 8، الصحة 12؛ الدخان 50 نجاحاً و0 فشل وتحذير واحد. التوقف نحو 4 دقائق (12:22 إلى 12:25:40Z). **إن فشلت المطابقة بهذا الخطأ بعد إعادة إنشاء PostgreSQL فأعد النشر نفسه؛ لا حاجة إلى التراجع.**

## الإعدادات الوطنية لكل جهة والترقيم (PLAN-48)

- **ما تغيّر:** سياسة `national_shared_read` (قراءة فقط) على الجداول الوطنية العشرة، تطبّقها `deploy/bootstrap/bootstrap.sh` مع `hr-core` في كل نشر (`scripts/sql/ten048-national-shared-read.sql`، وكتلة فحصها تفشل النشر إن نقصت أو زادت). والخادم يتصل بـ HR.Core بحساب المسؤول، فلا تُفرض RLS عليه (OPS-D).
- **الترقيم عند أول إقلاع:** `CodePolicyUpgrades` مرة واحدة (`ten048-code-policy-upgrade-v1` في `SeedPackApplications`، تحت قفل استشاري `4800480048`): أُرشف v1 من `FundedPosition` و`RecruitmentRequisition` ونُشر v2، ثم أُعيد بذر عدّادات كل مستأجر من الأرقام المصدَرة (`GREATEST`: لا يُخفض عدّاد، ولا يُخمَّن رقم لا يُحلَّل). لا يُعاد كتابة رقم صدر.
- **تقرير الأرقام بلا بادئة (قراءة فقط):** `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/ten048-find-prefixless-codes.ps1 -Target Server` من جهاز المالك، والتقرير في `artifacts/ten048/`.
- **النسخة:** `/opt/hrm/backups/pre-plan48-20261109T194711Z` (700، الملفات 600): `pg_dumpall.sql` و`env.backup` و`SHA256SUMS`، و17 سطر `CREATE DATABASE`.
- **التراجع: انشر إلى الأمام، لا إلى الخلف.** الإصدار السابق يقرأ v2 (فـ v1 مؤرشف) ويبني المفاتيح نفسها للنوعين المصحَّحين، فعدّاداتهما تستمر. لكنه يعيد العدّاد المشترك بين `AH` و`VIP` في `AssetTag`، فرقم `VIP` يصدر بعد الترقية قد يتصادم تحته متى تجاوز عدّاد `VIP` عدّاد `AH`. إن لزم التراجع، فقبل `deploy.ps1 -Rollback` ارفع العدّاد المشترك إلى عدّاد `VIP` بدور `hr_core_migrator` (لا يُخفض شيئاً):

```sql
UPDATE "CodeSequenceCounters" AS shared
SET "LastValue" = GREATEST(shared."LastValue", vip."LastValue"), "UpdatedAt" = now()
FROM "CodeSequenceCounters" AS vip
WHERE vip."EntityKind" = 'AssetTag' AND vip."ScopeKey" LIKE '%|AssetTag|ASSET-PRIVILEGED|%'
  AND shared."TenantId" = vip."TenantId" AND shared."EntityKind" = 'AssetTag' AND shared."Year" = vip."Year"
  AND shared."ScopeKey" = replace(vip."ScopeKey", '|AssetTag|ASSET-PRIVILEGED|', '|AssetTag|');
```

**نشر 2026-11-09 (PLAN-48).** بناء واحد لـ `8abd5aa7fe93` على الخادم منفصلاً عن جلسة ssh (`setsid nohup` وملف `.done`): رفع 31.9 MB في 59 ث، والبناء 1244 ث، والدفع 16 ث، و17 من 17 صورة. النشر من المحاولة الأولى من 22:46:23 إلى 22:48:31Z (نحو دقيقتين حتى الصحة الكاملة). طبّق bootstrap السياسة وتحقق منها (`hr-core national_shared_read applied and verified (TEN-048)`)، وسجّل الإقلاع الترقية: عدّاد واحد رُفع، و621 رقماً لا يُحلَّل بالصيغ الوطنية (منها `FP-####` و`CA-2026-####`) سُجّلت ولم تُخمَّن. الدخان 50 نجاحاً و0 فشل وتحذير واحد (فحص المنافذ من جهة العميل). الأرقام بلا بادئة بعد النشر: 0.

## نطاق محرك السياسات (PLAN-49)

- **ما تغيّر:** كتالوج واحد في الشيفرة (`PolicyDomainCatalog`) يحدد ما يحكمه محرك السياسات: خمسة مجالات محكومة (`Core.Recruitment`، `Core.Establishment`، `Assets.Custody`، `IDCard.Lifecycle`، `Contracts.Leave`) وخمسة عشر خارج النطاق. الترحيل `POL049_PolicyDomainRetirement` يضيف إلى `PolicyTemplates` عمودين يقبلان NULL (`RetiredAt`، `RetiredReason`) ولا يغيّر بيانات. وعند أول إقلاع تطبَّق أربع مجموعات تغيير مرة واحدة (`PolicySeedApplications`):
  - `POL-049-SCOPE-RETIRE`: إصدار أخير منشور بلا قواعد لكل قالب من الخمسة عشر، ويُعلَّم القالب مسحوباً بسببه. الإصدارات السابقة تبقى كما هي.
  - `POL-049-ASSETS-ACTION`: قواعد العهدة `R-PRIV-EVIDENCE` و`R-PRIV-WF` و`R-HIGH-APPROVAL` تُربط بالإجراء `Assign`.
  - `POL-049-IDCARD-ORPHAN`: سحب `R-ID-PROCESS` (كانت تطلق مساراً لا يبدؤه المحرك). فمجال البطاقات محكوم بلا قاعدة.
  - `POL-049-LEAVE`: سحب `R-LEAVE-MAX` و`R-LEAVE-LONG` (قيمة نائبة وأثر بلا مستهلك)، وربط `R-LEAVE-SELF` بالإجراء `Approve`.

  ويضاف رابط `ESTABLISHMENT-CHANGE-APPROVAL` لمجال الملاك (غير مثبَّت)، وتبقى الروابط الثلاثة عشر السابقة.
- **Shadow على الخادم:** الخادم يعمل ببيئة Production، فالأوضاع من ملفات `appsettings.Production.json` لا من compose (لا مفتاح `PolicyEngine__*` في بيئة أي حاوية). المجالات الخمسة `Shadow` في `hr-core-web`، وكل خدمة مالكة تضبط مجالها بالقيمة نفسها: `hr-contracts-web` (`Contracts.Leave`)، `admin-logistics-web` (`Assets.Custody`)، `idcard-web` (`IDCard.Lifecycle`). فكل إنشاء أو تعديل أو اعتماد لإجازة، وكل إنشاء أو تصنيف أو تسليم لأصل، وكل معالجة لبطاقة، يسأل HR.Core ويُسجَّل قراره في `PolicyEnforcementLedgers` (الوضع `Shadow`، و`CallerClientId` عميل الخدمة المالكة، وفارغ لمجالَي `Core.*`) ولا يغيّر نتيجة ولا يمنع إجراء. لا مجال في Enforce في أي ملف إنتاج (D-POL-13)، واختبار `Pol049PolicyModeConsistencyTests` يمنع ذلك. ومجال خارج النطاق وضعه Off مهما قال الإعداد، وأي خدمة فيها مفتاح له غير Off لا تُقلع.
- **كلفة Shadow وحدّها:** استدعاء واحد إلى HR.Core لكل إجراء (اثنان لإنشاء الأصل). إن كان HR.Core متوقفاً فالاتصال يُرفض فوراً ويكمل الإجراء. وإن كان يقبل الاتصال ولا يجيب، انتظر كل استدعاء مهلة العميل (15 ث) ثم أكمل الإجراء بسطر `policy_client_failure`؛ وقاطع الدائرة لا يعدّ هذه المهلة. الزمن الفعلي في سجل الخدمة المالكة: `docker logs hrm-hr-contracts-web 2>&1 | grep policy_client_call` (`elapsed_ms=`).
- **جرد الإقلاع (الجزآن):** `docker logs hrm-hr-core-web 2>&1 | grep -E 'PLAN-47 validator inventory|PLAN-49 agency override inventory'`. المتوقع على خادم العرض: `0 rule(s) of effective versions the validator refuses; N more in superseded versions (history, unchanged); 20 national template(s) read` ثم `0 rule(s) of overrides in force the validator refuses, in 0 of 0 agency tenant(s) read`. وللتحقق من جهاز المالك: احفظ ناتج الأمر في ملف، ثم `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/pol047-validate.ps1 -LogFile <الملف>`: يفشل إن ذكر آخر إقلاع قاعدة نافذة يرفضها المدقق، أو لم يقرأ أي قالب وطني، أو تعذّر أخذ أحد الجزأين.
- **الخدمات الثماني** التي لم تستدعِ المحرك قط (`hr-health-web`، `hr-movements-web`، `hr-training-web`، `payroll-web`، `records-archive-web`، `it-infra-web`، `dev-analytics-web`، `compliance-web`) لم تعد تسجّل عميل السياسة. وثلاث منها (`hr-health-web`، `hr-training-web`، `dev-analytics-web`) فقدت `Integration__HrCoreBaseUrl` لأن لا قارئ آخر له فيها.
- **صفحة السياسات** (`/ui/hr-core/PolicyTemplates`) تعرض الكتالوج: خمسة مجالات محكومة بوضع Shadow، وخمسة عشر خارج النطاق بسبب كل منها وبلا رابط مسودة. القالب المسحوب لا يقبل مسودة ولا تراجعاً.
- **ما تتركه فحوص الخادم:** جولة PLAN-47 تعمل الآن على `Contracts.Leave` (كانت على `Analytics.ETL`، وهو خارج النطاق): كل تشغيل يضيف إصدارين إلى سياسة الإجازات الوطنية النافذة (نسخة ثم تراجع إليها)، والقواعد تنتهي كما بدأت، وإن توقفت الجولة في منتصفها حذفت مسودتها. وفحص PLAN-49 الثاني يترك في كل تشغيل طلب إجازة ملغى (الرصيد يعود) وطلب تجميد وظيفة ملغى مع نسخة اعتماده الملغاة، وكلاهما موسوم «PLAN-49 server check»، وثلاثة صفوف في الدفتر. **مسودة مفتوحة على قالب محكوم تؤجل كل مجموعة تغيير لاحقة لذلك القالب حتى تُنشر أو تُحذف.**
- **النسخة:** `/opt/hrm/backups/pre-plan49-20261111T004110Z` (700، الملفات 600): `pg_dumpall.sql` و`env.backup` و`SHA256SUMS`، و17 سطر `CREATE DATABASE`.
- **التراجع إلى `8abd5aa7fe93`:** `scripts/vps/deploy.ps1 -Rollback`، بلا SQL وبلا استعادة (قُرئت شيفرة الإصدار السابق بـ `git show`):
  - العمودان الجديدان يقبلان NULL، والنموذج السابق يسمّي أعمدته فلا يقرؤهما ولا يكتبهما. وصف `POL049_PolicyDomainRetirement` في `__EFMigrationsHistory` لا يبحث عنه الإصدار السابق (يطبّق الترحيلات المعلّقة فقط).
  - مطبِّق الإصدار السابق يقرأ تسجيل مجموعتيه فقط (`POL-2026-10-30-BASELINE`، `POL-2026-10-30-RETIRE-R-REC-HIRE`)، وهما مسجلتان بالبصمة نفسها، فلا يعيد إضافة `R-LEAVE-MAX` ولا `R-ID-PROCESS`. وصفوف `POL-049-*` لا يقرؤها.
  - القوالب المسحوبة وإصداراتها الأخيرة الفارغة: المحرك السابق يجيب عن إصدار بلا قواعد بالسلوك الافتراضي، ولا خدمة تستدعي تلك المجالات. لكن صفحته تعرضها قوالب عادية وتسمح بفتح مسودة فيها: **لا تفتح مسودة هناك**، وأي مسودة تُركت يجب حذفها قبل العودة إلى هذا الإصدار.
  - الإصدارات الجديدة لـ `Assets.Custody` و`IDCard.Lifecycle` و`Contracts.Leave` تبقى نافذة. تلك المجالات Off في الإصدار السابق (ملفاته تضبط `Core.Recruitment` وحده)، فلا يقيّمها أحد. والخدمات الثلاث تتوقف عن سؤال HR.Core، وصفوف الدفتر تبقى تاريخاً.
  - compose الإصدار السابق يعيد `Integration__HrCoreBaseUrl` للخدمات الثلاث.
  - الإصدارات لا يُتراجع عنها بالتراجع عن النشر (التراجع عن إصدار هو إصدار جديد، لا تعديل). استعد النسخة فقط إن تضررت بيانات.

**نشر 2026-11-11 (PLAN-49).** بناء واحد لـ `046316bde9e7` على الخادم منفصلاً عن جلسة ssh (`setsid nohup` وملف `.done`): رفع 32.0 MB في 15 ث، والبناء 1223 ث، والدفع 17 ث، و17 من 17 صورة. النشر من المحاولة الأولى من 01:03:34 إلى 01:05:34Z (119.6 ث: السحب 2، التشغيل 92، الإقلاع 38، المطابقة 8، الصحة 13؛ غابت الخدمات نحو دقيقة و45 ثانية). طبّق الإقلاع الأول الترحيل والمجموعات الأربع: خمسة عشر قالباً مسحوباً بإصدار أخير بلا قواعد (الإصدار 2، والإصدار 6 لـ `Analytics.ETL` فوق إصداراته الخمسة)، وإصدار 2 لكل من `Assets.Custody` (ست قواعد، ثلاث منها مربوطة بـ `Assign`) و`IDCard.Lifecycle` (بلا قواعد) و`Contracts.Leave` (`R-LEAVE-SELF` وحدها). وقال الجرد: `0 rule(s) of effective versions the validator refuses; 23 more in superseded versions (history, unchanged); 20 national template(s) read`، و`0 rule(s) of overrides in force ..., in 0 of 0 agency tenant(s) read`. الدخان 50 نجاحاً و0 فشل وتحذير واحد (فحص المنافذ من جهة العميل).

## وصول المعتمد الإداري والتفويض (PLAN-50)

- **ما تغيّر:** المعتمد الإداري (`hr-management-approver`) يصل عبر البوابة إلى قراءة الأنواع الخمسة من الحركات واعتمادها (النقل والندب والإعارة والترفيع والتكليف)، وإلى إجراءات `HrCore.Approve` وصفحة التفويض؛ ولا ينشئ ولا ينفّذ. و`hr-movements` لا يعتمد. وللتفويض أثر فعلي: المفوَّض يعتمد خطوة الدور المفوَّض في محرك المسارات واعتمادات الحركات «بالنيابة عن» أصيله، داخل المدة ونوع الكيان ونطاق الأصيل المخزّن، ولا يتجاوز فصل المهام.
- **الترحيلان:** `HCM050_ApprovalDelegationScope` في `hr_core_db`: يحذف جدول `WorkflowDelegations` (كان فارغاً على الخادم)، ويضيف إلى `ApprovalDelegations` أعمدة الأصيل والمفوَّض بالموظف ونطاق الأصيل وسبب الإلغاء، ويجعل `DelegateId` يقبل NULL، ويلغي كل صف نافذ بلا دور أو بدور غير قابل للتفويض («PLAN-50 migration»)، ويضيف قيدين. على الخادم: صفر صف، فلم يُلغَ شيء. و`MOV050_ApprovalOnBehalfOf` في `hr_movements_db`: عمودان يقبلان NULL (`OnBehalfOfUserId`، `OnBehalfOfName`) في `TransferApprovals` و`MovementChecklists`. كلاهما يعمل في معاملة ويرفع `FORCE ROW LEVEL SECURITY` ثم يعيده.
- **النقطة الداخلية:** `GET /api/internal/approval-delegations/active` في HR.Core، لعميل `svc-hr-movements` وحده بنطاق `hr-core.lookup` (`Service.ApprovalDelegationRead`)، وتقرأ جهة الطلب فقط. HR.Movements يحفظ الجواب 60 ثانية (`Delegation:CacheSeconds`)، وأي فشل يعني «لا تفويض». فالإلغاء يسري فوراً في HR.Core، وخلال دقيقة في الحركات.
- **المستخدم الجديد:** `e2e-delegate-hr` (`hr-core`، `offline_access`، `tenant_id` للجهة الوطنية، و`employee_id` لموظف عرض موجود)، بكلمة مرور العرض، عبر `scripts/vps/add-demo-user.sh --employee-id`. ملف المستخدم في الـ realm الحي لا يعلن `employee_id`، فالسكربت يفتحه لكتابة واحدة ثم يعيده كما كان، والسمة المخزّنة يقرؤها المحوِّل في الرمز. وهو في `docker/keycloak/realm-manifest.json`. **التراجع:** حذف المستخدم من لوحة Keycloak (§7).
- **ما تتركه فحوص الخادم في كل تشغيل:** تفويضان ملغيان (`e2e-checker-mgmt` ← `e2e-delegate-hr`، لنوعي النقل وطلب الشاغر، سبب الإلغاء موسوم «PLAN-50 server check»)، وطلب شاغر معتمد على وظيفة ممولة شاغرة أنشأه `hradmin` واعتمد `e2e-delegate-hr` خطوته الأولى بنفسه والثانية بالنيابة (السجل والتوقيع يسمّيان الأصيل)، ونقلان موسومان أنشأهما `e2e-movements-ops` ثم ألغاهما: واحد اعتمده المعتمد بنفسه، وآخر اعتمده المفوَّض بالنيابة عنه (سجل الاعتماد يسمّي الأصيل). وصفوف التدقيق.

- **النسخة:** `/opt/hrm/backups/pre-plan50-20261111T161806Z` (700، الملفات 600): `pg_dumpall.sql` و`env.backup` و`SHA256SUMS`، و17 سطر `CREATE DATABASE`.
- **التراجع إلى `046316bde9e7`:** `scripts/vps/deploy.ps1 -Rollback`، بلا SQL وبلا استعادة (قُرئت شيفرة الإصدار السابق بـ `git show`):
  - الإصدار السابق لا يبحث عن صفّي `HCM050` و`MOV050` في `__EFMigrationsHistory` (يطبّق المعلّق فقط)، ونموذجه يسمّي أعمدته فلا يقرأ الأعمدة الجديدة ولا يكتبها.
  - جدول `WorkflowDelegations` محذوف، ونموذج الإصدار السابق يعرفه لكن لا شيفرة فيه تقرؤه ولا تكتبه.
  - **صفحة التفويض في الإصدار السابق** تفترض `DelegateId` غير فارغ: صف أنشأه هذا الإصدار (المفوَّض بالموظف، `DelegateId` فارغ) يُسقطها. وإنشاء تفويض فيها بلا دور يرفضه القيد `CK_ApprovalDelegations_ActiveHasRole` (خطأ 500). فإن احتيج إليها بعد التراجع: `DELETE FROM "ApprovalDelegations" WHERE "DelegateId" IS NULL;` في `hr_core_db` (بعد نسخة). على الخادم اليوم: تفويضات فحوص الخادم الملغاة وحدها.
  - عمودا «بالنيابة عن» في `TransferApprovals` و`MovementChecklists` يبقيان ويتجاهلهما الإصدار السابق، وسجلات الاعتماد بالنيابة تبقى تاريخاً.
  - المعتمد يفقد وصوله إلى شاشات الحركات وإجراءات `HrCore.Approve` التي فتحها هذا الإصدار، ويعود `hr-movements` إلى ما كان.

**نشر 2026-11-11 (PLAN-50).** بناء واحد لـ `a504f6e54921` على الخادم منفصلاً عن جلسة ssh (`setsid nohup` وملف `.done`): رفع 32.2 MB في 235 ث. المحاولة الأولى توقفت بعد 45 ث لأن خادمَي DNS لدى المزوّد أجابا SERVFAIL عن `ubuntu.com` (ولم يُغيَّر إعداد المحلِّل)، وبدأت الثانية تلقائياً حين عادا (بعد 32 دقيقة): البناء 1406 ث، والدفع 26 ث، و17 من 17 صورة. النشر من المحاولة الأولى من 17:20:12 إلى 17:22:43Z (السحب 2، التشغيل 96، الإقلاع 43، المطابقة 9، الصحة 13؛ غابت الخدمات نحو دقيقة و50 ثانية). الدخان 50 نجاحاً و0 فشل وتحذير واحد (فحص المنافذ من جهة العميل).

## سياسة صريحة لكل إجراء (PLAN-51)

- **ما تغيّر لمستخدمي العرض:**
  - صفحات تدقيق البوابة (`/Home/Audit` و`/Home/Operations` و`/ops/audit-search` و`/ops/metrics-summary`) صارت لسياسة `Gateway.Audit`: `platform-admin` يرى صفوف كل الجهات، و`auditor` و`compliance` يريان صفوف جهتهما فقط. فيفقدها `hradmin` و`e2e-hr-admin`، ويكسبها `e2e-compliance` بصفوف جهته. ورابطها في القائمة يظهر لحامل السياسة وحده. ومسارات الأخطاء الأخيرة في ملخص المقاييس لـ `platform-admin` وحده.
  - **الخدمة الذاتية:** `/ui/ess` و`/api/ess` لا يمرّان إلا إلى المتحكمات المسماة (`EssPortal` و`EssTalent` في HR.Core، و`Ess` في الرواتب) والملفات الثابتة بـ GET؛ وأي مسار آخر تحتهما يجيب 404 من البوابة نفسها ويسجّل التدقيق `Denied:EssNotExposed`. ولا يحمل أي مستخدم عرض `hr-employee`، فلا يتغير عليهم شيء يرونه، لكن الـ 404 يصح لكل مستخدم.
  - **التسجيل الجماعي للجهات (Onboarding)** صار لـ `platform-admin` وحده (`Platform.AgencyOnboarding`)، ولا يحمله مستخدم عرض؛ و`apply-file` محذوف. والسكربتات والبذور تبقى على `AgencyManifestLoader`.
  - **كل كتابة تسمي سياستها:** ما لم يسمِّ سياسة يلقى سياسة الكتابة لوحدته (`{Module}.Write`) بأدوار اليوم نفسها، فلا يفقد مستخدم عرض كتابة كان يملكها، ولا يكسب القارئ أو المعتمد كتابة. والرواتب تنتظر PLAN-53.
  - **D-IAM-4:** استثناء `hr-admin` من `HrHealth.Clinical` و`Payroll.Post` و`Payroll.Reverse` مكتوب في ملفات Production وStaging، لكنه معطّل حين `Deployment__Mode=demo`، وهذا هو حال الخادم؛ فـ `hradmin` يبقى على ما كان.
- **ما لم يتغير على الخادم:** لا ترحيل؛ ولا مفتاح جديد في `.env` ولا في ملف compose (المفاتيح الجديدة `Gateway:Ess` و`Gateway:Audit:Roles` و`Evidence:IngestClients` و`Authorization:AdminExcludedPolicies` في ملفات الإعداد داخل الصور)؛ ولا نطاق ولا عميل في الـ realm (D-51-4، والمطابقة 0 تغيير).
- **صفوف التدقيق القديمة:** نسخة الطابور التي تُكتب في القاعدة كانت تسقط `TenantId` منذ أول إصدار، فكل الصفوف قبل هذا النشر (10095) بلا جهة، ولا يراها إلا `platform-admin`. والصفوف الجديدة تحمل جهتها. لا تعبئة لاحقة: جهة الصف القديم لا تُقرأ بثقة من اسم المستخدم.
- **ما يراه المرفوض:** حين يفتح `hradmin` أو `e2e-hr-admin` صفحة التدقيق مباشرة، ترفضه البوابة بتحويل إلى `/Account/AccessDenied`، وهي صفحة لا تملكها البوابة فيرى «غير موجود» (404). الرفض قائم، والصفحة عيب مسجّل لخطة لاحقة (والرابط لا يظهر له في القائمة).
- **ما تتركه فحوص الخادم (`AC_IAM_51_vps_*`) في كل تشغيل:** صفوف تدقيق فقط؛ كل POST فيها يُرفض قبل أي كتابة (404 أو 403)، والتسجيل الجماعي تشغيل تجريبي ترفضه البوابة.

- **النسخة:** `/opt/hrm/backups/pre-plan51-20261112T065411Z` (700، الملفات 600): `pg_dumpall.sql` و`env.backup` و`SHA256SUMS`، و17 سطر `CREATE DATABASE`.
- **التراجع إلى `a504f6e54921`:** `scripts/vps/deploy.ps1 -Rollback`، بلا SQL وبلا استعادة (قُرئت شيفرة الإصدار السابق بـ `git show`):
  - لا ترحيل في هذه الخطة، فلا شيء في القواعد يحتاج الإصدار السابق إلى تجاهله سوى الصفوف نفسها.
  - صفوف التدقيق الجديدة تحمل `TenantId`؛ والإصدار السابق لا يرشّح بالجهة، فيعرض كل الصفوف لأي مستخدم مسجّل (سياسته القديمة `Gateway.Dashboard`).
  - صفوف `Denied:EssNotExposed` يعرضها الإصدار السابق بقيمتها كما هي (لا ترجمة لها في جدوله)، ولا يكسر ذلك شيئاً.
  - مفاتيح الإعداد الجديدة داخل الصور الجديدة، فتذهب معها؛ ويعود السابق إلى قائمة الأسماء في المرشح، و`Contains("/Ess")` في البوابة، و`apply-file`، و`HrCore.Write` لحساب `hr-core.workflow`.

**نشر 2026-11-12 (PLAN-51).** توقف المنفذ 22 من شبكة المالك من بداية الخطة، فانتظرت هذه المرحلة حتى مرّ عبر ProtonVPN. بناء واحد لـ `d0e446fe3280` على الخادم منفصلاً عن جلسة ssh (`setsid nohup` وملف `.done`): رفع 32.2 MB في 50 ث، والبناء 1817 ث، والدفع 27 ث، و17 من 17 صورة. النشر من المحاولة الأولى من 07:28:21 إلى 07:31:05Z (السحب 3، التشغيل 117، الإقلاع 51، المطابقة 14 بلا تغيير، الصحة 14؛ غابت الخدمات نحو 3 دقائق على الأكثر). الدخان 50 نجاحاً و0 فشل وتحذير واحد (فحص المنافذ من جهة العميل).

## نطاق الفرع على كل ما يصله مدير الفرع (PLAN-52)

- **ما تغيّر لمستخدمي العرض:** لم يكن أي مستخدم عرض مقيّداً بفرع، فلا يرى أحدهم تغييراً، عدا المستخدم الجديد:
  - `e2e-branch-manager` (`branch-manager` و`hr-core` و`admin-logistics` و`offline_access`، `tenant_id` للجهة الوطنية، و`scope_org_unit_id` = قسم الموارد البشرية `11111111-1111-1111-1111-111111111102`، بكلمة مرور العرض) عبر `scripts/vps/add-demo-user.sh --scope-org-unit-id`. والرمز يحمل `scope_org_unit_id` (تحقق Keycloak من رمز مثال للعميل `hr-gateway-client`). وهو في `docker/keycloak/realm-manifest.json`.
  - في HR.Core يرى سجلات قسمه وما تحته فقط: جدول مناوبته يفتح، وقائمة المناصب القيادية مناصب قسمه وحدها. وسجل قسم آخر (المالية `…111103`) يجيب 404 على التفاصيل وعلى أي POST بمعرّفه، ولا يتغير صفه. والكتالوجات المركزية (الورديات والمخزون والموردون) 403.
  - في `api/workflow-instances` نسخ قسمه فقط (6). ونسخة قسم آخر أو نسخة مركزية (بلا وحدة) 404 له و200 لـ `hradmin`.
  - في معالج التعيين لا يرى أياً من معاملات العرض الثماني، و`Step2` لها 404: لا لقطة موظفين في HR.Contracts على الخادم، فالمعاملات مركزية (D-52-1).
  - **Admin.Logistics:** لا لقطة وحدات فيها على الخادم (0، وكذلك HR.Health وHR.Training، مقابل 30 في HR.Contracts وPayroll وHR.Movements، رغم أن طوابير `IOrgUnitChanged` مربوطة ولها مستهلك). فنطاقه هناك `Denied`، وكل شاشة لوجستيات تجيبه 403 (رفض آمن، لا وصول أوسع). عيب مسجّل لخطة لاحقة.
- **ما تغيّر على الخادم:** الترحيل `HCM052_OrgScopeKeys` في `hr_core_db`، طبّقه `hr_core_migrator` بأقل صلاحية: عمود `OrgUnitId` يقبل NULL بفهرس ومفتاح أجنبي (SET NULL) على `DiwanCorrespondences` و`FieldInspectionVisits`؛ وتعبئة وحدة الديوان من موظفه المرتبط؛ وختم `ScopeOrgUnitId` لنسخ الأنواع الأربعة ذات المفتاح؛ ورفع FORCE ثم إعادته على الجداول التسعة نفسها التي كانت عليه. لا مفتاح جديد في `.env` ولا في ملفات compose، والمطابقة 0 تغيير.
- **السجلات بلا مفتاح** (`scripts/iam052-validate.ps1 -Counts -SshHost hrm-vps -Project hrm`، قراءة فقط): قبل HCM052 نسخ طلبات الشاغر 22 من 22 بلا وحدة، وبعده 0 من 22. وبقية الأنواع (العهدة 16، الفقدان 8، البطاقات 14، التأديب 7، الملاك 3) مركزية بالتصميم. والديوان والتفتيش 0 صفوف. وفي HR.Contracts المعاملات الستون والعقود الخمسمئة بلا لقطة موظف، فهي مركزية. وفي Admin.Logistics الأصول الـ224 مركزية للسبب نفسه، أما أوامر التوريد الخمسون فكلها بوحدة.
- **ما تتركه فحوص الخادم (`AC_IAM_52_vps_*`):** لا شيء. كل POST فيها يُرفض قبل أي كتابة (404 أو 403)، وصفوف الأهداف تُقرأ قبل وبعد.

- **النسخة:** `/opt/hrm/backups/pre-plan52-20261113T064822Z` (700، الملفات 600): `pg_dumpall.sql` و`env.backup` و`SHA256SUMS`، و17 سطر `CREATE DATABASE`.
- **التراجع إلى `d0e446fe3280`:** `scripts/vps/deploy.ps1 -Rollback`، بلا SQL وبلا استعادة (قُرئت شيفرة الإصدار السابق بـ `git show`):
  - لا يعرف الإصدار السابق العمودين الجديدين، فلا يربطهما EF ولا يراهما. ولا يرى ترحيلاً معلّقاً: يطبّق ما يعرفه فقط، وسطر HCM052 الزائد في السجل لا يضره.
  - ما يضيفه في الديوان والتفتيش يبقى بلا وحدة، ونسخ المسارات التي يبدؤها لا يختمها. فهي مركزية لمستخدم مقيّد بعد العودة إلى الأمام، ولا تُعبّأ تلقائياً، لأن HCM052 لا يُعاد.
  - يعود مستهلك النقل في Auth.Directory إلى تحديث الدليل وحده، فلا يمسح النطاق. ولا مستهلك للنقل في HR.Health.
  - يبقى المستخدم `e2e-branch-manager`، والإصدار السابق يطبّق عليه نطاقه الجزئي القديم. احذفه إن لزم.

**نشر 2026-11-13 (PLAN-52).**
- **البناء:** توقف المنفذ 22 من 01:45Z حتى شُغّل ProtonVPN (06:47Z). بناء واحد لـ `92a69960c826` على الخادم منفصلاً عن جلسة ssh (`setsid nohup` وملف `.done`): رفع 32.3 MB في 203 ث، والبناء 2084 ث، والدفع 38 ث، و17 من 17 صورة.
- **المحاولة الأولى للنشر** توقفت محلياً قبل أن تمس الخادم: Docker Desktop كان متوقفاً على جهاز المالك، فلم يعمل فحص gitleaks للحزمة.
- **المحاولة الثانية** من 07:38:36 إلى 07:43:17Z: السحب 15 ث، والتشغيل 147، والإقلاع 65 (ومنه HCM052)، والمطابقة 17 بلا تغيير، والصحة 17. غابت الخدمات نحو 3 دقائق على الأكثر.
- **الدخان:** 50 نجاحاً و0 فشل وتحذير واحد (فحص المنافذ من جهة العميل).

## واجهة عربية مقروءة وسياسة أمن محتوى واحدة (PLAN-67)

- **ما يراه مستخدمو العرض:**
  - سياسة CSP واحدة لكل صفحة: لون الجهة وسكربتاتها وكتل الصفحات تعمل، والبوابة العامة على `/careers` بتنسيقها الكامل. وقبل النشر كانت كل صفحة محوَّلة تحمل سياستين (البوابة ثم الخدمة) فيُحجب كل ذلك.
  - `/ui/hr-core/` يفتح لوحة القيادة لا البوابة العامة، و`/ui/hr-core/track` و`/my` 404. روابط البوابة العامة كلها كما هي.
  - كل وقت بتوقيت دمشق وبتنسيق واحد (`yyyy/MM/dd HH:mm`، «بتوقيت دمشق» في التلميح)، فيتقدم ثلاث ساعات عما ظهر قبل النشر. والتخزين UTC كما كان. وحقول التاريخ التقويمي (فترات الرواتب وغيرها) صارت `type="date"`.
  - الحالات والتسميات بالعربية، والأشخاص بأسمائهم من سجل البوابة: يظهر الاسم لمن دخل مرة بعد النشر، ومن لم يدخل يبقى «مستخدم غير معروف» والمعرّف في تلميح. وبعد فحوص الخادم يعرف السجل 14 مستخدماً (`hradmin` ومستخدمو `e2e-*`)، ولا «غير معروف» بين الأشخاص الذين تطبعهم صفحات السياسة والاعتماد والشواغر والملاك (3 معرّفات على الخادم).
  - قواعد السياسات جملٌ عربية، والمحاكاة بنموذج، وJSON خلف «عرض تقني» للمسؤول ومؤلفي السياسات ومعتمديها.
  - كل إجراء هدّام يسأل أولاً، وملاحظات المراجع تصل سجل الاعتماد في القبول والرفض.
- **ما تغيّر على الخادم:** الترحيل `UX067_UserNameRegistry` في `gateway_audit_db` (جدول `gateway_user_names`: المعرّف والاسم المعروض و`AgencyId` وأول وآخر ظهور)، طبّقه `gateway_audit_migrator`، وللدور `gateway_audit_app` صلاحياته بالامتيازات الافتراضية. لا مفتاح جديد في `.env` ولا في ملفات compose، والمطابقة بلا تغيير.
- **بقايا الفحوص (D-67-6):** أُغلق طلب الشاغر `REQ-2026-00004` (`23b42bbe-0980-460f-94f4-ca38f3f3a5d6`) بإجراء «إغلاق» في التطبيق كـ `hradmin`، فأُخفي إعلانه `b4e13f41-3d6b-42e7-b33c-d3ec26b9a875` («محاسب»، «PLAN-43 server check»). لم يكن عليه طلب مفتوح، فلم يُرفض شيء ولم تُلغَ مقابلة. لا حذف. ولا إجراء إعادة فتح في التطبيق، فالتراجع من النسخة أو بإعادة الحالة والنشر يدوياً (الجدول في §10). وتعرض البوابة الآن ثلاثة إعلانات. والإعلان `46635d93…` («موظف فئة ثالثة»، نص تجريبي ليس من فحوص الخادم) باقٍ للمالك.
- **فحوص الخادم لا تترك أثراً عاماً:** تكتب عناوين وملاحظات عربية واقعية والوسم في حقول لا يراها الموظف، ولا تنشر إعلاناً. وفحص CSP ينشئ طلب اعتماد معلّقاً ومسودة سياسة لمدة القراءة ثم يحذف الأول ويهمل الثانية بنموذج المحرر.

- **النسخة:** `/opt/hrm/backups/pre-plan67-20261115T063540Z` (700، الملفات 600): `pg_dumpall.sql` و`env.backup` و`SHA256SUMS`، و17 سطر `CREATE DATABASE`.
- **التراجع إلى `92a69960c826`:** `scripts/vps/deploy.ps1 -Rollback`، بلا SQL وبلا استعادة:
  - لا يعرف الإصدار السابق جدول الأسماء ولا يقرؤه، ولا يرى ترحيلاً معلّقاً (سطر `UX067_UserNameRegistry` الزائد في السجل لا يضره). والجدول يبقى، ويعود إليه الإصدار الجديد عند العودة إلى الأمام.
  - تعود السياستان لكل صفحة محوَّلة، فتعود البوابة العامة بلا تنسيق ويُحجب لون الجهة والسكربتات المضمّنة. وتعود الأوقات بتوقيت UTC، و`/ui/hr-core/` يفتح البوابة العامة كما كان.
  - ما خزّنه الإصدار الجديد من أوقات هو UTC، وحقول التاريخ التقويمي منتصف الليل UTC، فيقرؤها الإصدار السابق صحيحة.
  - طلب الشاغر المغلق يبقى مغلقاً.

**نشر 2026-11-15 (PLAN-67).**
- **البناء:** المحاولة الأولى (06:38 إلى 07:09Z) بنت 16 صورة من 17، وتوقفت صورة `hr-migrate` عند حزمة EF لـ dev-analytics حين امتلأ قرص الخادم (98%، 3.3 GB): ذاكرة البناء 13.3 GB بجانب 61 GB من الصور. لم يُمس الإصدار العامل. حُذفت ذاكرة البناء وصور سبعة إصدارات قديمة محلياً (باقية في السجل الحلقي)، فصار الفارغ 56 GB. المحاولة الثانية (07:13 إلى 07:48Z): البناء 2077 ث، والدفع 37 ث، و17 من 17 صورة. **قاعدة:** 30 GB فارغة على الأقل قبل بناء بارد.
- **النشر** من 07:49:01 إلى 07:52:56Z: السحب 5 ث، والتشغيل 139، والإقلاع 63 (ومنه ترحيل البوابة)، والمطابقة 15 بلا تغيير، والصحة 15. غابت الخدمات نحو 3 دقائق على الأكثر.
- **الدخان:** 50 نجاحاً و0 فشل وتحذير واحد (فحص المنافذ من جهة العميل).
- **فحوص PLAN-67 على الخادم:** 21 من 22 صفحة رئيسية بسياسة واحدة وبلا مخالفة (صفحة التعيين اختيارية: لا طلب بعرض مقبول على الخادم)، والبوابة منسّقة على `/careers`، وصفحة السياسة بالعربية بجملها وأوقاتها، والتأكيد والملاحظات. وفحوص PLAN-36..52: 48 من 49، والفاشل فحص دورات AC-WF-46 المعروف (نافذة السجل بعد إعادة الإنشاء)، ونجح وحده بعد الدورة التالية. والدخان في النهاية 50/0/1.

## صلاحيات كتابة الرواتب ومعاملة تغيير الراتب (PLAN-53)

- **ما تغيّر لمستخدمي العرض:**
  - `e2e-payroll-processor` (`payroll` و`payroll-processor`): يقدّم **طلب** تغيير راتب أو درجة من «ملفات الأجر ← طلب تغيير راتب» بسبب ومرجع قانوني، من أول يوم في شهر (الجاري أو لاحق، بتاريخ دمشق) وضمن حدود الدرجة؛ الطلب «بانتظار الاعتماد» والملف الفعّال لا يتغير. لا يعتمد طلبه (403). ويقدّم طلب تعديل سلّم أو درجة من شاشتيهما.
  - `e2e-payroll-approver` (`payroll` و`payroll-approver`): يعتمد أو يرفض من «طلبات تغيير الراتب» و«طلبات البيانات المرجعية». اعتماد طلب حان موعده يطبّقه فوراً (نسخة ملف جديدة تحمل الوحدة والنظام والحساب)، وطلب لشهر قادم يصير «مجدولاً» وتطبّقه المهمة في موعده. لا يقدّم طلباً ولا يكتب بيانات مرجعية (403).
  - `hradmin` يحتفظ بسياسات الرواتب في وضع العرض (`Deployment__Mode=demo`) كما كان.
  - **دور `payroll` وحده لا يكتب شيئاً** في الرواتب بعد اليوم.
  - **شهر محسوم لا يدخله تغيير:** اعتماد طلب يسري في شهر له تشغيل مجمّد أو معتمد أو مرحّل يغطي الموظف يُرفض (409) برسالة تسمّي الشهر. على العرض: الأشهر 2026-01 إلى 2026-05 محسومة، وتشغيل 2026-06 محسوب غير مجمّد (الاعتماد فيه يُطبَّق مع تحذير بإعادة الحساب).
  - **تشغيل اعتيادي جديد على العرض ما زال يُرفض** بحاجز نظام الخدمة: 500 سجل توظيف جارٍ بلا نظام و500 استثناء `UnclassifiedRegime` مفتوح (قراءة P0). رقعة القبول لا تعمل على العرض عمداً؛ تصنيف بيانات العرض ليس من هذه الخطة.
- **ما تغيّر على الخادم:** الترحيل `PAY053_SalaryChangeRequests` في `payroll_db` (جدولا `PayProfileChangeRequests` و`PayrollReferenceChangeRequests` بأمان الصفوف وفهرسين فريدين مرشَّحين، وعلاقتا السلّم←الدرجات والدرجة←ملفات الأجر صارتا `RESTRICT`). لا تغيير في البيانات. ومهمة التفعيل `PayProfileChangeActivationJob` تعمل كل 60 دقيقة في Payroll.
- **ما تتركه فحوص الخادم (`AC_IAM_53_vps_*`):** في كل تشغيل طلب تغيير راتب واحد `Cancelled` (مرجع المصدر `PLAN-53 server check`) وطلب بيانات مرجعية واحد `Rejected`، مع صفوف تدقيقهما. بعد هذه الخطة على الخادم: ثلاثة طلبات راتب ملغاة وطلبان مرجعيان مرفوضان. لا ملف أجر تغيّر (عدد الملفات الفعّالة وبصمة رواتبها كما قبل)، ولا طلب مفتوح، ولا تشغيل أُنشئ. الطلبات لا تُحذف (سجل).
- **النسخة:** `/opt/hrm/backups/pre-plan53-20261122T224310Z` (700، الملفات 600): `pg_dumpall.sql` و`env.backup` و`SHA256SUMS`، و17 سطر `CREATE DATABASE`.
- **التراجع:** الإصدار الجاري `5aa61b78e3a6` (الكود المدموج) وسابقه `27cbf35db9cc` كلاهما من هذه الخطة (السابق بلا رمز منع التزوير على واجهة طلبات تغيير الراتب)، فـ`scripts/vps/deploy.ps1 -Rollback` يعود إلى `27cbf35db9cc`. والعودة إلى إصدار PLAN-67: `scripts/vps/deploy.ps1 -Tag 1ee455bf1a6f` (صوره في السجل المحلي للخادم وعلى قرصه)، بلا SQL وبلا استعادة (قُرئت شيفرته بـ `git show`):
  - لا يعرف الإصدار السابق الجدولين الجديدين فلا يقرؤهما ولا يكتبهما، وسطر PAY053 الزائد في سجل الترحيلات لا يضره.
  - **كتابته المباشرة للراتب تعود:** `EmployeePayProfiles/Create` و`PUT …/pay-profile` يكتبان ملف الأجر بدور `payroll` وحده وبلا معتمد (G-IAM-02 كما كانت).
  - **حذف درجة مستعملة أو سلّم فيه درجات صار خطأً** (500 من قيد `RESTRICT`) بدل أن يُفرغ الدرجة من الملفات أو يحذف الدرجات بصمت؛ القيد يبقى في القاعدة.
  - **الطلبات المجدولة تنتظر:** لا مهمة تفعيل في الإصدار السابق، فتبقى `Scheduled` حتى العودة إلى هذا الإصدار، وعندها تطبَّق إن لم يتغير ملف صاحبها (وإلا تبقى مجدولة بسببها). وإن كتب الإصدار السابق راتباً مباشرة في أثناء ذلك فالطلب المكتوب على الملف القديم يُرفض عند التفعيل (409) ويُلغى ويُقدَّم من جديد.
  - الشاشات الجديدة تختفي؛ لا مستخدم ولا إعداد يحتاج تراجعاً.

**نشر 2026-11-22 (PLAN-53).**
- **البناء:** بناء واحد لـ `0640c2ac6db0` على الخادم. `build-on-host.ps1` رفع الأرشيف (31.0 MB في 139 ث) وبدأ البناء **مرتبطاً بجلسة ssh** كما هو مكتوب؛ فأُوقف بعد عشر دقائق بأرقام عملياته وأعيد السكربت نفسه **منفصلاً** (`setsid nohup`، وملف `.done`): البناء 1542 ث، والدفع 44 ث، و17 من 17 صورة. القرص 37 GB قبل البناء و29 GB بعده (السكربت يحذف المصدر وذاكرة البناء وصورة SDK).
- **المحاولة الأولى للنشر** توقفت محلياً قبل أن تمس الخادم: `make-deploy-bundle.ps1` يستدعي `tar.exe`، وجهاز المالك صار لينكس. أُصلح السكربتان (`tar` خارج ويندوز).
- **المحاولة الثانية** من 23:49:30 إلى 23:54:06Z: السحب 18 ث، والتشغيل 149، والإقلاع 63 (ومنه PAY053)، والمطابقة بلا تغيير. غابت الخدمات نحو 3 دقائق على الأكثر.
- **فحص المتصفح الجديد وجد عيباً فأُعيد البناء والنشر (2026-11-23):** الشاشات الأربع التي فيها نموذج أطلقت مخالفة `style-src-attr` واحدة: ملخّص تحقق فارغ (`asp-validation-summary="All"`) يكتب `<li style="display:none">`. أُصلحت نماذج الرواتب الاثنا عشر (`Views/Shared/_FormErrors.cshtml`: الملخّص يُكتب عند وجود أخطاء فقط)، وأضيف نموذج إلى صفحات AC-UX-67 الرئيسية. قبل البناء الثاني حُذفت الصور المحلية لإصدارَي `d0e446fe3280` و`92a69960c826` (القرص 28 GB ← 40 GB؛ تبقى في السجل المحلي). البناء الثاني لـ `27cbf35db9cc` منفصلاً من أوله: رفع 31.0 MB في 22 ث، والبناء 2035 ث، والدفع 34 ث، و17 من 17. النشر من 01:47:39 إلى 01:51:27Z: السحب 13 ث، والتشغيل 138، والإقلاع 60، والمطابقة 13، والصحة 15؛ والدخان 50 نجاحاً و0 فشل وتحذير واحد.
- **إصلاح ثالث بعد أول تشغيل CI لطلب الدمج (2026-11-23):** CodeQL وجد أن كتابات واجهة طلبات تغيير الراتب الخمس بلا رمز منع التزوير، والواجهة تُبلَغ بجلسة البوابة. أضيف `GET …/antiforgery` و`[ValidateAntiForgeryToken]` (نمط PLAN-40 و43 و47). بناء ثالث لـ `5aa61b78e3a6` منفصلاً (1819 ث، 17 من 17)؛ **القرص نزل إلى 5.4 GB في أدنى نقطة** ثم عاد 24 GB بعد تنظيف السكربت (احذف صور الإصدارات الأقدم قبل كل بناء ولو كان الحر 30 GB). النشر من 04:21:20 إلى 04:25:11Z، والدخان 50 نجاحاً و0 فشل. وعلى هذا الإصدار نجحت فحوص PLAN-53 الأربعة، ومنها أن كتابة بلا الرمز عبر جلسة البوابة تجيب 400.
- **فحوص الخادم على الإصدار `27cbf35db9cc`:** فحوص PLAN-53 الأربعة ناجحة، ومنها ثماني شاشات رواتب في Chromium حقيقي بسياسة واحدة وبلا مخالفة. ومجموعات PLAN-36..67 شُغّلت على `0640c2ac6db0` (الفرق بينه وبين النهائي عروض الرواتب فقط): 53 من 55؛ الاثنان الباقيان لسببين في جهاز التشغيل لا في الخادم (ملف «قبل» لـ PLAN-47 كان على جهاز ويندوز المفرمَت، وجولة الصفحات الرئيسية الطويلة تقطعها تغيّرات شبكة الجهاز).
- **الدخان:** 50 نجاحاً و0 فشل وتحذير واحد (فحص المنافذ من جهة العميل).

## الإطار القانوني للرواتب وبياناته المرجعية (PLAN-54)

- **ما تغيّر لمستخدمي العرض:**
  - **الرواتب ← الأطر القانونية** فيها الآن «إطار جديد» (من قالب نظام قانوني أو باستنساخ نسخة) و«استيراد حزمة قانونية» (ملف JSON)، وصفحة النسخة تعرض شرائح الضريبة والخصوم النظامية والعطل وقواعد أولوية الصرف والحقول التسعة عشر، ولكل قيمة مرجعها القانوني وحالتها (موقّعة أو مؤقتة) واستثناؤها، ومن حرّرها واعتمدها وصادق عليها بالاسم.
  - **الاعتماد والمصادقة والنشر صارت لثلاثة أشخاص:** `e2e-payroll-processor` يحرّر (ينشئ ويستورد ويعدّل المسودة)، و`e2e-payroll-approver` يعتمد، و`hradmin` يصادق قانونياً (يحتفظ بسياسات الرواتب في وضع العرض، وسياسة المصادقة `Payroll:Legal:AttestorPolicy` هي `Payroll.Approve`). المحرر لا يعتمد ولا ينشر، والمعتمد لا يصادق، ولا ينشر إلا المعتمد أو المصادق؛ كل رفض برسالة عربية ولا يغيّر شيئاً.
  - **بدل أو خصم أو تسوية يدخلها شخص تنتظر اعتماد شخص ثانٍ** («بانتظار الاعتماد»)، ولا تدخل حساباً ولا قسيمة ولا تصديراً قبله. القوائم الثلاث تعرض الحالة والمصدر ومن أدخل ومن قرّر، وللمعتمد زرّا «اعتماد» و«رفض» (بملاحظة).
  - **علامة «إلزامي» في أنواع الخصوم للقراءة فقط:** الخصم النظامي صار من نسخة الإطار.
  - **لا شيء آخر تغيّر:** نسخ العرض الثلاث (`LAW_50_2004` النشطة، و`LAW_53_2021`، و`CIVIL_SERVICE_2026`) نسخ v1 ببصماتها كما هي، وتُحسب كما كانت من الجداول العامة. مدخلات العرض القائمة (800 بدل، 500 خصم، 80 تسوية) كلها `Approved` ومصدرها `System`. وتشغيل اعتيادي جديد ما زال يُرفض بحاجز نظام الخدمة (PLAN-53).
  - صفحة التشغيل ومصدره يذكران النسخة ومؤشر «إطار أقدم من حزم الإطار القانوني» لتشغيلات نسخ v1، و«إطار مؤقت — استثناء …» لتشغيل تحكمه نسخة فيها قيم مؤقتة. ولوحة الرواتب تنبّه قبل انتهاء استثناء بستين يوماً.
- **ما تغيّر على الخادم:** الترحيل `PAY054_LegalFrameworkPacks` في `payroll_db`: ثلاثة جداول جديدة بأمان الصفوف القسري (`StatutoryDeductionRules`، `PayrollConfigurationFieldSources`، `PayrollConfigurationEditors`)، وأعمدة جديدة على النسخ والشرائح والعطل وقواعد الأولوية والمصادقات والمدخلات الثلاثة ومهمة التشغيل، وفهرس سجل القرارات صار فريداً لكل جهة. **الصفوف القائمة أخذت قيمها بافتراضات الأعمدة** (`HashSchemaVersion=1`، `Approved`، `System`، `Interim`، `[]`) بلا أي عبارة بيانات. وإعدادان جديدان في Payroll (في الصورة): `Payroll:ManualInputs:RequireApproval=true` و`Payroll:Legal:AttestorPolicy=Payroll.Approve`.
- **ما تتركه فحوص الخادم (`AC_PAY_54_vps_*`):** في كل تشغيل نسخة واحدة برمز الفحص `LAW_50_2004_CHK54` **معتمدة ومصادَق عليها وغير منشورة** (تاريخ سريانها أول يوم في آخر شهر مرحّل، فيرفض D-PAY-13 نشرها: لا تحكم تشغيلاً أبداً)، وبدل يدوي واحد **مرفوض** مؤرَّخ داخل الشهر المرحّل نفسه. لا نسخة منشورة تغيّرت، ولا صف في سجل القرارات كُتب (الحزمة تكرر السجل كما هو)، ولا تشغيل ولا مدخل قائم تغيّر. النسخ لا تُحذف (سجل).
- **الحدّ المؤرخ:** الاستثناءان اللذان تستشهد بهما الحزمة المؤقتة ينتهيان في **2027-01-31**. من 2027-02-01 لا تُعتمد ولا تُنشر ولا يُحسب بنسخة فيها قيم مؤقتة حتى يُجدَّدا في `docs/str-legal/control-catalog.json` أو تُنشر نسخة بقيم موقّعة (قرار صاحب العمل). نسخ العرض v1 لا تتأثر.
- **التراجع إلى إصدار PLAN-53** (`scripts/vps/deploy.ps1 -Tag 5aa61b78e3a6`، صوره على قرص الخادم وفي سجله المحلي)، بلا SQL وبلا استعادة؛ قُرئت شيفرته بـ`git show` لمعرفة ما يفعله ببيانات هذه الخطة:
  - **لا يقرأ الجداول ولا الأعمدة الجديدة،** وسطر PAY054 الزائد في سجل الترحيلات لا يضره. إدراجاته تأخذ افتراضات الأعمدة الجديدة (نسخة v1، ومدخل `Approved`/`System`).
  - **يحسب المدخل المعلّق والمرفوض،** لأنه لا يعرف الاعتماد: كل بدل أو خصم أو تسوية بحالة `PendingApproval` أو `Rejected` يدخل التشغيل التالي. بدل الفحص مؤرَّخ داخل شهر مرحّل فلا يدخل تشغيلاً. **قبل التراجع على خادم فيه مدخلات يدوية معلّقة أو مرفوضة:** اعتمدها أو احذفها، أو أنهِ سريانها.
  - **يحسب نسخة v2 بالجداول العامة:** تشغيل تحكمه نسخة v2 منشورة يُحسب بخصوم `DeductionTypes` النظامية وبالعطل العامة لا بجداول النسخة، وبلا بوابة الاستثناء. على العرض لا نسخة v2 منشورة (نسخة الفحص لا تُنشر)، فلا أثر. **قبل التراجع على خادم نشر نسخة v2:** لا تحسب تشغيلاً حتى العودة إلى هذا الإصدار.
  - **يختار «الإعداد النشط» بعلامة `IsActive` لا بالتاريخ:** نسخة v2 منشورة لتاريخ قادم تُعدّ نشطة عنده فوراً. لا واحدة على العرض.
  - **قواعد أولوية الصرف التي تخص نسخة مخزَّنة غير فعّالة،** فلا يخلطها الإصدار السابق بالقواعد العامة.
  - شاشات الإطار الجديدة وأزرار اعتماد المدخلات تختفي، والنماذج القديمة تعود (تكتب المدخل مباشرة بلا معتمد، وتعدّل علامة «إلزامي»)؛ لا مستخدم ولا إعداد يحتاج تراجعاً.
- **النسخة:** `/opt/hrm/backups/pre-plan54-20261123T170449Z` (700، الملفات 600): `pg_dumpall.sql` (35,538,324 بايت) و`env.backup` و`SHA256SUMS`، و17 سطر `CREATE DATABASE`.

**نشر 2026-11-23 (PLAN-54).**

| # | الوقت (UTC) | التغيير | التراجع |
|---|-------------|---------|---------|
| 1 | 16:01 | حذف الصور المحلية لإصدارَي `27cbf35db9cc` و`1ee455bf1a6f` (القرص 29 GB ← 41 GB؛ الحدّ 30 قبل البناء). تبقى في السجل المحلي | `docker pull 127.0.0.1:5050/hrmanagement/<image>:<tag>` |
| 2 | 17:04:49 | النسخة الاحتياطية أعلاه، متحقَّق منها | حذف المجلد حين لا يُحتاج |
| 3 | 17:19–17:56 | بناء واحد منفصل لـ`83b99786a420` (`setsid nohup`، وملف `.done`): الرفع 100 ث، البناء 2165 ث، الدفع 50 ث، 17 من 17 صورة؛ القرص 40 GB قبله، 17 GB في أدنى نقطة، 33 GB بعده | لا يلزم |
| 4 | 17:56:44–18:02:07 | نشر `83b99786a420` (السابق `5aa61b78e3a6`): السحب 5 ث، التشغيل 150، الإقلاع 64 (ومنه PAY054)، المطابقة 15، السلامة 22؛ غابت الخدمات نحو 3 دقائق على الأكثر. الدخان: 50 نجاحاً و0 فشل وتحذير واحد (فحص المنافذ من جهة العميل) | `scripts/vps/deploy.ps1 -Tag 5aa61b78e3a6` |
| 5 | 18:04–18:12 | ما تركته فحوص الخطة: نسخة `LAW_50_2004_CHK54` v1 معتمدة ومصادَق عليها (`hradmin`) وغير منشورة، سريانها 2026-04-01 داخل شهر مرحّل؛ وبدل يدوي مرفوض مؤرَّخ داخل 2026-04 | لا يلزم (سجل) |

- **فحوص الخادم لهذه الخطة:** الأربعة ناجحة — الهجرة (النسخ الثلاث v1 ببصمة الصورة نفسها، والسجل 17 صفاً بالبصمة نفسها، والمدخلات 800/500/80 كلها معتمدة)، والدورة بثلاثة أشخاص ورفض النشر بـD-PAY-13، والمدخل اليدوي، وعشر شاشات في Chromium حقيقي بمقاسين: سياسة واحدة وبلا مخالفة.
- **ملاحظة لمن يشغّل فحص المتصفح من جهاز المالك (لينكس):** حاوية التطوير `hrm-keycloak` تعيد التشغيل كل دقيقة تقريباً فتتغير واجهة شبكة تحت Chromium: `ERR_NETWORK_CHANGED`، وعلى HTTP/3 تعلق الصفحة حتى المهلة. الفحص يفتح صفحة واحدة بمقاس واحد في كل خطوة ويعيد الخطوة التي قطعتها شبكة الجهاز، ويشغّل Chromium بـ`--disable-quic`.

## حساب الموظف المصرفي ونسخ ملف الأجر (PLAN-55)

- **ما تغيّر لمستخدمي العرض:**
  - **الرواتب ← ملفات الأجر:** زر «الحسابات المصرفية» لكل موظف يفتح حساباته: الحالة (بانتظار التحقق، نافذ، مرفوض، محال)، ومن سجّل ومن قرّر بالاسم، والرقم **مقنّعاً** (آخر أربع خانات). وفي القائمة الجانبية «حسابات بانتظار التحقق».
  - **شخصان:** `e2e-payroll-processor` يسجّل الحساب (IBAN يُفحص بـ mod 97؛ لا يُقبل رقم محلي لأن قائمة المصارف `Payroll:Bank:Banks` فارغة على العرض)، و`e2e-payroll-approver` يتحقق منه بمرجع تأكيد المصرف، أو يرفضه بسبب، أو يحيل الحساب النافذ بسبب. المعالج لا يصل إلى التحقق (403)، و`hradmin` الذي يحمل السياستين في وضع العرض لا يتحقق من حساب سجّله هو (400). كل رفض برسالة عربية ولا يغيّر شيئاً.
  - **الرقم كاملاً يظهر في صفحة التحقق وحدها، للمعتمد**، ليطابقه مع كتاب المصرف، والصفحة لا تُخزَّن (`no-store`).
  - **صفحة التشغيل** فيها بطاقة «جاهزية الحسابات المصرفية»: عدد الأسطر بحساب مثبَّت، ومن لا حساب له بالاسم، ومن أُحيل حسابه بعد التثبيت. تشغيلات العرض القائمة كلها «بلا حساب» (425 من 425 في آخر تشغيل)، وهذا صحيح: لم يكن على العرض أي رقم.
  - **ملف أجر جديد عند تفعيل عقد** يحمل وحدة الموظف ونظامه، ويظهر التفعيل طلب تغيير راتب مطبَّقاً مصدره «تفعيل عقد».
  - **لا شيء آخر تغيّر:** لا حساب نافذ على العرض، ولا تشغيل ولا أمر دفع ولا حجز ولا ملف أجر قائم تغيّر (بصمتا ملفات الأجر الفعّالة الـ425 كما في الصورة قبل النشر).
- **ما تغيّر على الخادم:** الترحيل `PAY055_EmployeeBankAccounts` في `payroll_db`: جدول `EmployeeBankAccounts` بأمان الصفوف القسري وفهرس فريد مرشَّح (حساب نافذ واحد وحساب منتظر واحد لكل موظف وجهة)، وعمود `BankAccountId` اختياري على `SalaryLines` و`PayrollRunInputSnapshots`. الترحيل ينقل أي رقم كان على ملف أجر إلى حساب **بانتظار التحقق** (لا تحقق ضمنياً أبداً)؛ على العرض لم يُنقل شيء. أعمدة الملف الثلاثة القديمة باقية (للتراجع) ولا يقرؤها هذا الإصدار.
- **إصلاح الملفات القديمة** (ملفات أجر فعّالة بلا وحدة أو نظام، من إصدارات سابقة):
  ```powershell
  # تجريبي (الافتراضي): يسرد ولا يغيّر شيئاً، ويكتب تقرير JSON وقائمة CSV (قائمة التراجع: كل ملف بما كان فارغاً فيه)
  pwsh -NoProfile -File scripts/pay055-backfill-profile-orgunit.ps1 -GatewayBaseUrl https://<APP_DOMAIN> -CookieHeader "<جلسة hr-admin>"
  # التطبيق: يجري التجريبي أولاً ثم يملأ ما سرده هو وحده
  pwsh -NoProfile -File scripts/pay055-backfill-profile-orgunit.ps1 -GatewayBaseUrl https://<APP_DOMAIN> -CookieHeader "<جلسة hr-admin>" -Apply
  ```
  `password grant` مغلق في Production وDemo، فيأخذ السكربت ترويسة Cookie لجلسة مسجلة (`-CookieHeader` أو `HRM_REPAIR_COOKIE`) أو `-AccessToken` (`HRM_REPAIR_ACCESS_TOKEN`)، كسكربت PLAN-40. السياسة `Payroll.Repair` (المدير افتراضياً)، مركزي فقط، جهة المستدعي وحدها، ويحتاج رمز antiforgery يجلبه السكربت. يملأ الفارغ من آخر ملف للموظف نفسه يحمل القيمة، ولا يكتب فوق قيمة، ولا يخترع قيمة، ويُرفض (409) أثناء حساب جارٍ، ويكتب قيد تدقيق واحداً إن غيّر شيئاً، والتشغيل الثاني لا يجد شيئاً. **التراجع عنه:** قائمة CSV تسمّي كل ملف وما مُلئ فيه؛ تُفرَّغ تلك الأعمدة لتلك المعرّفات.
  - **على العرض (2026-11-24):** 425 ملفاً فعّالاً بلا نظام قانوني (ولا واحد بلا وحدة)، ولا يحمل أي ملف آخر للموظف نفسه نظاماً: **قابل للملء 0، غير قابل للملء 425، تغيّر 0**. النظام القانوني لهؤلاء قرار بيانات لا إصلاح (البذرة لم تكتبه)؛ التقريران وقائمتا التراجع (فارغتان) بجانب النسخة الاحتياطية.
- **ما تتركه فحوص الخادم (`AC_PAY_55_vps_*`):** في كل تشغيل حساب اختبار واحد **محال** (سُجّل، تحقق منه المعتمد، ثم أحاله) وحسابان **مرفوضان**، لثلاثة موظفين من العرض. أرقامها IBAN اختبارية (`SY` وخانتا تحقق محسوبتان ثم `CHK55`)، ولا يبقى حساب نافذ أو منتظر، ولم يثبّته تشغيل ولم يوضع حجز. الحسابات لا تُحذف (سجل).
- **التراجع إلى إصدار PLAN-54** (`scripts/vps/deploy.ps1 -Tag 014db7e3a01a`، صوره في السجل المحلي)، بلا SQL وبلا استعادة؛ قُرئت شيفرته بـ`git show` لمعرفة ما يفعله ببيانات هذه الخطة:
  - **لا يقرأ جدول الحسابات ولا العمودين الجديدين،** وسطر PAY055 الزائد في سجل الترحيلات لا يضره. أسطره ولقطاته الجديدة تأخذ `BankAccountId` فارغاً.
  - **يقرأ حقول الملف المصرفية القديمة من جديد** (أوامر الدفع، ملف الخزينة، FR-04، جواب DSAR)، وهي فارغة على العرض كما كانت. حساب سُجّل أو تُحقق منه في هذا الإصدار لا يراه: **قبل التراجع على خادم فيه حسابات نافذة** لا تُنشأ أوامر دفع ولا يُصدَّر ملف خزينة حتى العودة إلى هذا الإصدار (تخرج بلا وجهة).
  - **حجز الإحالة (`ACCOUNT-RETIRED`) حجز يدوي عادي عنده:** يبقى يمنع الدفعة حتى يُفرج عنه، ولا يعرف أن يمنع ملف الخزينة.
  - **تفعيل العقد يكتب عنده نسخة بلا وحدة ونظام من جديد** وبلا صف طلب؛ بعد العودة إلى هذا الإصدار يملؤها سكربت الإصلاح. وصفوف الطلبات التي مصدرها «تفعيل عقد» يعرفها (المصدر من PLAN-53).
  - شاشات الحسابات وبطاقة الجاهزية تختفي؛ لا مستخدم ولا إعداد يحتاج تراجعاً.
- **النسخة:** `/opt/hrm/backups/pre-plan55-20261124T215254Z` (700، الملفات 600): `pg_dumpall.sql` (38,262,947 بايت) و`env.backup` و`SHA256SUMS`، و17 سطر `CREATE DATABASE`؛ ومعها تقريرا الإصلاح وقائمتا التراجع.

**نشر 2026-11-24 و2026-11-25 (PLAN-55). الخادم يشغّل `c95179904e05`، وهو على `main` (PR #55، إيداع الدمج `a8f6ee32b890`).**

| # | الوقت (UTC) | التغيير | التراجع |
|---|-------------|---------|---------|
| 1 | 21:52:54 | النسخة الاحتياطية أعلاه، متحقَّق منها | حذف المجلد حين لا يُحتاج |
| 2 | 21:53 | حذف الصور المحلية لإصدارَي `5aa61b78e3a6` و`83b99786a420` (القرص 23 GB ← 35 GB؛ الحدّ 30 قبل البناء). تبقى في السجل المحلي | `docker pull 127.0.0.1:5050/hrmanagement/<image>:<tag>` |
| 3 | 21:54–22:31 | بناء واحد منفصل لـ`ea0d97a6ca26` (`setsid nohup`، وملف `.done`): الرفع 120 ث، البناء 2037 ث، الدفع 40 ث، 17 من 17 صورة؛ القرص 35 GB قبله، **8.8 GB في أدنى نقطة**، 27 GB بعده | لا يلزم |
| 4 | 22:32:02–22:35:47 | نشر `ea0d97a6ca26` (السابق `014db7e3a01a`): السحب 3 ث، التشغيل 131، الإقلاع 60 (ومنه PAY055)، المطابقة 13، السلامة 14؛ غابت الخدمات نحو دقيقتين ونصف على الأكثر. الدخان: 50 نجاحاً و0 فشل وتحذير واحد (فحص المنافذ من جهة العميل) | `scripts/vps/deploy.ps1 -Tag 014db7e3a01a` |
| 5 | 22:38 | إصلاح ملفات الأجر: التجريبي ثم التطبيق؛ لم يتغيّر شيء (0 قابل للملء) | لا يلزم |
| 6 | 22:39–22:52 | ما تركته فحوص الخطة: أربعة حسابات اختبار لثلاثة موظفين، واحد محال وثلاثة مرفوضة؛ لا حساب نافذ أو منتظر | لا يلزم (سجل) |
| 7 | 2026-11-25 02:03 | حذف الصور المحلية لإصدار `014db7e3a01a` (27 GB ← 33 GB). تبقى في السجل المحلي | `docker pull 127.0.0.1:5050/hrmanagement/<image>:014db7e3a01a` |
| 8 | 02:03–02:34 | بناء الإيداع النهائي `c95179904e05` (إصلاح CodeQL: اسم معامل، بلا تغيير سلوك): البناء 1826 ث، الدفع 21 ث، 17 من 17؛ القرص 8.8 GB في أدنى نقطة، 25 GB بعده | لا يلزم |
| 9 | 02:35:15–02:38:34 | نشر `c95179904e05` (السابق `ea0d97a6ca26`): السحب 5 ث، التشغيل 118، الإقلاع 56، المطابقة 12، السلامة 14؛ غابت الخدمات نحو دقيقتين. الدخان: 50 نجاحاً و0 فشل. بلا ترحيل | `scripts/vps/deploy.ps1 -Tag ea0d97a6ca26` (السلوك نفسه) |
| 10 | 02:38–02:44 | فحوص الخطة الخمسة من جديد على الإيداع النهائي: ناجحة؛ على العرض الآن سبعة حسابات اختبار لثلاثة موظفين (اثنان محالان وخمسة مرفوضة)، لا حساب نافذ أو منتظر | لا يلزم (سجل) |

- **فحوص الخادم لهذه الخطة:** الخمسة ناجحة — الهجرة (لا حساب منقول، والأعمدة القديمة باقية، وبصمتا ملفات الأجر كما في الصورة)، وحساب يسجّله شخص ويتحقق منه آخر ثم يُحال (ورقم بخانة تحقق خاطئة 400 بلا صف)، وحساب ثانٍ لا يتحقق منه مسجّله (400) ثم يُرفض، وتقرير جاهزية تشغيل قائم (425 بلا حساب، للقراءة فقط)، وسبع شاشات في Chromium حقيقي بمقاسين: سياسة واحدة وبلا مخالفة، والرقم مقنّع إلا في صفحة تحقق المعتمد.
- **ملاحظة قرص:** نزل القرص أثناء البناء إلى 8.8 GB (كان 17 في PLAN-54) مع أن 35 GB كانت حرة قبله؛ حدّ الـ30 قبل البناء لم يعد فيه هامش واسع. قبل البناء القادم تُحذف الصور المحلية لكل إصدار غير الجاري.

## آلة حالات تشغيل الرواتب: صافٍ واحد، رفض يعيد الفتح، إلغاء، وعكس معتمد (PLAN-56)

- **ما تغيّر لمستخدمي العرض:**
  - **صفحة التشغيل** فيها أفعال جديدة، كل فعل يظهر لمن يملكه وحده ويسأل قبل التنفيذ:
    - **رفض بملاحظة** (المعتمد، لتشغيل «قيد المراجعة»): يعود التشغيل مسودة بنسخة جديدة، وتُحفظ النسخة المرفوضة في جدول «النسخ المرفوضة» على الصفحة (عدد البنود والمجموع والملاحظة ومن رفض بالاسم)، فيُصحَّح ويُحسب من جديد.
    - **إلغاء بسبب** لأي تشغيل لم يُرحَّل: المعالج قبل الإرسال للمراجعة، والمعتمد بعده. تتحرر فترة التشغيل ومدخلاته (الخصوم والغياب والتسويات)، ويظهر على الصفحة من ألغاه ومتى ولماذا.
    - **إعادة بدء الحساب** لمهمة حساب فشلت أو أُوقفت: بلقطة جديدة.
    - **طلب عكس بسبب** لتشغيل مرحّل (حامل `Payroll.Reverse`: المرحِّل والعاكس و`hradmin` في وضع العرض). الطلب لا يعكس شيئاً.
  - **قائمة «طلبات العكس»** في القائمة الجانبية: يقرر فيها معتمد **ليس طالب العكس ولا من رحّل التشغيل**. الاعتماد يُبطل الدفعة التي لم تُرسل ويحرّر المدخلات ويجعل التشغيل معكوساً؛ والرفض يحتاج ملاحظة. **تشغيل خرجت دفعته إلى المصرف لا يُعكس** (D-PAY-16): يُرفض الاعتماد برسالة تدل على تشغيل التصحيح. تشغيلات العرض المرحّلة الأربعة دفعاتها مبذورة «مُعالَجة»، فطلب عكسها يُقبل طلباً ولا يُعتمد.
  - **القسيمة وتفاصيل التشغيل والتقارير والملفات** تُظهر **التسويات** بجانب الإجمالي والاستقطاعات، والصافي = الإجمالي − الاستقطاعات + التسويات. تشغيلات العرض بلا تسويات على بنودها، فالعمود صفر والأرقام كما كانت.
  - **نموذج التسوية** فيه «تصحيح لتشغيل مرحّل (اختياري)»: تسوية موجّهة إلى تشغيل مرحّل لا يأخذها إلا تشغيل تصحيحه («تصحيح رجعي» في صفحة التشغيل)، والفرق السالب يصير استقطاع «استرداد تصحيح» يأخذه أول تشغيل اعتيادي بعد ترحيل التصحيح.
  - **صفحة «إضافة سطر» اليدوية حُذفت:** البند يكتبه الحساب وحده.
  - **لا شيء آخر تغيّر:** لم تتغير حالة أي تشغيل على العرض ولا أي مبلغ (بصمة البنود الـ2550 كما في الصورة قبل النشر: `994e497f…`).
- **ما لا يعمل على العرض، كما كان قبل هذه الخطة:**
  - **العرض يرفض حساب تشغيل جديد** («425 موظفاً … بدون نظام مصنّف»: ملفات الأجر الـ425 بلا نظام قانوني، PLAN-55). التشغيل المرفوض حسابه يبقى «قيد الحساب» بمهمته المرفوضة، ويُلغى الآن بسبب أو يُعاد بدؤه.
  - **التشغيل «المعتمد» (2026-05) لا يُرحَّل:** لا سجل مراجعة مغلقاً يصل إليه أحد (G-PAY-05، PLAN-58)، ومكوّنات بنوده المبذورة لا تطابق أرقامها (مكوّن الاستقطاع كُتب موجباً في البذرة القديمة)، فيرفضه تحقق الترحيل برسالة تذكر السبب. لا يُصحَّح مبلغ مخزَّن؛ هذا التشغيل يُلغى ولا يُرحَّل.
- **ما تغيّر على الخادم:** ترحيلان في `payroll_db`:
  - `PAY056_NetFormula`: عمود `SalaryLines.TotalAdjustments` (0 افتراضياً)، وملؤه من مكوّنات التسوية لكل بند برفع `FORCE` عن الجدولين حول الملء وإعادته. على العرض لا مكوّن تسوية، فكل البنود صفر، ولم يتغير مبلغ.
  - `PAY056_RunLifecycle`: أعمدة التشغيل (`Revision` = 1، `PostedBy`، `PostedAt`، `CancelledBy`، `CancelledAt`، `CancellationReason`)، و`PayrollAdjustments.TargetSourceRunId`، وجدولا `PayrollRunRevisions` و`PayrollReversalRequests` بأمان الصفوف القسري، وفهارس فريدة مرشَّحة: تشغيل اعتيادي حي واحد للفترة (يُستثنى المعكوس والملغى)، وتصحيح مفتوح واحد لكل تشغيل مرجعي، وطلب عكس معلّق واحد لكل تشغيل.
- **ما تتركه فحوص الخادم (`AC_PAY_56_vps_*`):** في كل تشغيل لها تشغيلا رواتب في أيلول 2028 (شهر لا يغطيه تشغيل على العرض) ينتهيان **ملغيَين** بسببهما، وطلبا عكس على تشغيل كانون الثاني 2026 المرحّل ينتهيان **مرفوضَين** بملاحظتهما. لا يُرسَل اعتماد عكس على العرض أبداً، ولا يُلغى ولا يُرفض ولا يُعاد حساب تشغيل من تشغيلات العرض، وطلب عكس أنشأه مستخدم العرض لا تمسّه الفحوص (تميّز طلباتها بنص سببها). السجلات تبقى.
- **التراجع إلى إصدار PLAN-55** (`scripts/vps/deploy.ps1 -Tag c95179904e05`، صوره في السجل المحلي وتُسحب منه؛ والتراجع خطوة واحدة إلى البناء السابق من هذه الخطة نفسها `-Tag 69570ea4e1f6` لا يغيّر سلوكاً)، بلا SQL وبلا استعادة؛ قُرئت شيفرته بـ`git show` لمعرفة ما يفعله ببيانات هذه الخطة:
  - **لا يقرأ الأعمدة والجداول الجديدة،** وسطرا PAY056 الزائدان في سجل الترحيلات لا يضرّانه. حالة التشغيل «ملغى» (8) رقم يعرفه.
  - **لا يقرأ صفاً فيه اسم حالة لا يعرفه، ويفشل الاستعلام كله** (الحالات الثلاث الجديدة تُخزَّن **أسماءً** لا أرقاماً، ومحوّل EF يرمي عند اسم مجهول؛ `Pay056RollbackReadingTests`):
    - قرار `Cancelled` في `PayrollRunApprovals` (يكتبه كل إلغاء): **صفحة ذلك التشغيل الملغى تفشل (500)**، وكذلك أي أمر يحمّل التشغيل بقراراته. التشغيل ملغى فلا يُعمل عليه، لكن صفحته لا تُفتح.
    - دفعة `Voided` (يكتبها اعتماد عكس): **صفحة التشغيل المعكوس وقائمة «دفعات الصرف» للجهة كلها تفشلان.**
    - أمر دفع `Cancelled` (أوامر مسودة لدفعة أُبطلت): الاستعلامات التي تحمّله تفشل.
    - على العرض الآن: ثمانية تشغيلات فحص ملغاة (أيلول 2028) صفحاتها لن تُفتح بعد تراجع، ولا دفعة مبطلة ولا أمر ملغى.
    - **إذن: بعد أن يُلغي هذا الإصدار تشغيلاً أو يعتمد عكساً، طريق العودة هو الإصلاح إلى الأمام.** التراجع حالة ناقصة تدوم حتى يعود هذا الإصدار، لا تُفسد بيانات، وتُعرف الصفوف المعنية بقراءة فقط:
      ```sql
      select 'decision' k, "PayrollRunId" from "PayrollRunApprovals" where "Decision" = 'Cancelled'
      union all select 'batch', "PayrollRunId" from "PaymentBatches" where "Status" = 'Voided'
      union all select 'order', "PayrollRunId" from "PaymentOrders" where "Status" = 'Cancelled';
      ```
  - **تحققه يُفشل أي تسوية غير صفرية من جديد، وترحيله يعيد كتابة الصافي بلا التسويات، وزر العكس عنده يعكس مباشرة بلا طلب ويكتب دفعة عكس.** لذلك **لا يُرحَّل ولا يُلغى ولا يُعكس تشغيل والإصدار الأقدم يعمل.**
  - **محمّله لا يعرف `TargetSourceRunId`:** تسوية موجّهة حرة يأخذها أول تشغيل اعتيادي عنده (ثم يفشل تحققه بها). قبل تراجع على خادم فيه تسويات موجّهة غير مطبّقة لا يُحسب تشغيل.
  - إلغاؤه القديم لا يحرّر المدخلات، وليس عنده رفض يعيد الفتح ولا إعادة بدء.
  - `Down` الترحيل الثاني لا يُستعمل (يحذف النسخ المرفوضة وطلبات العكس، وقد يفشل في إعادة الفهرس القديم): التراجع بالوسم وحده.
- **النسخة:** `/opt/hrm/backups/pre-plan56-20261125T112742Z` (700، الملفات 600): `pg_dumpall.sql` (40,748,552 بايت) و`env.backup` و`SHA256SUMS`، و17 سطر `CREATE DATABASE`.

**نشر 2026-11-25 (PLAN-56). الخادم يشغّل `e64a0f7e65be`، وهو على `main` (PR #56، إيداع الدمج `01a8c3f96d9b`)؛ و`main` يزيد عليه إيداعاً واحداً (انظر آخر هذا القسم).**

| # | الوقت (UTC) | التغيير | التراجع |
|---|-------------|---------|---------|
| 1 | 11:27:42 | النسخة الاحتياطية أعلاه، متحقَّق منها | حذف المجلد حين لا يُحتاج |
| 2 | 11:28:28 | حذف الصور المحلية لإصدار `ea0d97a6ca26` (17 صورة؛ القرص 24 GB ← 30 GB؛ الحدّ 30 قبل البناء). تبقى في السجل المحلي (فُحصت قبل الحذف) | `docker pull 127.0.0.1:5050/hrmanagement/<image>:ea0d97a6ca26` |
| 3 | 11:28:52 | حذف ثلاث صور بلا وسم من بناءات سابقة (`docker image prune -f`، 1.145 GB؛ القرص 31 GB) | لا يلزم |
| 4 | 11:29:20–12:01:02 | بناء واحد منفصل لـ`69570ea4e1f6` (`setsid nohup`، وملف `.done`): الرفع 63 ث، البناء 1781 ث، الدفع 36 ث، 17 من 17 صورة؛ القرص 31 GB قبله، **8 GB في أدنى قراءة**، 24 GB بعده | لا يلزم |
| 5 | 12:01:45–12:05:55 | نشر `69570ea4e1f6` (السابق `c95179904e05`) من أول مرة: السحب 4 ث، التشغيل 136، الإقلاع 61 (ومنه ترحيلا PAY056)، المطابقة 13، السلامة 15؛ غابت الخدمات نحو دقيقتين ونصف على الأكثر. الدخان: 50 نجاحاً و0 فشل وتحذير واحد (فحص المنافذ من جهة العميل) | `scripts/vps/deploy.ps1 -Tag c95179904e05` (انظر ملاحظة التراجع أعلاه) |
| 6 | 12:06–12:29 | ما تركته فحوص الخطة: أربعة تشغيلات في أيلول 2028 ملغاة بسببها، وثلاثة طلبات عكس على تشغيل كانون الثاني 2026 مرفوضة بملاحظتها؛ لا تشغيل ولا بند ولا دفعة من العرض تغيّرت | لا يلزم (سجل) |
| 7 | 12:30–14:24 | ما تركته فحوص الخطط السابقة (PLAN-36..55) كما في كل تشغيل لها؛ 67 من 68 (الباقي يقرأ ملفاً من جهاز Windows المهيّأ) | لا يلزم (سجل) |
| 8 | 16:33–16:40 | **المنفذ 22 رفض جهاز التنفيذ نحو سبع دقائق** (HTTPS وping و443 تعمل، والعرض لم يتأثر) ثم عاد وحده: على الأرجح حدّ معدّل بعد مئات اتصالات ssh من فحوص الخادم ومراقبة البناء. منذئذ يُراقَب البناء من عملية واحدة على الخادم | لا يلزم |
| 9 | 16:41:16 | حذف الصور المحلية لإصدار `c95179904e05` (17 صورة، فُحصت في السجل المحلي أولاً): القرص 23 GB ← 29 GB. **بدأ البناء الثاني تحت حدّ الـ30 بـ0.8 GB** مع حارس على الخادم يوقف البناء برقم عمليته تحت 3000 MiB | `docker pull 127.0.0.1:5050/hrmanagement/<image>:c95179904e05` |
| 10 | 16:42–17:23:54 | بناء منفصل ثانٍ، لـ`e64a0f7e65be` (رأس الفرع بعد إعادة كتابة تاريخه المحلي قبل الدفع: نص اختبار واحد قرأه gitleaks مفتاحاً؛ شيفرة المنتج هي نفسها): الرفع 229 ث، البناء 2192 ث، الدفع 39 ث، 17 من 17. **نزل القرص إلى 3106 MiB، فوق خط الحارس بـ106 MiB** | لا يلزم |
| 11 | 17:36:33–17:43:54 | نشر `e64a0f7e65be` (السابق `69570ea4e1f6`). **المحاولة الأولى توقفت عند `keycloak-reconcile`** بعد إعادة إنشاء PostgreSQL (الحاويات أُنشئت ولم تبدأ)، فأُعيد النشر نفسه فوراً: سليم عند 17:42:52، السحب 5 ث، التشغيل 106، الإقلاع 56، المطابقة 13 (بلا تغيير)، السلامة 14. **غاب العرض نحو ست دقائق.** الدخان: 50 نجاحاً و0 فشل. بلا ترحيل | `scripts/vps/deploy.ps1 -Tag 69570ea4e1f6` (صوره المحلية باقية) |
| 12 | 17:45–19:15 | فحوص الخطة الخمسة على الإصدار المعاد نشره: ناجحة (فحص الشاشات نجح في إعادة منفردة؛ إعادةٌ قبلها قطعها حدّ زمني وضعته أنا فتركت تشغيل فحص مسودة وطلب عكس معلّقاً، ثم أغلقتهما الإعادة التالية). على العرض الآن: ثمانية تشغيلات فحص في أيلول 2028 كلها ملغاة، وسبعة طلبات عكس على تشغيل كانون الثاني 2026 كلها مرفوضة؛ تشغيلات العرض الستة كما كانت | لا يلزم (سجل) |

- **فحوص الخادم لهذه الخطة:** الخمسة ناجحة — الترحيلان (كل بند يحمل تسوياته، وبصمة البنود كما في الصورة، ولا بند يخالف صافيه الصيغة، والجداول الجديدة تحت أمان الصفوف القسري)، وتشغيل للفحص يُلغى بسبب (العرض رفض حسابه، فأوقف الإلغاء مهمته وبقيت المدخلات حرة وتحررت الفترة)، و`validate` على التشغيل المعتمد يُرفض (400) وحالته كما هي، وطلب عكس لا يقرر فيه طالبه (400) ولا المعالج (403) ويرفضه المعتمد بملاحظة، وسبع شاشات في Chromium حقيقي بمقاسين: سياسة واحدة وبلا مخالفة، والأشخاص بأسمائهم، والقسيمة تحمل التسويات وصافيها صافي الصيغة.
- **ملاحظة قرص، أهم من سابقتها:** البناء يأخذ نحو **26 GB في ذروته**، والحدّ 30 قبل البناء لم يعد فيه هامش: البناء الثاني بدأ بـ29 GB ونزل إلى 3.1 GB. السبب ليس صور الإصدارات (تُحذف المحلية منها قبل كل بناء) بل **حجم سجلات Seq: المجلد `hrm_seq_data` يحمل 46.6 GB ويكبر**. تحديد مدة الاحتفاظ بسجلات Seq قرار المالك؛ بدونه لن يتسع القرص لبناء قادم بعد إصدار أو اثنين.
- **الإصدار الجاري ناقص إيداعاً واحداً عن `main`:** إصلاح CodeQL على فرع الإصدار (`[ValidateAntiForgeryToken]` على `POST api/payroll/jobs/{id}/cancel`) جاء بعد البناء الثاني، **ولم يُبنَ الخادم مرة ثالثة لأن القرص لا يتسع** (28 GB بعد حذف الصور المسموح حذفها، والذروة 26). الخادم يشغّل `e64a0f7e65be`، وهو إيداع على `main`، فشرط الخطة التالية قائم، ونشرها يحمل الإصلاح. الفرق الوحيد على العرض: إلغاء المهمة القديم عبر الواجهة البرمجية لا يطلب رمز منع التزوير بعد؛ الأفعال الجديدة كلها تطلبه.

## فترة حضور مقفلة، تصحيح بفرق، وتسليم موثوق للرواتب (PLAN-57)

- **ما تغيّر لمستخدمي العرض:**
  - **«فترات الحضور»** في القائمة الجانبية: قائمة الأشهر بحالتها («مفتوحة»، «مغلقة جزئياً»، «مغلقة»)، وصفحة **«إغلاق فترة»**: الشهر والوحدة (المركزي يتركها فارغة فيغلق الجهة كلها). الإغلاق يكتب ملخصاً لكل موظف في شجرة الوحدة، ويقفل أيامه. إن كان لموظف يوم عمل بلا سجل يُرفض الإغلاق كله وتظهر قائمة الناقص، ولا يُكتب شيء.
  - **«تصحيحات الحضور»:** اليوم المقفل يعرض «طلب تصحيح» بدل «تعديل». الطلب بحالة جديدة وسبب (15 حرفاً على الأقل)، ويبتّ فيه **مركزي غير الطالب** (`hradmin` و`e2e-hr-admin` على العرض): الاعتماد يغيّر اليوم ويكتب نسخة تالية لملخص الموظف، والرفض يحتاج ملاحظة.
  - **تعديل سجل الحضور** يعرض الموظف والتاريخ للقراءة فقط، ولا يعرض «في إجازة» (تأتي من إجازة معتمدة وحدها). **الإدخال الجماعي** يملأ أيام العمل فقط حتى اليوم، ويقول كم يوماً كتب وكم تخطى.
  - **صفحة تشغيل الرواتب** فيها لوحة «الحضور»: وضع البوابة، ومن في نطاق التشغيل، ومن لم يُغلق حضوره لفترته بالاسم، والفروقات المرحَّلة. و**«مراجعة الرواتب»** تعرض تحذير البوابة بلا زر «إغلاق».
  - **لا شيء آخر تغيّر:** لم يتغير تشغيل ولا بند ولا مبلغ ولا سجل حضور قائم (بصمات الصورة قبل النشر كما هي: 17,850 سجل حضور `28881f67…`، 14 تشغيلاً `5e260f8e…`، 2,550 بنداً `a258ad24…`).
- **ما لا يعمل على العرض، كما كان قبل هذه الخطة:** العرض يرفض حساب تشغيل جديد (ملفات الأجر الـ425 بلا نظام قانوني، PLAN-55)، فلا يُرى تحذير البوابة على تشغيل محسوب جديد. قراءة `attendance-status` لتشغيل حزيران 2026 القائم تجيب `Warn` و425 في النطاق و425 بلا حضور مغلق (العرض لم يغلق شهراً من أشهر تشغيلاته).
- **ما تغيّر على الخادم:**
  - ترحيل في `hr_core_db` (`LVA057_AttendancePeriods`): جداول `AttendancePeriods` و`AttendancePeriodClosures` و`AttendanceEmployeeSummaries` و`AttendanceCorrections` بأمان الصفوف القسري، وأعمدة `Source` (0 افتراضياً) و`SourceReference` و`ReplacedStatus` على `DailyAttendanceRecords`، وتعليم سجلات الإجازة القديمة `Source = Leave` برفع `FORCE` حول الملء وإعادته. على العرض لا سجل صنعته إجازة، فلم يتغير سجل.
  - ترحيل في `payroll_db` (`LVA057_AttendanceHandoff`): جدول `AttendanceSummaryReceipts` بأمان الصفوف القسري، وأعمدة `OriginalPeriodStart/End` و`CarryForward` (false افتراضياً) و`SourceVersion` على `AbsenceDeductions`.
  - طابور جديد `payroll-web-attendance-summary-published` بمستهلكه؛ والطابور القديم `payroll-web-attendance-period-closed` باقٍ فارغاً بلا مستهلك (يُحذف يدوياً إن أُريد: `rabbitmqctl delete_queue payroll-web-attendance-period-closed`).
  - إعدادات جديدة بقيمها الافتراضية من ملفات الإنتاج: `Attendance:Periods:Mode=CalendarMonth`، `Attendance:Close:UnrecordedWorkingDays=RequireComplete`، `Attendance:Corrections:LeaveEventsAfterClose=AutoApply`، `Attendance:Calendar:WeeklyRestDays=Friday,Saturday`، `Attendance:Calendar:Holidays` فارغة، `Payroll:Attendance:Gate=Warn`. **العطل تُضبط بالبيئة** (`Attendance__Calendar__Holidays__0=2027-04-17` على `hr-core-web`) وقبل إغلاق شهرها؛ لا قائمة عطل في الشيفرة.
- **ما تركته فحوص الخادم (`AC_LVA_57_vps_*`):**
  - **إغلاق واحد حقيقي:** الوحدة `HR-ATT` (17 موظفاً) لشهر **آب 2026** (شهر ماضٍ لا يغطيه تشغيل على العرض ولا حضور مبذور فيه). مُلئت أيام عمله بالإدخال الجماعي «حاضر» (22 يوماً لكل موظف)، ثم أُغلق: 17 ملخصاً و17 إيصالاً في الرواتب، والفترة «مغلقة جزئياً» لأن باقي الوحدات مفتوحة. **يبقى مغلقاً: الفترة المغلقة لا يُعاد فتحها، بالتصميم**؛ تغيير يوم فيها يمر بطلب تصحيح.
  - **تصحيحان متعاكسان** في كل تشغيل للفحص على يوم 5 آب لموظف واحد: «غائب» ثم «حاضر»، فرقاهما في الرواتب +1 و−1 (صفّان حرّان بمرجع `ATT:<ملخص>:v<نسخة>` محصّلتهما صفر)، وترتفع نسخة ملخصه نسختين. وطلب ثالث يصنعه فحص الشاشات لصفحة القرار ويُرفض بملاحظته. لا طلب يبقى معلّقاً.
  - لا يُحسب ولا يُلغى ولا يتغير تشغيل من تشغيلات العرض، ولا يُكتب سجل حضور خارج تلك الوحدة وذلك الشهر.
- **التراجع إلى إصدار PLAN-56** (`scripts/vps/deploy.ps1 -Tag e64a0f7e65be`؛ صوره على GHCR منذ 2026-11-26، أُعيد بناؤها من الإيداع نفسه، وكانت قبله في السجل المحلي)، بلا SQL وبلا استعادة؛ قُرئت شيفرته بـ`git show` لمعرفة ما يفعله ببيانات هذه الخطة:
  - **لا يقرأ الجداول والأعمدة الجديدة،** وسطرا LVA057 الزائدان في سجلَّي الترحيلات لا يضرّانه. الأعمدة الجديدة لها قيم افتراضية أو تقبل الفراغ، فكتابته لسجل حضور أو صف غياب تنجح. التعدادات الجديدة مخزَّنة **أرقاماً** في جداول لا يقرؤها، فلا صف يكسر قراءته (بخلاف ما وجدته PLAN-56 في الأسماء النصية).
  - **لا قفل عنده:** شاشاته تكتب على أيام الفترة المغلقة، وتعديله يربط الموظف من جديد (ينقل السجل)، ويقبل «في إجازة» يدوياً، وإدخاله الجماعي يكتب الجمعة والسبت. ما يكتبه على يوم مقفل لا يغيّر الملخص ولا يصل الرواتب، ويطويه أول تصحيح لاحق لذلك الموظف بصمت.
  - **إغلاقه القديم ينشر الحدث القديم من جديد ومستهلكه القديم يعود:** يكتب صفوف غياب بسبب `AttendancePeriodClosed:` لا يعرفها التسليم الجديد، فإغلاق حقيقي لاحق للشهر نفسه **يخصم مرتين**. على العرض لا صف من هذا النوع اليوم (المخزون قبل النشر: صفر).
  - **قاعدة الغياب عنده تتخطى صف الاسترداد** (أيام سالبة) ولا تستهلكه، وتطبّق الصف الموجب إن وقعت فترته في فترة التشغيل: **يخصم أكثر مما يجب**. ومحمّله لا يأخذ صفاً مرحَّلاً إلى شهر لاحق.
  - **تحذير البوابة عنده سجل مراجعة عادي:** يظهر له زر «إغلاق»، وإغلاقه يكفي شرط الترحيل.
  - **إذن: لا إغلاق فترة ولا تصحيح ولا حساب تشغيل والإصدار الأقدم يعمل.** وبعد أن يكتب هذا الإصدار إغلاقاً أو تصحيحاً على بيانات حقيقية، طريق العودة هو الإصلاح إلى الأمام. على العرض الآن إغلاق الفحص وحده (وحدة واحدة، آب 2026) وصفّا فرق محصّلتهما صفر: التراجع لا يفسد شيئاً ما لم يُحسب تشغيل لآب 2026 تحته.
  - `Down` الترحيلين لا يُستعمل (يحذف الفترات والملخصات والتصحيحات والإيصالات): التراجع بالوسم وحده.
- **النسخة:** `/opt/hrm/backups/pre-plan57-20261126T025930Z` (700، الملفات 600): `pg_dumpall.sql` (44,611,604 بايت) و`env.backup` و`SHA256SUMS`، و17 سطر `CREATE DATABASE`.

**نشر 2026-11-26 (PLAN-57). الخادم يشغّل `4eccb1f269a0`: رأس فرع الخطة عند البناء، وهو سلف لإيداع الدمج في `main`؛ ما جاء بعده في الفرع وثائق وتقارير فقط.**

| # | الوقت (UTC) | التغيير | التراجع |
|---|-------------|---------|---------|
| 1 | 02:59:30 | النسخة الاحتياطية أعلاه، متحقَّق منها | حذف المجلد حين لا يُحتاج |
| 2 | 03:08:20 | **سياسة احتفاظ Seq: 30 يوماً** بإذن المالك، عبر واجهة Seq نفسها (`POST /api/retentionpolicies`، المعرّف `retentionpolicy-34`؛ لم تكن هناك سياسة). الأحجام: قبلها 20,727 MiB حراً و`hrm_seq_data` 47.4 GB، وبعدها 20,726 MiB و47.4 GB — **لم تحرّر شيئاً** لأن سجلات Seq تبدأ من 2026-10-31. من 2026-11-30 يحذف Seq ما تجاوز 30 يوماً | **حذف السجلات لا يُستعاد.** السياسة نفسها تُزال بـ`curl -X DELETE http://127.0.0.1:5380/api/retentionpolicies/retentionpolicy-34` |
| 3 | 03:09:23 | حذف الصور المحلية لإصدار `69570ea4e1f6` (17 صورة، فُحصت في السجل المحلي أولاً): 20,724 ← 26,521 MiB. بقي القرص تحت خط الـ28 GB للبناء، فتوقفت الخطة وسُئل المالك | (انظر السطر 4: لم تعد في السجل) |
| 4 | 04:15:53–04:16:42 | **مسح السجل المحلي للصور بإذن المالك («امسح وأكمل») وإعادة دفع الإصدار الجاري وحده:** أُوقف `hrm-registry`، أُفرغ مجلده، شُغّل، ودُفعت صور `e64a0f7e65be` السبع عشرة من جديد (17 من 17). **السجل 53,651 ← 1,913 MiB، والحر 26,486 ← 78,221 MiB.** خدمات العرض لم تتوقف | **لا يُستعاد:** وسوم 33 إصداراً أقدم (من PLAN-36 إلى أول بناء لـPLAN-56) ذهبت من الخادم؛ التراجع إلى أحدها إعادة بناء من git (`scripts/vps/build-on-host.ps1 -Tag <sha12>`) |
| 5 | 04:17:08–04:48:52 | بناء واحد منفصل لـ`4eccb1f269a0` (`setsid nohup` تحت غلاف واحد على الخادم يسجّل `df` ويوقف البناء برقم عمليته تحت 3000 MiB ويكتب ملف `.done`): الرفع 29 ث، البناء 1798 ث، الدفع 32 ث، 17 من 17 صورة. القرص 78,187 MiB قبله، **50,031 MiB في أدنى قراءة (ذروة نحو 28 GB)**، 70,601 MiB بعده | لا يلزم |
| 6 | 04:49:30–04:53:01 | نشر `4eccb1f269a0` (السابق `e64a0f7e65be`) من أول مرة؛ الطابور القديم فارغ قبله. السحب 6 ث، التشغيل 116، الإقلاع 54 (ومنه ترحيلا LVA057)، المطابقة 11، السلامة 15؛ غابت الخدمات نحو دقيقتين ونصف على الأكثر. الدخان: 50 نجاحاً و0 فشل وتحذير واحد (فحص المنافذ من جهة العميل) | `scripts/vps/deploy.ps1 -Tag e64a0f7e65be` بعد قراءة ملاحظة التراجع أعلاه |
| 7 | 04:53:50–05:12:58 | ما تركته فحوص الخطة: إغلاق آب 2026 للوحدة `HR-ATT` (17 ملخصاً و17 إيصالاً)، وتصحيحان متعاكسان على 5 آب (نسخة الملخص 3، وصفّا فرق +1 و−1)، وطلب تصحيح مرفوض لفحص الشاشات؛ لا تشغيل ولا بند ولا سجل حضور قائم تغيّر | لا يلزم (سجل)؛ الإغلاق لا يُعاد فتحه |
| 8 | 05:13–07:38 | ما تركته فحوص الخطط السابقة (PLAN-36..56) كما في كل تشغيل لها؛ 72 من 73 (الباقي يقرأ ملفاً من جهاز Windows المهيّأ)، ثم الدخان مع ربط التكامل: 50 نجاحاً و0 فشل | لا يلزم (سجل) |

- **فحوص الخادم لهذه الخطة:** الخمسة ناجحة — الترحيلان (الجداول الجديدة تحت أمان الصفوف القسري، ولا سجل صنعته إجازة كما عدّ المخزون، وبصمات الحضور والتشغيلات والبنود كما في الصورة، والطابور القديم بلا رسالة ولا مستهلك)، والإغلاق (ملخص لكل موظف وإيصاله، ورفض الكتابة اليدوية على يوم مقفل، والإغلاق المكرر لا يكتب شيئاً)، والتصحيحان (اعتماد الطالب لطلبه 400، ثم نسختان وفرقان محصّلتهما صفر)، والبوابة (قراءة فقط)، وعشر شاشات في Chromium بمقاسين تحت سياسة أمن محتوى واحدة مفروضة بلا مخالفة، عربية من اليمين إلى اليسار، بالأسماء لا بالمعرّفات.
- **ملاحظة قرص، تصحّح سابقتها:** أكبر ما كان يملأ القرص **السجل المحلي للصور** (`/opt/hrm/registry`، 53.7 GB: صور كل إصدار منذ PLAN-36، ولم يكن أحد قاسه)، ثم سجلات Seq (47.4 GB، نحو 1.7 GB في اليوم). بعد المسح صار الحر 77 GB، وكل بناء يضيف إلى السجل نحو 1.7 GB. قبل أي بناء: `du -sm /opt/hrm/*` و`docker system df -v`. **الانتقال إلى GHCR** (البناء في GitHub Actions والخادم يسحب) يزيل البناء على الخادم وذروته؛ نُفّذ في اليوم نفسه بعد دمج هذه الخطة (القسم التالي).

## صور الإصدارات على GHCR (2026-11-26)

- **ما تغيّر:** صور الإصدار السبع عشرة تبنيها GitHub Actions وتدفعها إلى `ghcr.io/omardefdev/hrmanagement`، والخادم يسحبها.
  **لا يُبنى شيء على الخادم بعد اليوم** (كان كل إصدار يُبنى عليه منذ PLAN-36: نحو 28 GB من القرص في ذروة البناء ونصف ساعة،
  وسجل حلقي بلغ 53.7 GB). الطريقة في §4، والنشر والتراجع في §6.
- **ما لم يتغيّر:** `scripts/vps/deploy.ps1` و`deploy/vps/remote-deploy.sh` وملفات compose كما هي؛ تغيّر `HRM_IMAGE_REPO` في
  `.env` الخادم وحده. لا ترحيل ولا تغيير بيانات. لمستخدمي العرض: لا شيء.
- **صورة الوسيط `hr-rabbitmq` أُصلحت في الطريق.** أول بناء على Actions شغّل فحص Trivy لأول مرة: 16 صورة من 17 مرّت،
  وفشلت `hr-rabbitmq` بـ78 ثغرة لها إصلاح (2 حرجة و76 عالية)، كلها في حزم Alpine في الصورة الأساس المثبّتة (openssl وexpat
  وmusl وpython). الأساس المثبّت هو آخر ما دُفع لـ`3.13-management-alpine` (2025-12-02)، فلا أساس أحدث يُثبَّت؛ اختار المالك
  إصلاح الصورة: `apk upgrade --no-cache` في `docker/rabbitmq/Dockerfile`، ضمن إصدار Alpine نفسه (3.22). RabbitMQ 3.13.7
  وErlang/OTP 26 ليسا حزم apk ولم يتغيّرا. البناء التالي مرّ كله (17 من 17، موقّعة).
- **أهداف التراجع على GHCR:** `0dd3564560b6` (الجاري) و`d6b6880ead87` (السابق، `-Rollback`)، و**`4eccb1f269a0` (PLAN-57)
  و`e64a0f7e65be` (PLAN-56) أُعيد بناؤهما هناك من إيداعيهما** (`-f source_sha=…`). صور هذين الأخيرين بناء جديد من المصدر
  نفسه (لا البايتات التي عملت على الخادم)، و`hr-rabbitmq` فيهما بالأساس غير المصلَّح وبلا توقيع (مهمتها حمراء عند Trivy، وهذا
  متوقع لإيداع سبق الإصلاح). الإصدارات الثلاثة والثلاثون الأقدم ليست على GHCR ولا على الخادم: تُبنى بـ`source_sha` عند الحاجة.
- **فشل عابر رأيناه:** مهمة بناء تفشل عند `apt-get update` والمرآة الأمنية لـUbuntu في منتصف مزامنة («File has unexpected
  size … Mirror sync in progress?»). لا شيء يُدفع من مهمة فشلت هناك. العلاج: `gh run rerun <run-id> --failed` (احتاج بناء
  `4eccb1f269a0` إعادتين).
- **فحوص الخادم بعد كل نشر من النشرين:** الدخان مع ربط التكامل 50 نجاحاً و0 فشل وتحذير واحد (فحص المنافذ من جهة العميل)،
  وفحوص PLAN-57 الخمسة (`AC_LVA_57_vps_*`) ناجحة. **قيمة `LVA057_VPS_RUNS_MD5` من صورة PLAN-57 لم تعد صالحة:** على العرض 16
  تشغيلاً منذ فحوص PLAN-56 صباح اليوم نفسه (آخرها أُنشئ 06:49:31Z) لا 14؛ القيمة المحسوبة من نسخة ما قبل النشر
  `7f4ee2cda600091300fc0c27fe28b357`، وبصمتا الحضور (`28881f67…`) والبنود (`a258ad24…`) كما هما. كل جولة من الفحوص تترك ما
  تتركه دائماً: تصحيحان متعاكسان على 5 آب (محصّلتهما صفر، ونسخة الملخص ترتفع نسختين) وطلب مرفوض لفحص الشاشات.
- **النسخة:** `/opt/hrm/backups/pre-ghcr-20261126T100045Z` (700، الملفات 600): `pg_dumpall.sql` (47,823,022 بايت، 17 سطر
  `CREATE DATABASE`) و`env.backup` (فيه القيمة القديمة لـ`HRM_IMAGE_REPO`) و`SHA256SUMS`.

| # | الوقت (UTC) | التغيير | التراجع |
|---|-------------|---------|---------|
| 1 | 09:44:54–09:55:47 | أول بناء على Actions (`action=build`، التشغيل 45794835086) للإيداع `d6b6880ead87`: 17 صورة دُفعت، 16 مهمة خضراء و`hr-rabbitmq` حمراء عند Trivy. الحزم السبع عشرة أُنشئت **عامة** (المستودع عام)، فتُقرأ بلا دخول | حذف الحزم أو وسومها من صفحة Packages في GitHub؛ لا يلزم |
| 2 | 10:00:45 | النسخة الاحتياطية أعلاه، متحقَّق منها | حذف المجلد حين لا يُحتاج |
| 3 | 10:01:12 | `HRM_IMAGE_REPO=ghcr.io/omardefdev/hrmanagement` في `/opt/hrm/shared/.env` (كان `127.0.0.1:5050/hrmanagement`؛ الملف 600 كما كان) | إعادة القيمة القديمة تصلح فقط مع سجل حلقي قائم فيه الصور (السطر 8)؛ الطريق العملي إبقاء GHCR |
| 4 | 10:01:23–10:07:16 | نشر `d6b6880ead87` من GHCR (السابق `4eccb1f269a0`). السحب 94 ث، التشغيل 126، الإقلاع 57، المطابقة 12، السلامة 14. الدخان 50 نجاحاً و0 فشل. شيفرة الخدمات هي شيفرة PLAN-57 نفسها | `scripts/vps/deploy.ps1 -Tag 4eccb1f269a0` (صوره على GHCR منذ السطر 6) |
| 5 | 10:08–10:31 | جولة أولى من فحوص PLAN-57 الخمسة: ناجحة (الأول أُعيد ببصمة التشغيلات الصحيحة، انظر أعلاه) | لا يلزم (سجل) |
| 6 | 10:37:53–11:16:06 | بناء هدفَي التراجع على GHCR بـ`source_sha`: `e64a0f7e65be` (التشغيل 45803146658، 17 من 17 دُفعت) و`4eccb1f269a0` (التشغيل 45803126021، 17 من 17 بعد إعادتين للمهام الفاشلة عند مرآة Ubuntu) | لا يلزم |
| 7 | 10:50:58–11:10:28 | إصلاح `hr-rabbitmq` ثم بناء `0dd3564560b6` (التشغيل 45805121954، **17 من 17 خضراء**) ونشره (السابق `d6b6880ead87`). السحب 111 ث، التشغيل 131، الإقلاع 60، المطابقة 13، السلامة 15. الدخان 50 نجاحاً و0 فشل، ثم الجولة الثانية من فحوص PLAN-57: الخمسة ناجحة (11:11–11:36). حاوية الوسيط أُعيد إنشاؤها بالصورة المصلَّحة؛ volume بياناته كما هو | `scripts/vps/deploy.ps1 -Rollback` (يعود إلى `d6b6880ead87` بصورة الوسيط غير المصلَّحة) |
| 8 | 11:37:08 | **أُزيل السجل الحلقي بإذن المالك** («نعم، احذفه»): `docker rm -f hrm-registry` و`sudo rm -rf /opt/hrm/registry` (كان فيه وسما `4eccb1f269a0` و`e64a0f7e65be` لسبع عشرة صورة، 3,649 MiB). الحر 58,579 ← 62,254 MiB. لا شيء يستمع على 5050. الخدمات لم تتوقف (21 حاوية، لا واحدة غير سليمة) | **لا يُستعاد.** سجل فارغ من جديد: `host-setup.sh --with-registry` (§4، «الطرق السابقة») |

- **ما بقي على الخادم ولم يُمسّ:** الصور المحلية للإصدارين `4eccb1f269a0` و`e64a0f7e65be` بأسمائها القديمة
  (`127.0.0.1:5050/hrmanagement/<image>:<tag>`، 34 وسماً)، وصور `d6b6880ead87`، وصورة `registry:2.8.3`. كلها تُحذف بلا ضرر
  (`docker rmi`؛ الإصدارات الأربعة على GHCR): `docker system df` يعدّ 18 GB قابلة للاسترداد من 27 GB. ودخول `ghcr.io` المحفوظ في
  `~hrm/.docker/config.json` منذ PLAN-36 لم يُستعمل ولم يُمسّ؛ يزيله المالك بـ`docker logout ghcr.io` إن أراد.
- **قبل أي نشر:** القرص لم يعد قيداً على البناء. يبقى ما يملؤه سجلات Seq (47 GB، سياسة 30 يوماً منذ PLAN-57) والصور المحلية
  للإصدارات السابقة: `du -sm /opt/hrm/*` و`docker system df -v`.

- **ملاحظة قرص، تصحّح سابقتها:** أكبر ما كان يملأ القرص **السجل المحلي للصور** (`/opt/hrm/registry`، 53.7 GB: صور كل إصدار منذ PLAN-36، ولم يكن أحد قاسه)، ثم سجلات Seq (47.4 GB، نحو 1.7 GB في اليوم). بعد المسح صار الحر 77 GB، وكل بناء يضيف إلى السجل نحو 1.7 GB. قبل أي بناء: `du -sm /opt/hrm/*` و`docker system df -v`. **الانتقال إلى GHCR** (المسار الافتراضي في §4: البناء في GitHub Actions والخادم يسحب) يزيل البناء على الخادم وذروته، وهو تغيير مستقل بعد دمج هذه الخطة.

## حوكمة الصرف: مسار واحد للمال، أربع عيون حقيقية، وحجز لكل موظف (PLAN-58)

- **ما تغيّر لمستخدمي العرض:**
  - **«طلبات بانتظار قرارك»** في القائمة: كل طلب أربع عيون (الإفراج عن حجز، حل تنبيه حرج، تعديل متأخرات، إعادة إصدار ملف مصرفي،
    تسجيل تسوية دفعة أو رفضها) يقدّمه شخص ويقرّره آخر من جلسته؛ لا يرى الطالب طلبه في صندوقه. الطلب ينتهي بعد 72 ساعة بلا أثر.
  - **صفحة الدفعة:** ملفاتها المصرفية (ملف لكل إرسال بصف تحكم بالعدد والمجموع، تُنزَّل كما خُزّنت)، وبطاقة «المحجوزون» بالاسم والسبب
    والمبلغ وطلب الإفراج المعلّق، وأفعال التأكيد: «تسجيل إشعار القناة» للمُرحِّل، و«طلب تسوية» و«طلب رفض» يقررهما معتمد.
  - **التمويل الجزئي قرار على الدفعة بعد الترحيل** (معتمد الرواتب)، والإرسال بيد المُرحِّل وحده (`process`، ثم `submit-held` لما
    أُفرج عنه). أُزيل «صرف جزئي» على التشغيل، وتصدير «ملف الخزينة» وحزمته من التقارير.
  - **صفحة التشغيل:** «اعتماد 1 من 2» لتشغيل فوق العتبة (`Payroll:Approval:DualApprovalThreshold`، 50,000,000)، وسجل المراجعة
    المستقلة: يفتحه مدقق (`auditor`) ويغلقه مدقق آخر عبر البوابة، ولا يُرحَّل التشغيل قبل ذلك.
  - **الإفراج عن حجز وحل تنبيه حرج طلبان** لا فعلان من شخص واحد.
  - **`hradmin` يُرفض في أفعال الرواتب المالية** (المعالجة والاعتماد والترحيل والعكس والمراجعة) برسالة تسمّي التعارض: في وضع العرض
    يحمل كل سياسات الرواتب، ورمز يحمل دورين متعارضين لا يمر. يعمل بدله أشخاص الرواتب (`e2e-payroll-processor`، `e2e-payroll-approver`).
  - **لا شيء آخر يتحرك:** لا تشغيل ولا بند ولا دفعة ولا أمر ولا مبلغ يتغير بالنشر (الهجرة إضافية: أعمدة بقيم افتراضية وجدولان جديدان).
- **ما يحتاجه العرض لترحيل تشغيل** (لا تضيفه فحوص الخطة): مُرحِّل (`payroll-poster`)، ومعتمد ثانٍ فوق العتبة، ومدققان. ولا يُحسب على
  العرض تشغيل: كل موظفيه الـ425 بلا نظام خدمة مصنّف، فحساب جديد يحجزهم جميعاً `REGIME-BLOCKER` (`Payroll:Blockers:Mode=HoldEmployee`)؛
  مهمة بيانات: تصنيف أنظمة الخدمة لموظفي العرض.
- **ما تغيّر على الخادم:** ترحيل في `payroll_db` (`PAY058_DisbursementGovernance`): جدولا `PaymentBatchFiles` و`PayrollDualControlRequests`
  بأمان الصفوف القسري، وأعمدة `PaymentOrders.HoldReason`/`PaymentBatchFileId`، و`PaymentBatches.ArrearsAmount`/`Source` (0 افتراضياً)،
  و`PayrollRunApprovals.Revision` (1 افتراضياً)، وعمود `xmin` النظامي على `DeductionRecords` و`PayrollAdjustments` (لا عمود يُضاف فعلاً).
  إعدادات جديدة بقيمها الافتراضية: `Payroll:Approval:DualApprovalThreshold=50000000`، `Payroll:Audit:Required=true`،
  `Payroll:Audit:ReviewerRoles=auditor`، `Payroll:Blockers:Mode=HoldEmployee`، `Payroll:Sod:RejectConflictingRoleHolders=true`،
  `Payroll:DualControl:ExpiryHours=72`.
- **التراجع إلى الإصدار السابق** (بالوسم وحده، بلا SQL وبلا استعادة؛ قُرئت شيفرته لمعرفة ما يفعله ببيانات هذه الخطة):
  - **يتجاهل الجدولين الجديدين والأعمدة الجديدة** (لها قيم افتراضية أو تقبل الفراغ، فكتابته لا تتعطل).
  - **لا يقرأ أمراً مخزَّناً `Held`** (اسم لا يعرفه تعداده): وهذا يُكتب **عند أول ترحيل** لكل سطر بلا حساب مثبَّت، وعند أول إرسال لكل
    موظف محجوز. ولا يقرأ قيد متأخرات `Voided` (يكتبه عكس تشغيل دفعته الممولة لم تخرج). صفحة دفعة أو متأخرات فيها مثل ذلك تفشل عنده.
  - **يعيد ما أزالته الخطة:** الصرف الجزئي على التشغيل، وملف الخزينة، والمعتمد الذي يرسله العميل في جسم الإفراج.
  - **إذن: لا ترحيل ولا تمويل ولا إرسال ولا عكس والإصدار الأقدم يعمل.** وبعد أن يوجد أمر `Held` أو قيد `Voided` على بيانات حقيقية،
    فطريق العودة إصلاح إلى الأمام لا تراجع. و`Down` الهجرة لا يُستعمل.
- **النسخة:** `/opt/hrm/backups/pre-plan58-20261126T201439Z` (700، الملفات 600): `pg_dumpall.sql` (48,351,999 بايت، 17 سطر
  `CREATE DATABASE`) و`env.backup` و`SHA256SUMS`، متحقَّق منها.

**نشر 2026-11-26 (PLAN-58). الخادم يشغّل `25be64cf9fa8`: رأس `release/plan-58` (فرع الخطة مدموجاً فوق `main` بعد PR #58)، بُنيت صوره
السبع عشرة على GitHub Actions (§4) وسُحبت من GHCR. ما جاء بعده في الفرع وثائق وتقارير فقط.**

| # | الوقت (UTC) | التغيير | التراجع |
|---|-------------|---------|---------|
| 1 | 18:38:30 | **دمج PR #58** (صور الإصدار على GHCR) بأمر المالك («ادمجه واكمل»)، وكل فحوصه الخمسة عشر خضراء؛ ثم دمج `main` في فرع الخطة | لا يلزم (`main`) |
| 2 | 20:13:52–20:24 | بناء `25be64cf9fa8` على Actions (`action=build`، التشغيل 45889506130): 17 من 17 دُفعت وخضراء | لا يلزم |
| 3 | 20:14:39 | النسخة الاحتياطية أعلاه، متحقَّق منها، ثم صورة القراءة (كصورة P0 تماماً: 16 تشغيلاً، 2,550 بنداً، 6 دفعات، لا أوامر، 15 قيد متأخرات) | حذف المجلد حين لا يُحتاج |
| 4 | 20:25:30–20:31:19 | نشر `25be64cf9fa8` (السابق `0dd3564560b6`) من أول مرة: السحب 86 ث، التشغيل 123، الإقلاع 54 (ومنه ترحيل PAY058)، المطابقة 12، السلامة 18؛ الدخان 50 نجاحاً و0 فشل وتحذير واحد (فحص المنافذ من جهة العميل) | `scripts/vps/deploy.ps1 -Rollback` (يعيد `0dd3564560b6` من GHCR)، وانظر ملاحظة التراجع أعلاه |
| 5 | 20:33–20:44 | فحوص الخطة الخمسة (`AC_PAY_58_vps_*`) ناجحة. تركت **طلب أربع عيون واحداً مرفوضاً** على قيد متأخرات قائم (طلبه المعالج بمبالغ القيد نفسها، ورفضه المعتمد بملاحظة؛ القيد كما هو) | لا يلزم (سجل) |
| 6 | 20:44:51–23:33:46 | فحوص الخطط السابقة على الخادم (`AcOps36VpsBootTests` و`AcInt37VpsServerTests` بلا `AC_PAY_58_vps` ولا `AC_OPS_36j`): **72 من 78 ناجحة**. أربعة من فحوص PLAN-54/55/56 كُتبت ليعمل `hradmin` في أفعال الرواتب، وهذا ما ترفضه الخطة الآن (D-PAY-20)، فعُدّلت لتثبت الرفض وتبقي باقي معناها (`64dfffff`، `4a6f21ae`)؛ و`AC_POL_47` لسببه المحلي المسجّل منذ PLAN-53 (يقرأ `artifacts/plan47/vps/fingerprint-before.json` الذي ذهب مع جهاز Windows)؛ و`AC_WF_46` يقرأ آخر 5,000 سطر من سجل `idcard-web`، والسجل زاد 27,267 سطراً في 40 دقيقة تحت حركة الفحوص، فخرج منه سطر الكنس | لا يلزم |
| 7 | 23:35–23:59 | إعادة الفحوص الخمسة: PAY-55 وPAY-56 (العكس والشاشات) ناجحة من أول مرة؛ PAY-54 (الدورة) وجد استعلاماً يصير `NULL` حين لا مصادقة (`\|\|` مع `NULL`)، فأُصلح بـ`coalesce` ونجح؛ `AC_WF_46` نجح بعد دورة كنس (23:58:01) | لا يلزم |
| 8 | 23:59:41 | الدخان `-RequireIntegrationWiring`: 50 نجاحاً و0 فشل وتحذير واحد (فحص المنافذ من جهة العميل)؛ الخادم على `25be64cf9fa8`، 54 G حرة | لا يلزم |

**ما تركته فحوص الخطط السابقة** (ما تتركه في كل نشر، لا جديد): ثلاثة تشغيلات للفحص في 2028-09 ملغاة (`Pay56FirstMonth`)، وحساب أحدها حجز الموظفين الـ425 `REGIME-BLOCKER` على ذلك التشغيل الملغى: تظهر في صفحة الحجوزات `Active`، ولا تحجز شيئاً (البوابة لا تقرأ إلا حجز تشغيل الدفعة أو حجزاً بلا تشغيل)؛ ونسخ إطار برمز الفحص `LAW_50_2004_CHK54` معتمدة غير منشورة ولا مفعّلة (لا مصادق على العرض غير `hradmin`)، وطلبات مرفوضة. لا ترحيل ولا تمويل ولا إرسال ولا تسوية، ولا مستخدم أُضيف.
