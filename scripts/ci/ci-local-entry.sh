#!/usr/bin/env bash
# QA-034: entry point inside the ci-local container (started by scripts/ci-local.ps1).
#   /src        = the repository on the host (bind mount)
#   /work/repo  = the tree under test: a clone of the committed HEAD, optionally overlaid with the working tree
# Env: CI_LOCAL_SOURCE (head-clone | working-tree), CI_LOCAL_OUT (repo-relative output dir),
#      CI_LOCAL_ARGS (arguments for run-steps.sh, shell-quoted).
set -uo pipefail
# bash reads a script while it runs: run from a private copy so editing /src during a long run cannot break this one
if [[ -z "${CI_LOCAL_REEXEC:-}" ]]; then
  cp "$0" /tmp/ci-local-entry.sh
  CI_LOCAL_REEXEC=1 exec bash /tmp/ci-local-entry.sh "$@"
fi
: "${CI_LOCAL_SOURCE:=head-clone}"
: "${CI_LOCAL_OUT:=artifacts/ci-local}"
: "${CI_LOCAL_ARGS:=--all}"

git config --global --add safe.directory '*'
# Parity with the GitHub runner, which is a VM: the SDK image sets DOTNET_RUNNING_IN_CONTAINER=true, and the web hosts
# then enforce container-only settings (e.g. "HrGateway:InternalBaseUrl must use Docker service DNS") when the EF tools
# build them at design time.
unset DOTNET_RUNNING_IN_CONTAINER
# Testcontainers reaches the sibling containers through their published ports via host.docker.internal. Its IPv6
# address is not routable from here ("Network is unreachable"), so the IPv4 address is used. On Docker Desktop this
# path still goes through the desktop port proxy and PostgreSQL connections time out now and then under load (the
# bridge gateway does not work either: Ryuk is unreachable there). Evidence runs therefore use -SkipTestcontainers
# and run the Testcontainers suite natively on the host (PLAN-34 rule P-3); GitHub runners are not affected.
if [[ "${TESTCONTAINERS_HOST_OVERRIDE:-}" == "host.docker.internal" ]]; then
  v4="$(getent ahostsv4 host.docker.internal | awk 'NR==1{print $1}')"
  [[ -n "$v4" ]] && export TESTCONTAINERS_HOST_OVERRIDE="$v4"
fi
echo "ci-local: TESTCONTAINERS_HOST_OVERRIDE=${TESTCONTAINERS_HOST_OVERRIDE:-unset}"
rm -rf /work/repo /work/out
git clone --quiet --no-hardlinks /src /work/repo || { echo "ci-local: git clone of /src failed"; exit 90; }
cd /work/repo
if [[ "$CI_LOCAL_SOURCE" == "working-tree" ]]; then
  # only what the host's git reports as changed: modified tracked files and untracked (not ignored) files are
  # copied over the clone of HEAD, deleted tracked files are removed
  # CI_SOURCE_AUTOCRLF: the host checkout's core.autocrlf (Windows: true), else CRLF files look modified here
  (cd /src && git -c core.autocrlf="${CI_SOURCE_AUTOCRLF:-false}" ls-files -z --modified --others --exclude-standard) \
    | python3 -c "import os,sys; d=sys.stdin.buffer.read().split(b'\0'); sys.stdout.buffer.write(b'\0'.join(p for p in d if p and os.path.isfile(os.path.join(b'/src', p))))" \
    | (cd /src && tar --null -T - -cf -) | tar -xf - -C /work/repo || { echo "ci-local: working-tree overlay failed"; exit 91; }
  (cd /src && git -c core.autocrlf="${CI_SOURCE_AUTOCRLF:-false}" ls-files -z --deleted) | (cd /work/repo && xargs -0 -r rm -f --)
  # D-QA-12: CI always uses the committed global.json (a local rollForward edit is reported, never tested)
  git checkout --quiet HEAD -- global.json
  # the copied files come from the Windows checkout (CRLF, no mode bits); compare them with HEAD as the host does
  git config core.autocrlf true
  git config core.fileMode false
fi
git config core.quotepath false
echo "ci-local: source=$CI_LOCAL_SOURCE sha=$(git rev-parse HEAD) dotnet=$(dotnet --version)"

eval "set -- $CI_LOCAL_ARGS"
bash scripts/ci/run-steps.sh "$@" --out /work/out
rc=$?

dest="/src/$CI_LOCAL_OUT"
# a fresh output folder, so results of an earlier run never survive into this one (only ever under artifacts/)
case "$CI_LOCAL_OUT" in
  artifacts/?*) [[ "$CI_LOCAL_OUT" != *..* ]] && rm -rf "${dest:?}" ;;
  *) echo "ci-local: -Out must be a folder under artifacts/ (got '$CI_LOCAL_OUT'); not clearing it" ;;
esac
mkdir -p "$dest"
cp -r /work/out/. "$dest/" 2>/dev/null
for d in TestResults coveragereport; do
  if [[ -d "$d" ]]; then rm -rf "${dest:?}/$d"; cp -r "$d" "$dest/$d"; fi
done
[[ -f vuln-report.txt ]] && cp vuln-report.txt "$dest/"
# the summary points into the container; point it at the copy
[[ -f "$dest/summary.json" ]] && sed -i "s#/work/out/#$CI_LOCAL_OUT/#g" "$dest/summary.json"
exit $rc
