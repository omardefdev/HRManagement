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
- الصور تُبنى على جهاز المالك وتُدفع إلى سجل، والخادم يسحبها بوسم `<sha12>` فقط. لا شيفرة ولا SDK على الخادم.
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

- **GHCR خاص (الافتراضي):** جهاز المالك يدفع بـ `gh auth token | docker login ghcr.io -u <owner> --password-stdin`
  (يحتاج نطاق `write:packages`). الخادم يسحب برمز **classic** للقراءة فقط (`read:packages`)، يُلصق في طرفية المالك:
  `ssh -t hrm-vps "docker login ghcr.io -u <owner>"`.
- **البديل (P-10):** سجل على الخادم مربوط بـ `127.0.0.1:5050` خارج مشروع التطبيق:
  `ssh hrm-vps "sudo bash /opt/hrm/current/deploy/vps/host-setup.sh --with-registry"`، ثم الدفع عبر نفق
  `ssh -N -L 5050:127.0.0.1:5050 hrm-vps` و`HRM_IMAGE_REPO=127.0.0.1:5050/hrmanagement`.

البناء والدفع (من جهاز المالك، الشجرة نظيفة):

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/vps/build-push-images.ps1 -Registry ghcr.io/<owner>/hrmanagement
```

الناتج: `artifacts/vps/images-<tag>.json` (الـ digest والحجم وزمن الدفع لكل صورة). وبعد دفع انقطع:
`build-push-images.ps1 -Registry ... -Tag <sha12> -SkipBuild` يدفع الصور المبنية دون إعادة البناء، مع إعادة المحاولة.

### البناء على الخادم (ما استُخدم في PLAN-36)

اتصال المالك يرفع بنحو 0.16 MB/s فقط، والصور نحو 810 MB من الطبقات الفريدة، وفشل الدفع إلى GHCR على الشبكة. لذلك قرر
المالك (انحراف موثق عن §6 القاعدة 1) أن يبني الخادم الصور من أرشيف المصدر (~30 MB) للوسم الملتزم:

```bash
ssh hrm-vps "sudo bash /opt/hrm/current/deploy/vps/host-setup.sh --with-registry"   # مرة واحدة: سجل 127.0.0.1:5050
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/vps/build-on-host.ps1   # أرشيف HEAD -> بناء -> دفع -> تنظيف
```

`deploy/vps/build-on-host.sh` يفك الأرشيف، ويولّد طبقة الاستعادة، ويبني الصور الـ15 على دفعات، ويدفعها إلى
`127.0.0.1:5050/hrmanagement`، ثم يحذف المصدر وذاكرة البناء وصورة SDK. النشر يبقى سحباً من سجل
(`HRM_IMAGE_REPO=127.0.0.1:5050/hrmanagement` في `.env`). الأزمنة: رفع ~380 s، بناء ~530 s، دفع ~10 s.

نفق SSH إلى السجل (`ssh -N -L 5050:...`) لا يصل إليه محرك Docker Desktop (connection refused)، فلا يصلح للدفع من Windows.

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
| السجل الحلقي `hrm-registry` (127.0.0.1:5050، `/opt/hrm/registry`) | `docker rm -f hrm-registry && sudo rm -rf /opt/hrm/registry` |
| `/opt/hrm/build` (تقارير البناء على الخادم) | `rm -rf /opt/hrm/build` |
| `tcpdump` (لتشخيص فحص المنافذ) | `sudo apt-get purge tcpdump` |
| مشروع compose `hrm` | `docker compose -p hrm ... down` (والـ volumes بـ `-v`) |
| `docker login ghcr.io` على الخادم | `docker logout ghcr.io` (يحذف `~hrm/.docker/config.json`) |
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
- **تقرير القواعد التي يرفضها المدقق:** `docker logs hrm-hr-core-web 2>&1 | grep 'PLAN-47 validator inventory'`. المتوقع حتى PLAN-49: `R-HIRE-START` و`R-SEP-START` و`R-TKT` و`R-TRF` (تطلق مسارات سحبها PLAN-46) و`R-EST-SUBMIT` (مسار غير مربوط بالمجال). لا يوقف التقرير الإقلاع، ولا تُصلَح القواعد في مكانها: الإصلاح مجموعة تغيير جديدة.
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
