#!/usr/bin/env bash
# PLAN-35 (triage rule 1): the first PR run of CI died with "No space left on device" while building the service images
# (run 41083926373). GitHub-hosted ubuntu runners keep ~14 GB free; this removes preinstalled toolchains the jobs never
# use (Android SDK, GHC, Swift, the CodeQL bundle of the tool cache, Boost; /usr/share/dotnet stays) and the preloaded
# Docker images. A no-op outside GitHub Actions (ci-local).
set -euo pipefail
if [ "${GITHUB_ACTIONS:-}" != "true" ] || [ "${RUNNER_OS:-}" != "Linux" ] || [ "${RUNNER_ENVIRONMENT:-github-hosted}" != "github-hosted" ]; then
  echo "free-runner-disk: not a GitHub-hosted Linux runner - nothing to do"
  exit 0
fi
echo "Before:"; df -h / | tail -1
sudo rm -rf /usr/local/lib/android /opt/ghc /usr/local/.ghcup /usr/share/swift /opt/hostedtoolcache/CodeQL /usr/local/share/boost || true
docker image prune --all --force > /dev/null || true
echo "After:"; df -h / | tail -1
