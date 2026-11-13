#!/usr/bin/env bash
# The browser of the E2E job, installed while the stack starts (ci.yml, job e2e).
# The install took 32.9 to 52.3 minutes in runs 42301745454, 42301740446 and 42146523940, and every minute was apt: 181 packages,
# 112 MB, from azure.archive.ubuntu.com at 36 to 58 kB/s. The browsers themselves took about 15 s in every run. So:
#  - only the headless Chromium is installed, the one browser the tests launch; most of the 181 packages were for WebKit and Firefox;
#  - the .deb files are cached with the browsers, so a warm run takes nothing from that mirror. apt checks each cached file against
#    its index and downloads the ones that went stale, so the key needs no package versions;
#  - the install runs in the background, so even a cold cache costs the job little: the free-disk and stack steps take 10 minutes or more.
#   key    writes the cache key to the step output (Playwright version and runner image)
#   start  starts the install in the background and returns; needs the built E2E project
#   wait   waits for the install, prints its log and the time it took, and fails when it failed
#   run    the install itself, what "start" runs
set -euo pipefail

playwright=src/Tests/HRManagement.E2E/bin/Release/net10.0/playwright.ps1
debs="$HOME/.cache/playwright-debs"
archives=/var/cache/apt/archives
work="${RUNNER_TEMP:-/tmp}/playwright-install"

case "${1:-}" in
  key)
    version=$(sed -n 's/.*Include="Microsoft\.Playwright" Version="\([^"]*\)".*/\1/p' Directory.Packages.props)
    [ -n "$version" ] || { echo "::error::no Microsoft.Playwright version in Directory.Packages.props"; exit 1; }
    echo "key=playwright-shell-${ImageOS:-linux}-${version}" >> "$GITHUB_OUTPUT"
    ;;
  start)
    mkdir -p "$work" "$debs"
    date +%s > "$work/started"
    nohup bash "$0" run > "$work/install.log" 2>&1 < /dev/null &
    echo "$!" > "$work/pid"
    echo "Playwright install started in the background (process $!)"
    ;;
  run)
    shopt -s nullglob
    cached=("$debs"/*.deb)
    echo "Cached .deb files: ${#cached[@]}"
    if [ "${#cached[@]}" -gt 0 ]; then
      sudo cp "${cached[@]}" "$archives"/
    fi
    code=0
    pwsh "$playwright" install --with-deps --only-shell chromium || code=$?
    kept=("$archives"/*.deb)
    echo ".deb files in ${archives}: ${#kept[@]}"
    if [ "${#kept[@]}" -gt 0 ]; then
      cp "${kept[@]}" "$debs"/ || echo "::warning::could not keep the .deb files for the cache"
    fi
    date +%s > "$work/finished"
    echo "$code" > "$work/exit-code"
    ;;
  wait)
    joined=$(date +%s)
    while [ ! -f "$work/exit-code" ] && kill -0 "$(cat "$work/pid")" 2>/dev/null; do
      sleep 2
    done
    now=$(date +%s)
    echo "::group::Playwright install log"
    cat "$work/install.log"
    echo "::endgroup::"
    [ -f "$work/exit-code" ] || { echo "::error::the Playwright install ended without an exit code"; exit 1; }
    code=$(cat "$work/exit-code")
    [ "$code" = 0 ] || { echo "::error::the Playwright install failed with exit code ${code}"; exit 1; }
    took=$(($(cat "$work/finished") - $(cat "$work/started")))
    line="Playwright install: ${took} s in the background, $((now - joined)) s of waiting for it, cache hit: ${CACHE_HIT:-false}"
    echo "$line"
    echo "$line" >> "${GITHUB_STEP_SUMMARY:-/dev/null}"
    ;;
  *)
    echo "usage: playwright-install.sh key|start|wait" >&2
    exit 2
    ;;
esac
