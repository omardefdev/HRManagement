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
`DEMO_USERS_PASSWORD` عن 16 حرفاً أو `GATEWAY_CALLER_PROOF_SECRET`/`HR_CORE_OTP_HMAC_KEY` عن 32 — دون طباعة أي قيمة.
ولا تُكتب هذه الخطوة في مولّد ملتزم؛ المولّد الملتزم ← PLAN-38.

**إضافة مفتاح لخادم قائم (PLAN-37):** كل مفتاح `SVC_*` في `deploy/wiring/service-wiring.json` (حقل `secretEnv`) مطلوب. لإضافة
الناقص فقط، على الخادم ودون طباعة:

```bash
f=/opt/hrm/shared/.env
for k in SVC_AUTH_DIRECTORY_CLIENT_SECRET; do
  grep -q "^$k=" "$f" || printf '%s=%s\n' "$k" "$(openssl rand -hex 24)" >> "$f"
done
chmod 600 "$f"; cut -d= -f1 "$f" | grep -E '^SVC_'
```

## 5أ. عملاء الخدمات في Keycloak (PLAN-37)

realm الخادم لا يُحذف ولا يُعاد استيراده (المستخدمون التجريبيون والبيانات يعتمدون عليه). `deploy/vps/keycloak-sync-clients.sh`
يجعله مطابقاً للمصفوفة **بلا أثر جانبي عند التكرار**: كل نطاق بمُعيّن audience لكل جمهور، وكل عميل خدمة (سرّي، حساب خدمة، بلا
تدفق متصفح) بنطاقاته الاختيارية كما في المصفوفة بالضبط، وسرّه من متغير `secretEnv` في بيئة حاوية Keycloak (يملؤها compose من
`.env`). كل أوامر `kcadm.sh` تجري داخل الحاوية، فلا يمر سر في سطر أوامر على المضيف ولا يُطبع؛ المخرجات أسماء العملاء والإجراء
و`changes=N`.

- `remote-deploy.sh deploy` يشغّله بعد أن تصبح الحاويات سليمة؛ فشله يُفشل النشر (`failed-tag` ثم `-Rollback`).
- يدوياً: `ssh hrm-vps "cd /opt/hrm/current && bash deploy/vps/keycloak-sync-clients.sh [--check]"` (`--check` لا يغيّر شيئاً
  ويخرج بـ 1 إن كان تغيير مستحقاً).
- realm خادم جديد يأخذ العملاء نفسها عند أول استيراد (`prepare-keycloak-import.ps1 -Profile VpsDemo` يقرأ المصفوفة).

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

## 11. التسليم للمالك

- الرابط: `https://hr.161-97-185-71.sslip.io/` (Keycloak: `https://sso.161-97-185-71.sslip.io/`)، بشهادة Let's
  Encrypt حقيقية (production، تُجدَّد تلقائياً داخل Caddy).
- كلمة مرور المستخدمين التجريبيين يقرؤها **المالك وحده**:
  `ssh hrm-vps "grep ^DEMO_USERS_PASSWORD= /opt/hrm/shared/.env"`
  المستخدمون: `hradmin` (مدير الموارد البشرية) ومستخدمو `e2e-*` في realm العرض، كلهم بهذه الكلمة.
- Seq ولوحة Keycloak: §7. النشر والتراجع: §6.
- **عطّل VNC في لوحة Contabo** حين لا تحتاجه.
