#!/usr/bin/env bash
# PLAN-36 (section 6): prepares one Ubuntu server (24.04/26.04) for the single-host chain. Run as root:
#   bash host-setup.sh                   base setup (idempotent; every step says what it changed)
#   bash host-setup.sh --harden-ssh      key-only SSH, no root login (run only after the hrm user is verified)
#   bash host-setup.sh --undo-harden-ssh restore the sshd drop-ins from the last backup
#   bash host-setup.sh --with-registry   also the loopback-only fallback registry (127.0.0.1:5050, P-10)
#
# Base setup: Docker Engine + Compose plugin from Docker's apt repository; 8 GiB swap and vm.swappiness=10; Docker log
# rotation; ufw allowing only 22/tcp, 443/tcp, 443/udp (the SSH rule before enabling); unattended-upgrades without
# automatic reboot; UTC; the hrm user (no password, docker group, NOPASSWD sudo, the owner's key copied from root);
# /opt/hrm/{releases,shared} owned by hrm, mode 750. Undo commands: docs/OPS_VPS_SINGLE_HOST.md, "Server changes".
set -euo pipefail

[ "$(id -u)" -eq 0 ] || { echo "run as root" >&2; exit 1; }
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
say() { printf '[host-setup] %s\n' "$*"; }
changed() { printf '[host-setup] CHANGED: %s\n' "$*"; }
unchanged() { printf '[host-setup] unchanged: %s\n' "$*"; }

HARDEN_FILE=/etc/ssh/sshd_config.d/00-hrm-hardening.conf

docker_engine() {
  if command -v docker >/dev/null 2>&1 && docker compose version >/dev/null 2>&1; then
    unchanged "docker $(docker version --format '{{.Server.Version}}' 2>/dev/null || echo present), compose $(docker compose version --short)"
    return
  fi
  export DEBIAN_FRONTEND=noninteractive
  apt-get update -q
  apt-get install -y -q ca-certificates curl
  install -m 0755 -d /etc/apt/keyrings
  curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
  chmod a+r /etc/apt/keyrings/docker.asc
  . /etc/os-release
  echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu ${VERSION_CODENAME} stable" \
    > /etc/apt/sources.list.d/docker.list
  apt-get update -q
  apt-get install -y -q docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
  systemctl enable --now docker
  changed "installed Docker Engine $(docker version --format '{{.Server.Version}}') and Compose $(docker compose version --short) from download.docker.com (${VERSION_CODENAME})"
}

docker_daemon_json() {
  local want='{
  "log-driver": "json-file",
  "log-opts": { "max-size": "10m", "max-file": "3" }
}'
  if [ -f /etc/docker/daemon.json ] && [ "$(cat /etc/docker/daemon.json)" = "$want" ]; then
    unchanged "/etc/docker/daemon.json (log rotation 10m x 3)"; return
  fi
  mkdir -p /etc/docker
  [ -f /etc/docker/daemon.json ] && cp /etc/docker/daemon.json "/etc/docker/daemon.json.bak-$STAMP"
  printf '%s\n' "$want" > /etc/docker/daemon.json
  systemctl restart docker
  changed "/etc/docker/daemon.json: json-file log rotation max-size=10m max-file=3 (docker restarted)"
}

swap() {
  if swapon --show=NAME --noheadings | grep -qx /swapfile; then
    unchanged "swap /swapfile ($(swapon --show=SIZE --noheadings | head -1))"
  else
    if [ ! -f /swapfile ]; then
      fallocate -l 8G /swapfile || dd if=/dev/zero of=/swapfile bs=1M count=8192 status=none
      chmod 600 /swapfile
      mkswap /swapfile >/dev/null
    fi
    swapon /swapfile
    changed "swap /swapfile 8 GiB on"
  fi
  if ! grep -qE '^/swapfile\s' /etc/fstab; then
    echo '/swapfile none swap sw 0 0' >> /etc/fstab
    changed "/etc/fstab: /swapfile"
  fi
  if [ "$(cat /etc/sysctl.d/90-hrm-swappiness.conf 2>/dev/null)" != "vm.swappiness=10" ]; then
    echo 'vm.swappiness=10' > /etc/sysctl.d/90-hrm-swappiness.conf
    sysctl -q -p /etc/sysctl.d/90-hrm-swappiness.conf
    changed "vm.swappiness=10 (/etc/sysctl.d/90-hrm-swappiness.conf)"
  else
    unchanged "vm.swappiness=10"
  fi
}

