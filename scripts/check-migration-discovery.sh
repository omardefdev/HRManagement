#!/usr/bin/env bash
# Every EF migration must carry [Migration("<id>")] and [DbContext(typeof(...))], either on the
# migration class itself or on a sibling .Designer.cs partial. EF discovers migrations through
# that attribute only: a migration without it compiles, ships, and is silently never applied.
#
# This exists because OBS003 (creates security_audit_events) and EVD003 shipped without it, so the
# gateway ran with a missing table while /health/ready still reported 200.
set -euo pipefail

fail=0
checked=0

# Migration files live next to a ModelSnapshot; that is what identifies a migrations folder.
while IFS= read -r snapshot; do
  dir="$(dirname "$snapshot")"

  while IFS= read -r migration; do
    base="$(basename "$migration" .cs)"

    # Skip snapshots and designer files themselves.
    case "$base" in
      *ModelSnapshot|*.Designer) continue ;;
    esac

    # A migration file name is always <timestamp>_<Name>.
    if [[ ! "$base" =~ ^[0-9]{14}_ ]]; then
      continue
    fi

    checked=$((checked + 1))
    designer="${dir}/${base}.Designer.cs"

    has_migration_attr=0
    has_dbcontext_attr=0

    for candidate in "$migration" "$designer"; do
      [ -f "$candidate" ] || continue
      if grep -q '\[Migration(' "$candidate"; then has_migration_attr=1; fi
      if grep -q '\[DbContext(' "$candidate"; then has_dbcontext_attr=1; fi
    done

    if [ "$has_migration_attr" -eq 0 ] || [ "$has_dbcontext_attr" -eq 0 ]; then
      echo "FAIL: ${migration#./} is not discoverable by EF (missing [Migration] and/or [DbContext])." >&2
      fail=1
    fi
  done < <(find "$dir" -maxdepth 1 -name '*.cs' | sort)
done < <(find src -name '*ModelSnapshot.cs' | sort)

if [ "$fail" -ne 0 ]; then
  echo "" >&2
  echo "Add the attributes to the migration class (or its .Designer.cs partial):" >&2
  echo '  [DbContext(typeof(YourDbContext))]' >&2
  echo '  [Migration("<timestamp>_<Name>")]' >&2
  exit 1
fi

echo "OK: all ${checked} migration(s) are discoverable by EF."
