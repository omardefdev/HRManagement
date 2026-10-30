#!/usr/bin/env bash
# PLAN-35 (G-QA-15): actionlint (with shellcheck) on .github/workflows/*.yml, from the image pinned by digest in
# docs/qa/tool-pins.json. The workflows go into the container over stdin (tar), so the step also runs inside the
# ci-local container, where host paths cannot be bind-mounted.
set -euo pipefail
ref=$(grep -o '"ref": "rhysd/actionlint@sha256:[0-9a-f]*"' docs/qa/tool-pins.json | cut -d'"' -f4)
[ -n "$ref" ] || { echo "::error::no actionlint pin in docs/qa/tool-pins.json"; exit 1; }
echo "actionlint ${ref}"
tar -c .github/workflows | docker run --rm -i --entrypoint sh "${ref}" -c \
  'd=$(mktemp -d) && cd "$d" && tar -x && actionlint -no-color -oneline .github/workflows/*.yml'
echo "actionlint: no findings"
