#!/usr/bin/env bash
# PLAN-36, owner decision (c) — a documented deviation from plan §6 rule 1 ("no source or build tools on the server"):
# the owner's upload link (~0.16 MB/s) cannot carry the images (~810 MB of unique layers), so the server builds them
# from a ~31 MB source archive of the tagged commit, pushes them to its loopback registry (127.0.0.1:5050, P-10) and
# then removes the source, the build cache and the SDK image. The deploy itself still pulls from a registry.
#
#   bash build-on-host.sh <tag> <archive.tar.gz> [registry]      (as hrm; registry default 127.0.0.1:5050/hrmanagement)
#
# The archive is made on the owner's machine by scripts/vps/build-on-host.ps1 (git archive of the tag, without what
# .dockerignore keeps out of every build anyway). Prints "timing <name>=<seconds>" lines and writes
# /opt/hrm/build/images-<tag>.json (image, digest, size).
set -euo pipefail

TAG="${1:?usage: build-on-host.sh <tag> <archive.tar.gz> [registry]}"
ARCHIVE="${2:?archive required}"
REGISTRY="${3:-127.0.0.1:5050/hrmanagement}"
BATCH="${HRM_BUILD_BATCH:-4}"
[[ "$TAG" =~ ^[0-9a-f]{12}$ ]] || { echo "tag must be a 12-character commit SHA" >&2; exit 2; }
case "$REGISTRY" in *:latest|*/) echo "invalid registry $REGISTRY" >&2; exit 2;; esac

ROOT=/opt/hrm/build
SRC="$ROOT/src-$TAG"
REPORT="$ROOT/images-$TAG.json"
mkdir -p "$ROOT"
rm -rf "$SRC"
mkdir -p "$SRC"
cleanup() { rm -rf "$SRC" "$ROOT"/bake-"$TAG"-*.json; }
trap cleanup EXIT

t0=$(date +%s)
tar -xzf "$ARCHIVE" -C "$SRC"
rm -f "$ARCHIVE"

# The restore layer of docker/DotnetService.Dockerfile (what scripts/docker-generate-build-manifest.ps1 does on Windows).
(cd "$SRC" && find . \( -path '*/bin/*' -o -path '*/obj/*' -o -path '*/node_modules/*' -o -path '*/TestResults/*' \) -prune -o \
  -type f \( -name '*.csproj' -o -name packages.lock.json -o -name Directory.Build.props -o -name Directory.Packages.props \) -print0 \
  | while IFS= read -r -d '' f; do mkdir -p "docker/build-manifest/$(dirname "$f")"; cp "$f" "docker/build-manifest/$f"; done)
echo "build manifest: $(find "$SRC/docker/build-manifest" -type f | wc -l) project/lock files"

# Bake files in batches from docker/service-build-matrix.json.
python3 - "$SRC" "$REGISTRY" "$TAG" "$BATCH" "$ROOT" <<'PY'
import json, sys, os
src, registry, tag, batch, root = sys.argv[1], sys.argv[2], sys.argv[3], int(sys.argv[4]), sys.argv[5]
matrix = json.load(open(os.path.join(src, "docker", "service-build-matrix.json"), encoding="utf-8"))
for n, i in enumerate(range(0, len(matrix), batch), start=1):
    targets = {}
    for m in matrix[i:i + batch]:
        targets[m["service"]] = {
            "context": src, "dockerfile": "docker/DotnetService.Dockerfile", "platforms": ["linux/amd64"],
            "tags": [f"{registry}/{m['service']}:{tag}"],
            "args": {"PROJECT_CSPROJ": m["projectCsproj"], "ASSEMBLY_NAME": m["assemblyName"],
                     "NEEDS_KRB5": m["needsKrb5"], "GATEWAY_AUDIT_DIR": m["gatewayAuditDir"]},
            "labels": {"org.opencontainers.image.revision": tag, "org.opencontainers.image.source": "hrmanagement"}}
    json.dump({"group": {"default": {"targets": list(targets)}}, "target": targets},
              open(os.path.join(root, f"bake-{tag}-{n}.json"), "w"), indent=2)
PY

for bake in $(ls "$ROOT"/bake-"$TAG"-*.json | sort -V); do
  echo "[build] $(basename "$bake")"
  docker buildx bake --progress=plain -f "$bake" --load > "$ROOT/build-$TAG.log" 2>&1 || { tail -30 "$ROOT/build-$TAG.log"; exit 1; }
done
t1=$(date +%s); echo "timing build_seconds=$((t1 - t0))"

services=$(python3 -c "import json,sys; print(' '.join(m['service'] for m in json.load(open(sys.argv[1]))))" "$SRC/docker/service-build-matrix.json")
echo "[" > "$REPORT"; first=1
for s in $services; do
  ref="$REGISTRY/$s:$TAG"
  docker push -q "$ref" > /dev/null
  digest=$(docker inspect -f '{{index .RepoDigests 0}}' "$ref" | sed 's/.*@//')
  size=$(docker image inspect -f '{{.Size}}' "$ref")
  [ $first -eq 1 ] || echo "," >> "$REPORT"; first=0
  printf '  {"service":"%s","image":"%s","digest":"%s","sizeBytes":%s}' "$s" "$ref" "$digest" "$size" >> "$REPORT"
  echo "[push] $s $digest"
done
echo "]" >> "$REPORT"
t2=$(date +%s); echo "timing push_seconds=$((t2 - t1))"

# No build tools left behind: the build cache and the SDK image go; the runtime base stays (the images use it).
docker builder prune -af > /dev/null
docker images --format '{{.Repository}}:{{.Tag}} {{.ID}}' | awk '$1 ~ /dotnet\/sdk/ {print $2}' | xargs -r docker rmi -f > /dev/null
echo "cleanup: source, build cache and SDK image removed ($(docker system df --format '{{.Type}} {{.Size}}' | tr '\n' ';'))"
echo "report: $REPORT"