firewall() {
  command -v ufw >/dev/null 2>&1 || { apt-get install -y -q ufw; changed "installed ufw"; }
  local before
  before="$(ufw status verbose 2>/dev/null || true)"
  ufw default deny incoming >/dev/null
  ufw default allow outgoing >/dev/null
  # SSH first, so enabling can never cut the current session (sshd's real port; 22 on a stock image).
  local ssh_port
  ssh_port="$(sshd -T 2>/dev/null | awk '/^port /{print $2; exit}')"
  ufw allow "${ssh_port:-22}/tcp" >/dev/null
  ufw allow 443/tcp >/dev/null
  ufw allow 443/udp >/dev/null
  ufw --force enable >/dev/null
  if [ "$before" = "$(ufw status verbose)" ]; then unchanged "ufw (deny incoming; 22/tcp, 443/tcp, 443/udp)"; else changed "ufw: deny incoming, allow 22/tcp 443/tcp 443/udp, enabled"; fi
  # Docker publishes ports with its own iptables rules (ufw does not see them): the compose chain publishes only the
  # edge's 443 and Seq on 127.0.0.1, which lint-compose.ps1 enforces.
}

unattended() {
  export DEBIAN_FRONTEND=noninteractive
  dpkg -s unattended-upgrades >/dev/null 2>&1 || { apt-get install -y -q unattended-upgrades; changed "installed unattended-upgrades"; }
  local conf=/etc/apt/apt.conf.d/52-hrm-unattended
  local want='Unattended-Upgrade::Automatic-Reboot "false";
APT::Periodic::Update-Package-Lists "1";
APT::Periodic::Unattended-Upgrade "1";'
  if [ "$(cat "$conf" 2>/dev/null)" = "$want" ]; then unchanged "$conf (security updates, no automatic reboot)"; else printf '%s\n' "$want" > "$conf"; changed "$conf: unattended upgrades on, Automatic-Reboot false"; fi
  systemctl enable --now unattended-upgrades >/dev/null 2>&1 || true
}

timezone() {
  if [ "$(timedatectl show -p Timezone --value)" = "UTC" ]; then unchanged "timezone UTC"; else timedatectl set-timezone UTC; changed "timezone UTC"; fi
  say "NTP synchronized: $(timedatectl show -p NTPSynchronized --value)"
}

hrm_user() {
  if id hrm >/dev/null 2>&1; then unchanged "user hrm"; else useradd --create-home --shell /bin/bash hrm; passwd -l hrm >/dev/null; changed "user hrm (no password, locked for password login)"; fi
  if id -nG hrm | tr ' ' '\n' | grep -qx docker; then unchanged "hrm in group docker"; else usermod -aG docker hrm; changed "hrm added to group docker"; fi
  local sudoers=/etc/sudoers.d/90-hrm
  if [ "$(cat "$sudoers" 2>/dev/null)" != "hrm ALL=(ALL) NOPASSWD:ALL" ]; then
    printf 'hrm ALL=(ALL) NOPASSWD:ALL\n' > "$sudoers.tmp"
    chmod 440 "$sudoers.tmp"
    visudo -cf "$sudoers.tmp" >/dev/null
    mv "$sudoers.tmp" "$sudoers"
    changed "$sudoers (NOPASSWD, validated with visudo -cf)"
  else
    unchanged "$sudoers"
  fi
  install -d -m 700 -o hrm -g hrm /home/hrm/.ssh
  if [ -f /root/.ssh/authorized_keys ] && ! cmp -s /root/.ssh/authorized_keys /home/hrm/.ssh/authorized_keys; then
    install -m 600 -o hrm -g hrm /root/.ssh/authorized_keys /home/hrm/.ssh/authorized_keys
    changed "/home/hrm/.ssh/authorized_keys copied from root ($(wc -l < /home/hrm/.ssh/authorized_keys) key(s))"
  else
    unchanged "/home/hrm/.ssh/authorized_keys"
  fi
  local d
  for d in /opt/hrm /opt/hrm/releases /opt/hrm/shared; do
    if [ -d "$d" ] && [ "$(stat -c '%U:%a' "$d")" = "hrm:750" ]; then unchanged "$d (hrm, 750)"; else install -d -m 750 -o hrm -g hrm "$d"; changed "$d (hrm, 750)"; fi
  done
}

