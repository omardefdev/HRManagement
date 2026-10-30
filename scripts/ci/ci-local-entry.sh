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
rm -rf /work/repo /work/out
git clone --quiet --no-hardlinks /src /work/repo || { echo "ci-local: git clone of /src failed"; exit 90; }
cd /work/repo
if [[ "$CI_LOCAL_SOURCE" == "working-tree" ]]; then
  # only what the host's git reports as changed: modified tracked files and untracked (not ignored) files are
  # copied over the clone of HEAD, deleted tracked files are removed
  (cd /src && git ls-files -z --modified --others --exclude-standard) \
    | python3 -c "import os,sys; d=sys.stdin.buffer.read().split(b'\0'); sys.stdout.buffer.write(b'\0'.join(p for p in d if p and os.path.isfile(os.path.join(b'/src', p))))" \
    | (cd /src && tar --null -T - -cf -) | tar -xf - -C /work/repo || { echo "ci-local: working-tree overlay failed"; exit 91; }
  (cd /src && git ls-files -z --deleted) | (cd /work/repo && xargs -0 -r rm -f --)
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
mkdir -p "$dest"
cp -r /work/out/. "$dest/" 2>/dev/null
for d in TestResults coveragereport; do
  if [[ -d "$d" ]]; then rm -rf "${dest:?}/$d"; cp -r "$d" "$dest/$d"; fi
done
[[ -f vuln-report.txt ]] && cp vuln-report.txt "$dest/"
# the summary points into the container; point it at the copy
[[ -f "$dest/summary.json" ]] && sed -i "s#/work/out/#$CI_LOCAL_OUT/#g" "$dest/summary.json"
exit $rc
