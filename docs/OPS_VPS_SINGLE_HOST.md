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
