#!/usr/bin/env python3
"""QA-002: Merge Cobertura reports by summing lines; enforce global gate + floors.

Uses additive merge (covered/valid summed per assembly across reports), not
best-of selection, so a stale high-coverage artifact cannot mask a low current run.
"""
from __future__ import annotations

import json
import os
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def find_coverage_files(explicit: str | None) -> list[Path]:
    if explicit:
        p = Path(explicit)
        if not p.is_file():
            print(f"ERROR: Coverage file not found: {p}", file=sys.stderr)
            sys.exit(1)
        return [p]
    roots = [Path("TestResults")] if Path("TestResults").is_dir() else [Path(".")]
    files: list[Path] = []
    for root in roots:
        for p in root.rglob("coverage.cobertura.xml"):
            if "TestResults" in p.parts:
                files.append(p)
    return sorted(files)


def package_stats(coverage_files: list[Path]) -> dict[str, tuple[int, int]]:
    """Sum covered/valid line counts per package across reports (additive merge)."""
    totals: dict[str, list[int]] = {}
    for path in coverage_files:
        root = ET.parse(path).getroot()
        for pkg in root.findall(".//package"):
            name = pkg.get("name") or ""
            lines = pkg.findall(".//line")
            if not lines:
                continue
            covered = sum(1 for ln in lines if int(ln.get("hits") or 0) > 0)
            valid = len(lines)
            if name not in totals:
                totals[name] = [0, 0]
            totals[name][0] += covered
            totals[name][1] += valid
    return {k: (v[0], v[1]) for k, v in totals.items()}


def load_floors(path: Path) -> dict:
    if not path.is_file():
        print(f"ERROR: coverage floors file not found: {path}", file=sys.stderr)
        sys.exit(1)
    return json.loads(path.read_text(encoding="utf-8"))


def main() -> int:
    explicit = sys.argv[1] if len(sys.argv) > 1 else None
    floors_path = Path(os.environ.get("COVERAGE_FLOORS_FILE", "coverage-floors.json"))
    cfg = load_floors(floors_path)

    env_threshold = os.environ.get("COVERAGE_THRESHOLD")
    global_threshold = float(env_threshold) if env_threshold else float(cfg.get("globalThreshold", 60))
    gate_assemblies: list[str] = list(cfg.get("gateAssemblies") or [])
    floors: dict[str, float] = {k: float(v) for k, v in (cfg.get("floors") or {}).items()}

    files = find_coverage_files(explicit)
    if not files:
        print("ERROR: No coverage.cobertura.xml found. Coverage gate FAILED.", file=sys.stderr)
        return 1

    packages = package_stats(files)
    if not packages:
        print("ERROR: No packages parsed from coverage files.", file=sys.stderr)
        return 1

    print(f"Coverage files ({len(files)}; additive merge):")
    for f in files:
        print(f"  {f}")

    g_cov = g_valid = 0
    missing_gate: list[str] = []
    for name in gate_assemblies:
        if name not in packages:
            missing_gate.append(name)
            continue
        c, v = packages[name]
        g_cov += c
        g_valid += v
        pct = round(100.0 * c / v, 1) if v else 0.0
        print(f"  gate {name}: {pct}% ({c}/{v})")

    if missing_gate:
        print(
            "ERROR: gateAssemblies missing from coverage report: " + ", ".join(missing_gate),
            file=sys.stderr,
        )
        return 1
    if g_valid <= 0:
        print("ERROR: Combined gate coverage has no valid lines.", file=sys.stderr)
        return 1

    global_pct = round(100.0 * g_cov / g_valid, 1)
    print(
        f"Gate line coverage: {global_pct}% (threshold: {global_threshold}%) "
        f"from {len(gate_assemblies)} assembly(ies), covered lines {g_cov}/{g_valid}"
    )
    failed = False
    if global_pct < global_threshold:
        print(
            f"ERROR: Gate coverage {global_pct}% is below threshold {global_threshold}%",
            file=sys.stderr,
        )
        failed = True

    print("Assembly floors:")
    for name, floor in sorted(floors.items()):
        if name not in packages:
            print(f"  {name}: (not in report — skip)")
            continue
        c, v = packages[name]
        pct = round(100.0 * c / v, 1) if v else 0.0
        status = "OK" if pct >= floor else "FAIL"
        print(f"  {name}: {pct}% (floor {floor}%) [{status}]")
        if pct < floor:
            print(
                f"ERROR: Assembly {name} coverage {pct}% is below floor {floor}%",
                file=sys.stderr,
            )
            failed = True

    if failed:
        return 1
    print("Coverage gate passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
