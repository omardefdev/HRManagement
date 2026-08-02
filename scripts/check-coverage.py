#!/usr/bin/env python3
"""QA-002: Merge Cobertura reports with union line hits; enforce global gate + floors.

Union merge: for each source line, a line is covered if any report records hits > 0.
Summing covered/valid across overlapping reports (e.g. unit + integration + TC) would
inflate valid line counts and fail the gate even when combined test runs exercise code.
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
            if "TestResults" not in p.parts:
                continue
            # Coverlet also writes intermediate copies under _runner*/In/…; skip them so
            # additive merge does not double-count valid lines (QA-002 gate inflation bug).
            if any(part.startswith("_runner") for part in p.parts):
                continue
            files.append(p)
    return sorted(files)


def package_stats(coverage_files: list[Path]) -> dict[str, tuple[int, int]]:
    """Union merge: max hits per (file, line) across reports; count unique lines once."""
    merged: dict[str, dict[tuple[str, int], int]] = {}
    for path in coverage_files:
        root = ET.parse(path).getroot()
        for pkg in root.findall(".//package"):
            name = pkg.get("name") or ""
            if name not in merged:
                merged[name] = {}
            for cls in pkg.findall("classes/class"):
                filename = cls.get("filename") or ""
                lines_el = cls.find("lines")
                if lines_el is None:
                    continue
                for ln in lines_el.findall("line"):
                    num = int(ln.get("number") or 0)
                    hits = int(ln.get("hits") or 0)
                    key = (filename, num)
                    merged[name][key] = max(merged[name].get(key, 0), hits)
    return {
        name: (sum(1 for h in hits.values() if h > 0), len(hits))
        for name, hits in merged.items()
    }


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

    print(f"Coverage files ({len(files)}; union merge):")
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