harden_ssh() {
  local want='# PLAN-36: key-only SSH, no root login. Named 00- so it is read first: sshd keeps the first value it finds.
PermitRootLogin no
PasswordAuthentication no
KbdInteractiveAuthentication no
PubkeyAuthentication yes'
  local backup="/root/sshd-backup-$STAMP" moved=0 f
  # Never lock everyone out: root login and passwords go away, so hrm must already be able to sign in with a key.
  if [ ! -s /home/hrm/.ssh/authorized_keys ]; then
    say "refusing: /home/hrm/.ssh/authorized_keys is missing or empty (run the base setup with a root key first)"; exit 1
  fi
  if ! sshd -T -C user=hrm,host=localhost,addr=127.0.0.1 | grep -qi '^pubkeyauthentication yes'; then
    say "refusing: sshd would not accept a public key for hrm"; exit 1
  fi
  mkdir -p "$backup"
  cp -a /etc/ssh/sshd_config "$backup/"
  cp -a /etc/ssh/sshd_config.d "$backup/" 2>/dev/null || true
  for f in /etc/ssh/sshd_config.d/*.conf; do
    [ -e "$f" ] || continue
    [ "$f" = "$HARDEN_FILE" ] && continue
    if grep -qiE '^\s*(PasswordAuthentication|KbdInteractiveAuthentication|PermitRootLogin)\s+yes' "$f"; then
      mv "$f" "$backup/moved-$(basename "$f")"; moved=$((moved + 1)); changed "moved $f to $backup (enabled passwords or root login)"
    fi
  done
  if [ "$(cat "$HARDEN_FILE" 2>/dev/null)" != "$want" ]; then printf '%s\n' "$want" > "$HARDEN_FILE"; chmod 644 "$HARDEN_FILE"; changed "$HARDEN_FILE"; else unchanged "$HARDEN_FILE"; fi
  if ! sshd -t; then
    say "sshd -t failed: restoring the backup"
    rm -f "$HARDEN_FILE"
    for f in "$backup"/moved-*; do [ -e "$f" ] && mv "$f" "/etc/ssh/sshd_config.d/${f##*/moved-}"; done
    exit 1
  fi
  systemctl reload ssh
  # A repeated run moves nothing; keep pointing at the backup that holds the original drop-ins.
  if [ "$moved" -gt 0 ] || [ ! -f /root/sshd-backup-latest ]; then echo "$backup" > /root/sshd-backup-latest; fi
  say "backup: $backup (undo: bash host-setup.sh --undo-harden-ssh)"
  say "effective: $(sshd -T | grep -Ei '^(permitrootlogin|passwordauthentication|kbdinteractiveauthentication|pubkeyauthentication) ' | tr '\n' ' ')"
}

undo_harden_ssh() {
  local backup f
  backup="$(cat /root/sshd-backup-latest 2>/dev/null || true)"
  rm -f "$HARDEN_FILE"
  if [ -n "$backup" ] && [ -d "$backup" ]; then
    for f in "$backup"/moved-*; do [ -e "$f" ] && mv "$f" "/etc/ssh/sshd_config.d/${f##*/moved-}" && changed "restored ${f##*/moved-}"; done
  fi
  sshd -t
  systemctl reload ssh
  changed "removed $HARDEN_FILE"
  say "effective: $(sshd -T | grep -Ei '^(permitrootlogin|passwordauthentication|kbdinteractiveauthentication|pubkeyauthentication) ' | tr '\n' ' ')"
}

registry() {
  # P-10 fallback only: outside the application compose project, loopback only, 128 MiB.
  install -d -m 750 -o hrm -g hrm /opt/hrm/registry
  if docker ps -a --format '{{.Names}}' | grep -qx hrm-registry; then unchanged "registry hrm-registry"; return; fi
  docker run -d --name hrm-registry --restart unless-stopped -p 127.0.0.1:5050:5000 --memory 128m \
    -v /opt/hrm/registry:/var/lib/registry registry:2.8.3 >/dev/null
  changed "registry:2.8.3 as hrm-registry on 127.0.0.1:5050 (data /opt/hrm/registry)"
}

case "${1:-}" in
  "")
    docker_engine; docker_daemon_json; swap; firewall; unattended; timezone; hrm_user
    say "done. Listening: $(ss -Htln | awk '{print $4}' | tr '\n' ' ')";;
  --harden-ssh) harden_ssh;;
  --undo-harden-ssh) undo_harden_ssh;;
  --with-registry) registry;;
  *) echo "usage: host-setup.sh [--harden-ssh | --undo-harden-ssh | --with-registry]" >&2; exit 2;;
esac
